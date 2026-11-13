using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace FallenSec.Core;

/// <summary>Un motor de blocare: Windows Filtering Platform sau Windows Firewall.</summary>
public interface IBlockBackend
{
    string Name { get; }
    /// <summary>nume regulă → descriere (conține hash-ul specificației)</summary>
    Dictionary<string, string> ListOurRules();
    void Add(FwSpec s);
    void Remove(string name);
}

/// <summary>
/// Blocare direct prin Windows Filtering Platform (filtre persistente, provider + sublayer FallenSec).
/// Funcționează și când Windows Firewall e oprit (ex. Bitdefender/ESET/Kaspersky au preluat firewall-ul),
/// pentru că BFE (Base Filtering Engine) rulează mereu, iar un BLOCK din orice sublayer câștigă.
/// </summary>
public sealed class WfpService : IBlockBackend, IDisposable
{
    public string Name => "WFP";
    public static readonly Guid ProviderKey = new("6f1b9c2e-3a4d-4c8e-9b1f-fa11e05ec001");
    public static readonly Guid SubLayerKey = new("6f1b9c2e-3a4d-4c8e-9b1f-fa11e05ec002");

    static readonly Guid LayerConnectV4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    static readonly Guid LayerConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    static readonly Guid LayerRecvV4 = new("e1cd9fe7-f4b5-4273-96c0-592e487b8650");
    static readonly Guid LayerRecvV6 = new("a3b42c97-9f04-4672-b87e-cee9c483257f");
    static readonly Guid CondAppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");
    static readonly Guid CondRemoteAddr = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
    static readonly Guid CondRemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");
    static readonly Guid CondProtocol = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");

    const uint FWP_UINT8 = 1, FWP_UINT16 = 2, FWP_UINT32 = 3, FWP_BYTE_ARRAY16 = 11, FWP_BYTE_BLOB = 12,
               FWP_V6_ADDR_MASK = 0x101, FWP_RANGE = 0x102;
    const uint MATCH_EQUAL = 0, MATCH_RANGE = 5;
    const uint ACTION_BLOCK = 0x1001;
    const uint FILTER_PERSISTENT = 0x1, FILTER_CLEAR_ACTION_RIGHT = 0x8;
    const uint ERR_ALREADY_EXISTS = 0x80320009;

    readonly object _lock = new();
    IntPtr _engine;
    readonly Dictionary<string, List<Guid>> _byName = new();
    readonly Dictionary<string, string> _descByName = new();
    readonly Dictionary<string, byte[]> _appIdCache = new(StringComparer.OrdinalIgnoreCase);
    bool _listed;

