using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace FallenSec.Core;

public readonly record struct FlowKey(int Pid, bool Udp, IPAddress Remote, int RemotePort, int LocalPort);

public sealed class FlowCounter { public long Up, Down; }

/// <summary>Trafic per proces/conexiune via ETW (kernel TCP/IP) + răspunsuri DNS (DNS-Client).</summary>
public sealed class TrafficMonitor : IDisposable
{
    ConcurrentDictionary<FlowKey, FlowCounter> _flows = new();
    TraceEventSession? _kernel, _dns;
    HashSet<IPAddress> _localIps = new();
    public bool KernelOk { get; private set; }
    public bool DnsOk { get; private set; }
    public string? Error { get; private set; }

    /// <summary>(nume domeniu, IP-uri)</summary>
    public event Action<string, List<IPAddress>>? DnsResolved;

    static readonly Guid DnsClientProvider = new("1C95126E-7EEA-49A9-A3FE-A378B03DDB4D");

    public void Start()
    {
        RefreshLocalIps();
        NetworkChange.NetworkAddressChanged += (_, _) => RefreshLocalIps();
        new Thread(RunKernel) { IsBackground = true, Name = "etw-kernel" }.Start();
        new Thread(RunDns) { IsBackground = true, Name = "etw-dns" }.Start();
    }

    void RefreshLocalIps()
    {
        var s = new HashSet<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                foreach (var u in ni.GetIPProperties().UnicastAddresses)
                    s.Add(Native.Norm(u.Address));
        }
        catch { }
        _localIps = s;
    }

    void RunKernel()
    {
        try
        {
            if (!(TraceEventSession.IsElevated() ?? false)) { Error = L.T("Fără drepturi de administrator – vitezele nu sunt disponibile.", "No administrator rights – speeds are not available."); return; }
            try
            {
                _kernel = new TraceEventSession("FallenSec-Net") { StopOnDispose = true };
                _kernel.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
            }
            catch (Exception e1)
            {
                Log.W("Kernel session custom name failed: " + e1.Message);
                _kernel?.Dispose();
                _kernel = new TraceEventSession(KernelTraceEventParser.KernelSessionName) { StopOnDispose = true };
                _kernel.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
            }
            var k = _kernel.Source.Kernel;
            k.TcpIpSend += d => Add(d.ProcessID, false, d.saddr, d.sport, d.daddr, d.dport, d.size, true);
            k.TcpIpRecv += d => Add(d.ProcessID, false, d.saddr, d.sport, d.daddr, d.dport, d.size, false);
            k.TcpIpSendIPV6 += d => Add(d.ProcessID, false, d.saddr, d.sport, d.daddr, d.dport, d.size, true);
            k.TcpIpRecvIPV6 += d => Add(d.ProcessID, false, d.saddr, d.sport, d.daddr, d.dport, d.size, false);
            k.UdpIpSend += d => Add(d.ProcessID, true, d.saddr, d.sport, d.daddr, d.dport, d.size, true);
            k.UdpIpRecv += d => Add(d.ProcessID, true, d.saddr, d.sport, d.daddr, d.dport, d.size, false);
            k.UdpIpSendIPV6 += d => Add(d.ProcessID, true, d.saddr, d.sport, d.daddr, d.dport, d.size, true);
            k.UdpIpRecvIPV6 += d => Add(d.ProcessID, true, d.saddr, d.sport, d.daddr, d.dport, d.size, false);
            KernelOk = true;
            _kernel.Source.Process();
        }
        catch (Exception e)
        {
            Error = "ETW kernel: " + e.Message;
            Log.W(Error);
        }
        finally { KernelOk = false; }
    }

    void Add(int pid, bool udp, IPAddress saddr, int sport, IPAddress daddr, int dport, int size, bool send)
    {
        if (size <= 0) return;
        saddr = Native.Norm(saddr); daddr = Native.Norm(daddr);
        // la ETW TCP/IP: saddr = local, daddr = remote. Ne asigurăm.
        IPAddress remote = daddr; int rport = dport, lport = sport;
        if (_localIps.Contains(daddr) && !_localIps.Contains(saddr) && !IPAddress.IsLoopback(daddr))
        { remote = saddr; rport = sport; lport = dport; }
        var c = _flows.GetOrAdd(new FlowKey(pid, udp, remote, rport, lport), _ => new FlowCounter());
        if (send) Interlocked.Add(ref c.Up, size); else Interlocked.Add(ref c.Down, size);
    }

    void RunDns()
    {
        try
        {
            if (!(TraceEventSession.IsElevated() ?? false)) return;
            _dns = new TraceEventSession("FallenSec-Dns") { StopOnDispose = true };
            _dns.EnableProvider(DnsClientProvider, TraceEventLevel.Verbose);
            var parser = new RegisteredTraceEventParser(_dns.Source);
            parser.All += e =>
            {
                if ((int)e.ID != 3008) return;
                try
                {
                    var name = e.PayloadByName("QueryName") as string;
                    var res = e.PayloadByName("QueryResults") as string;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(res)) return;
                    var ips = new List<IPAddress>();
                    foreach (var tok in res.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var t = tok.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase) ? tok[7..] : tok;
                        if (IPAddress.TryParse(t, out var ip)) ips.Add(Native.Norm(ip));
                    }
                    if (ips.Count > 0) DnsResolved?.Invoke(name.TrimEnd('.').ToLowerInvariant(), ips);
                }
                catch { }
            };
            DnsOk = true;
            _dns.Source.Process();
        }
        catch (Exception e) { Log.W("ETW DNS: " + e.Message); }
        finally { DnsOk = false; }
    }

    /// <summary>Returnează contorii acumulați de la ultimul apel și îi resetează.</summary>
    public ConcurrentDictionary<FlowKey, FlowCounter> Drain() => Interlocked.Exchange(ref _flows, new());

    public void Dispose()
    {
        try { _kernel?.Dispose(); } catch { }
        try { _dns?.Dispose(); } catch { }
    }
}