    // ---------------------------------------------------------------- P/Invoke
    [DllImport("fwpuclnt.dll")] static extern uint FwpmEngineOpen0(string? serverName, uint authnService, IntPtr authIdentity, IntPtr session, out IntPtr engine);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmEngineClose0(IntPtr engine);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionBegin0(IntPtr engine, uint flags);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionCommit0(IntPtr engine);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionAbort0(IntPtr engine);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmProviderAdd0(IntPtr engine, IntPtr provider, IntPtr sd);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmSubLayerAdd0(IntPtr engine, IntPtr subLayer, IntPtr sd);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterAdd0(IntPtr engine, IntPtr filter, IntPtr sd, out ulong id);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterDeleteByKey0(IntPtr engine, ref Guid key);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterCreateEnumHandle0(IntPtr engine, IntPtr template, out IntPtr handle);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterEnum0(IntPtr engine, IntPtr handle, uint requested, out IntPtr entries, out uint returned);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterDestroyEnumHandle0(IntPtr engine, IntPtr handle);
    [DllImport("fwpuclnt.dll")] static extern void FwpmFreeMemory0(ref IntPtr p);
    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)] static extern uint FwpmGetAppIdFromFileName0(string fileName, out IntPtr appId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint QueryDosDevice(string device, StringBuilder target, int max);

    static void Check(uint r, string what)
    {
        if (r != 0) throw new Win32Exception2(r, what);
    }

    sealed class Win32Exception2 : Exception
    {
        public uint Code { get; }
        public Win32Exception2(uint code, string what) : base($"{what}: 0x{code:X8} {Describe(code)}") { Code = code; }
        static string Describe(uint c) => c switch
        {
            5 => L.T("acces refuzat (necesită administrator)", "access denied (administrator required)"),
            0x80320009 => L.T("există deja", "already exists"),
            0x80320001 => L.T("callout inexistent", "callout not found"),
            0x80320002 => L.T("condiție invalidă", "invalid condition"),
            0x80320003 => L.T("filtru inexistent", "filter not found"),
            0x8032000A => L.T("tip de date invalid pentru condiție", "invalid data type for condition"),
            0x80320012 => L.T("prea multe condiții", "too many conditions"),
            0x80320025 => L.T("tip de potrivire invalid", "invalid match type"),
            0x8032000D => L.T("intervale invalide", "invalid ranges"),
            0x80320035 => L.T("BFE nu rulează", "BFE is not running"),
            0x800706D9 => L.T("serviciul BFE (Base Filtering Engine) nu rulează", "the BFE (Base Filtering Engine) service is not running"),
            _ => ""
        };
    }

    // ---------------------------------------------------------------- inițializare
    /// <summary>Deschide sesiunea WFP și înregistrează provider-ul și sublayer-ul FallenSec (persistente).</summary>
    public void Open()
    {
        lock (_lock)
        {
            if (_engine != IntPtr.Zero) return;
            Check(FwpmEngineOpen0(null, 10 /*RPC_C_AUTHN_WINNT*/, IntPtr.Zero, IntPtr.Zero, out _engine), "FwpmEngineOpen0");
            using var mem = new Mem();
            // FWPM_PROVIDER0 (64 B): key, displayData{name,desc}, flags, providerData{size,ptr}, serviceName
            var prov = mem.Alloc(64);
            WriteGuid(prov, 0, ProviderKey);
            Marshal.WriteIntPtr(prov, 16, mem.Str("FallenSec"));
            Marshal.WriteIntPtr(prov, 24, mem.Str("FallenSec - firewall per aplicație"));
            Marshal.WriteInt32(prov, 32, 1); // FWPM_PROVIDER_FLAG_PERSISTENT
            uint r = FwpmProviderAdd0(_engine, prov, IntPtr.Zero);
            if (r != 0 && r != ERR_ALREADY_EXISTS) Check(r, "FwpmProviderAdd0");
            // FWPM_SUBLAYER0 (72 B): key, displayData, flags, providerKey*, providerData, weight(uint16)
            var sl = mem.Alloc(72);
            WriteGuid(sl, 0, SubLayerKey);
            Marshal.WriteIntPtr(sl, 16, mem.Str("FallenSec"));
            Marshal.WriteIntPtr(sl, 24, mem.Str("Reguli de blocare FallenSec"));
            Marshal.WriteInt32(sl, 32, 1); // FWPM_SUBLAYER_FLAG_PERSISTENT
            Marshal.WriteIntPtr(sl, 40, mem.Guid(ProviderKey));
            Marshal.WriteInt16(sl, 64, unchecked((short)0xF000));
            r = FwpmSubLayerAdd0(_engine, sl, IntPtr.Zero);
            if (r != 0 && r != ERR_ALREADY_EXISTS) Check(r, "FwpmSubLayerAdd0");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_engine != IntPtr.Zero) { FwpmEngineClose0(_engine); _engine = IntPtr.Zero; }
        }
    }

    // ---------------------------------------------------------------- listare / ștergere
    public Dictionary<string, string> ListOurRules()
    {
        lock (_lock)
        {
            Enumerate();
            return new Dictionary<string, string>(_descByName);
        }
    }

    /// <summary>Toate filtrele noastre (identificate prin provider sau sublayer), grupate după nume.</summary>
    void Enumerate()
    {
        _byName.Clear(); _descByName.Clear();
        Check(FwpmFilterCreateEnumHandle0(_engine, IntPtr.Zero, out var h), "FwpmFilterCreateEnumHandle0");
        try
        {
            while (true)
            {
                Check(FwpmFilterEnum0(_engine, h, 500, out var entries, out uint n), "FwpmFilterEnum0");
                try
                {
                    for (int i = 0; i < n; i++)
                    {
                        var f = Marshal.ReadIntPtr(entries, i * IntPtr.Size);
                        var pk = Marshal.ReadIntPtr(f, 40);
                        bool ours = (pk != IntPtr.Zero && ReadGuid(pk, 0) == ProviderKey) || ReadGuid(f, 80) == SubLayerKey;
                        if (!ours) continue;
                        var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(f, 16)) ?? "";
                        var desc = Marshal.PtrToStringUni(Marshal.ReadIntPtr(f, 24)) ?? "";
                        if (!_byName.TryGetValue(name, out var l)) _byName[name] = l = new();
                        l.Add(ReadGuid(f, 0));
                        // dacă nu toate filtrele unui nume au aceeași descriere, forțăm re-crearea
                        _descByName[name] = _descByName.TryGetValue(name, out var d0) && d0 != desc ? "" : desc;
                    }
                }
                finally { if (entries != IntPtr.Zero) FwpmFreeMemory0(ref entries); }
                if (n < 500) break;
            }
        }
        finally { FwpmFilterDestroyEnumHandle0(_engine, h); }
        _listed = true;
    }

    public void Remove(string name)
    {
        lock (_lock)
        {
            if (!_listed || !_byName.ContainsKey(name)) Enumerate();
            if (!_byName.TryGetValue(name, out var keys)) return;
            Check(FwpmTransactionBegin0(_engine, 0), "FwpmTransactionBegin0");
            try
            {
                foreach (var k in keys)
                {
                    var key = k;
                    uint r = FwpmFilterDeleteByKey0(_engine, ref key);
                    if (r != 0 && r != 0x80320003) Check(r, "FwpmFilterDeleteByKey0");
                }
                Check(FwpmTransactionCommit0(_engine), "FwpmTransactionCommit0");
            }
            catch { FwpmTransactionAbort0(_engine); throw; }
            _byName.Remove(name); _descByName.Remove(name);
        }
    }

    /// <summary>Șterge tot ce aparține FallenSec din WFP (filtre; provider-ul/sublayer-ul rămân, sunt goale).</summary>
    public int RemoveAll()
    {
        lock (_lock)
        {
            Enumerate();
            int n = 0;
            foreach (var name in _byName.Keys.ToList()) { Remove(name); n++; }
            return n;
        }
    }

    // ---------------------------------------------------------------- adăugare
    public void Add(FwSpec s)
    {
        lock (_lock)
        {
            byte[]? appId = string.IsNullOrEmpty(s.App) ? null : AppId(s.App!);
            var v4 = new List<(uint lo, uint hi)>();
            var v6 = new List<(byte[] addr, int prefix, byte[]? hi)>();
            bool hasAddr = !string.IsNullOrWhiteSpace(s.RemoteAddr);
            if (hasAddr) ParseAddrs(s.RemoteAddr!, v4, v6);
            var ports = ParsePorts(s.RemotePorts, s.Proto);

            var layers = new List<(Guid layer, bool v6)>();
            foreach (var d in new[] { s.Dir })
            {
                bool outb = d == FirewallService.DirOut;
                if (!hasAddr || v4.Count > 0) layers.Add((outb ? LayerConnectV4 : LayerRecvV4, false));
                if (!hasAddr || v6.Count > 0) layers.Add((outb ? LayerConnectV6 : LayerRecvV6, true));
            }
            if (layers.Count == 0) return; // nimic valid de blocat

            var desc = "FallenSec h=" + s.Hash();
            var added = new List<Guid>();
            Check(FwpmTransactionBegin0(_engine, 0), "FwpmTransactionBegin0");
            try
            {
                foreach (var (layer, isV6) in layers)
                {
                    using var mem = new Mem();
                    var conds = new List<Action<IntPtr>>();
                    if (appId != null)
                    {
                        var blob = mem.Alloc(16);
                        Marshal.WriteInt32(blob, 0, appId.Length);
                        Marshal.WriteIntPtr(blob, 8, mem.Bytes(appId));
                        conds.Add(c => WriteCond(c, CondAppId, MATCH_EQUAL, FWP_BYTE_BLOB, blob));
                    }
                    if (s.Proto != FirewallService.ProtoAny)
                        conds.Add(c => WriteCond(c, CondProtocol, MATCH_EQUAL, FWP_UINT8, (IntPtr)s.Proto));
                    foreach (var (lo, hi) in ports)
                    {
                        if (lo == hi) conds.Add(c => WriteCond(c, CondRemotePort, MATCH_EQUAL, FWP_UINT16, (IntPtr)lo));
                        else
                        {
                            var rg = Range(mem, FWP_UINT16, (IntPtr)lo, (IntPtr)hi);
                            conds.Add(c => WriteCond(c, CondRemotePort, MATCH_RANGE, FWP_RANGE, rg));
                        }
                    }
                    if (hasAddr && !isV6)
                        foreach (var (lo, hi) in v4)
                        {
                            if (lo == hi) conds.Add(c => WriteCond(c, CondRemoteAddr, MATCH_EQUAL, FWP_UINT32, (IntPtr)(long)lo));
                            else
                            {
                                var rg = Range(mem, FWP_UINT32, (IntPtr)(long)lo, (IntPtr)(long)hi);
                                conds.Add(c => WriteCond(c, CondRemoteAddr, MATCH_RANGE, FWP_RANGE, rg));
                            }
                        }
                    if (hasAddr && isV6)
                        foreach (var (addr, prefix, hi) in v6)
                        {
                            if (hi != null)
                            {
                                var rg = Range(mem, FWP_BYTE_ARRAY16, mem.Bytes(addr), mem.Bytes(hi));
                                conds.Add(c => WriteCond(c, CondRemoteAddr, MATCH_RANGE, FWP_RANGE, rg));
                            }
                            else if (prefix >= 128) { var p = mem.Bytes(addr); conds.Add(c => WriteCond(c, CondRemoteAddr, MATCH_EQUAL, FWP_BYTE_ARRAY16, p)); }
                            else
                            {
                                var am = mem.Alloc(17); Marshal.Copy(addr, 0, am, 16); Marshal.WriteByte(am, 16, (byte)prefix);
                                conds.Add(c => WriteCond(c, CondRemoteAddr, MATCH_EQUAL, FWP_V6_ADDR_MASK, am));
                            }
                        }

                    var condArr = mem.Alloc(40 * Math.Max(conds.Count, 1));
                    for (int i = 0; i < conds.Count; i++) conds[i](condArr + 40 * i);

                    // FWPM_FILTER0 (200 B)
                    var key = Guid.NewGuid();
                    var f = mem.Alloc(200);
                    WriteGuid(f, 0, key);
                    Marshal.WriteIntPtr(f, 16, mem.Str(s.Name));
                    Marshal.WriteIntPtr(f, 24, mem.Str(desc));
                    Marshal.WriteInt32(f, 32, unchecked((int)(FILTER_PERSISTENT | FILTER_CLEAR_ACTION_RIGHT)));
                    Marshal.WriteIntPtr(f, 40, mem.Guid(ProviderKey));
                    WriteGuid(f, 64, layer);
                    WriteGuid(f, 80, SubLayerKey);
                    // weight: FWP_EMPTY (0) => automat
                    Marshal.WriteInt32(f, 112, conds.Count);
                    Marshal.WriteIntPtr(f, 120, conds.Count > 0 ? condArr : IntPtr.Zero);
                    Marshal.WriteInt32(f, 128, unchecked((int)ACTION_BLOCK));
                    Check(FwpmFilterAdd0(_engine, f, IntPtr.Zero, out _), "FwpmFilterAdd0");
                    added.Add(key);
                }
                Check(FwpmTransactionCommit0(_engine), "FwpmTransactionCommit0");
            }
            catch { FwpmTransactionAbort0(_engine); throw; }
            if (!_byName.TryGetValue(s.Name, out var l)) _byName[s.Name] = l = new();
            l.AddRange(added);
            _descByName[s.Name] = desc;
        }
    }

    static void WriteCond(IntPtr c, Guid field, uint match, uint type, IntPtr value)
    {
        // FWPM_FILTER_CONDITION0 (40 B): fieldKey(16), matchType(4)+pad, FWP_CONDITION_VALUE0{type(4)+pad, union(8)}
        WriteGuid(c, 0, field);
        Marshal.WriteInt32(c, 16, (int)match);
        Marshal.WriteInt32(c, 24, (int)type);
        Marshal.WriteInt64(c, 32, (long)value);
    }

    static IntPtr Range(Mem mem, uint type, IntPtr lo, IntPtr hi)
    {
        // FWP_RANGE0 (32 B): FWP_VALUE0 low, FWP_VALUE0 high
        var r = mem.Alloc(32);
        Marshal.WriteInt32(r, 0, (int)type); Marshal.WriteInt64(r, 8, (long)lo);
        Marshal.WriteInt32(r, 16, (int)type); Marshal.WriteInt64(r, 24, (long)hi);
        return r;
    }

    // ---------------------------------------------------------------- parsare
    static List<(int lo, int hi)> ParsePorts(string? spec, int proto)
    {
        var res = new List<(int, int)>();
        if (string.IsNullOrWhiteSpace(spec) || proto == FirewallService.ProtoAny) return res;
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ab = part.Split('-');
            int a = int.Parse(ab[0]), b = ab.Length > 1 ? int.Parse(ab[1]) : a;
            if (a > b) (a, b) = (b, a);
            res.Add((Math.Clamp(a, 0, 65535), Math.Clamp(b, 0, 65535)));
        }
        return res;
    }

    static void ParseAddrs(string spec, List<(uint, uint)> v4, List<(byte[], int, byte[]?)> v6)
    {
        foreach (var raw in spec.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            try
            {
                if (part.Contains('/'))
                {
                    var sl = part.Split('/');
                    var ip = Native.Norm(IPAddress.Parse(sl[0])); int pre = int.Parse(sl[1]);
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        pre = Math.Clamp(pre, 0, 32);
                        uint a = ToU32(ip), mask = pre == 0 ? 0u : uint.MaxValue << (32 - pre);
                        v4.Add((a & mask, (a & mask) | ~mask));
                    }
                    else v6.Add((ip.GetAddressBytes(), Math.Clamp(pre, 0, 128), null));
                }
                else if (part.Contains('-'))
                {
                    var ab = part.Split('-');
                    var a = Native.Norm(IPAddress.Parse(ab[0])); var b = Native.Norm(IPAddress.Parse(ab[1]));
                    if (a.AddressFamily != b.AddressFamily) continue;
                    if (a.AddressFamily == AddressFamily.InterNetwork) v4.Add((ToU32(a), ToU32(b)));
                    else v6.Add((a.GetAddressBytes(), 128, b.GetAddressBytes()));
                }
                else
                {
                    var ip = Native.Norm(IPAddress.Parse(part));
                    if (ip.AddressFamily == AddressFamily.InterNetwork) { var u = ToU32(ip); v4.Add((u, u)); }
                    else v6.Add((ip.GetAddressBytes(), 128, null));
                }
            }
            catch { Log.W("WFP: adresă ignorată: " + part); }
        }
    }

    static uint ToU32(IPAddress ip) { var b = ip.GetAddressBytes(); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }

    /// <summary>ALE_APP_ID: calea NT a executabilului, cu litere mici, UTF-16 cu terminator.</summary>
    byte[] AppId(string path)
    {
        if (_appIdCache.TryGetValue(path, out var c)) return c;
        byte[] res;
        if (path.Equals("System", StringComparison.OrdinalIgnoreCase))
            res = Encoding.Unicode.GetBytes("System\0");
        else if (FwpmGetAppIdFromFileName0(path, out var blob) == 0)
        {
            try
            {
                int size = Marshal.ReadInt32(blob, 0);
                res = new byte[size];
                Marshal.Copy(Marshal.ReadIntPtr(blob, 8), res, 0, size);
            }
            finally { FwpmFreeMemory0(ref blob); }
        }
        else
        {
            // fișierul nu (mai) există: construim manual \device\harddiskvolumeN\...
            var dev = path;
            if (path.Length > 2 && path[1] == ':')
            {
                var sb = new StringBuilder(260);
                if (QueryDosDevice(path[..2], sb, sb.Capacity) > 0) dev = sb + path[2..];
            }
            res = Encoding.Unicode.GetBytes(dev.ToLowerInvariant() + "\0");
        }
        _appIdCache[path] = res;
        return res;
    }

    // ---------------------------------------------------------------- memorie nativă
    static void WriteGuid(IntPtr p, int off, Guid g) => Marshal.Copy(g.ToByteArray(), 0, p + off, 16);
    static Guid ReadGuid(IntPtr p, int off) { var b = new byte[16]; Marshal.Copy(p + off, b, 0, 16); return new Guid(b); }

    sealed class Mem : IDisposable
    {
        readonly List<IntPtr> _all = new();
        public IntPtr Alloc(int size)
        {
            var p = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
            _all.Add(p); return p;
        }
        public IntPtr Str(string s) { var p = Marshal.StringToHGlobalUni(s); _all.Add(p); return p; }
        public IntPtr Bytes(byte[] b) { var p = Alloc(b.Length); Marshal.Copy(b, 0, p, b.Length); return p; }
        public IntPtr Guid(Guid g) { var p = Alloc(16); WriteGuid(p, 0, g); return p; }
        public void Dispose() { foreach (var p in _all) Marshal.FreeHGlobal(p); _all.Clear(); }
    }

    // ---------------------------------------------------------------- test (rulat cu --wfptest, ca admin)
    /// <summary>Test complet: blochează curl.exe către 1.1.1.1 printr-un filtru, verifică, apoi șterge.</summary>
    public static string SelfTest()
    {
        var sb = new StringBuilder();
        void L(string s) { sb.AppendLine(s); }
        using var w = new WfpService();
        try
        {
            w.Open(); L("open: ok");
            var curl = Path.Combine(Environment.SystemDirectory, "curl.exe");
            L("appId: " + Encoding.Unicode.GetString(w.AppId(curl)).TrimEnd('\0'));
            int Curl(string url)
            {
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(curl, $"-s -o NUL -m 6 -w %{{http_code}} {url}") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
                p.WaitForExit(9000); return p.ExitCode;
            }
            L("curl înainte: exit=" + Curl("https://1.1.1.1"));
            var spec = new FwSpec("FallenSec [selftest] out", curl, FirewallService.DirOut, FirewallService.ProtoTcp, "1.1.1.0/24,1.0.0.1-1.0.0.2,2606:4700:4700::1111", "443,80");
            w.Add(spec); L("add: ok");
            var list = w.ListOurRules();
            L("listă: " + string.Join(" | ", list.Select(kv => kv.Key + " => " + kv.Value)));
            L("curl blocat (1.1.1.1): exit=" + Curl("https://1.1.1.1") + " (așteptat ≠ 0)");
            L("curl alt IP (8.8.8.8): exit=" + Curl("https://8.8.8.8") + " (așteptat 0)");
            var all = new FwSpec("FallenSec [selftest2] out", curl, FirewallService.DirOut, FirewallService.ProtoAny, null, null);
            w.Add(all); L("curl blocat total (8.8.8.8): exit=" + Curl("https://8.8.8.8") + " (așteptat ≠ 0)");
            w.Remove(spec.Name); w.Remove(all.Name); L("remove: ok");
            L("curl după: exit=" + Curl("https://1.1.1.1") + " (așteptat 0)");
            L("rămase: " + w.ListOurRules().Count);
        }
        catch (Exception e) { L("EROARE: " + e.Message); try { w.RemoveAll(); } catch { } }
        return sb.ToString();
    }
}
