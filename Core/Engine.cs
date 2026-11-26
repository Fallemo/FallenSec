using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace FallenSec.Core;

public sealed class Conn
{
    public string Id = "";
    public bool Udp;
    public int Pid;
    public AppInfo App = null!;
    public int LocalPort;
    public IPAddress Remote = IPAddress.Any;
    public int RemotePort;
    public int State;
    public long TotUp, TotDown;
    public double RateUp, RateDown;
    public DateTime First, LastActivity;
    public bool InTable;
    public SocketRow? Row;
    public GeoInfo Geo = GeoInfo.None;
}

public sealed class AppStat
{
    public AppInfo App = null!;
    public int Conns;
    public double RateUp, RateDown;
    public long TotUp, TotDown;
    public HashSet<string> Listening = new();
    public DateTime LastActive;
    public HashSet<string> Countries = new();
}

public sealed class Engine : IDisposable
{
    public readonly Settings Settings;
    public readonly GeoService Geo = new();
    public readonly FirewallService Fw = new();
    public readonly RuleEngine Rules;
    public readonly TrafficMonitor Traffic = new();
    public HistoryDb? Db;
    readonly ProcessCache _procs = new();

    readonly object _state = new();
    readonly Dictionary<string, Conn> _conns = new();
    readonly Dictionary<string, AppStat> _apps = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, string> _hosts = new(); // ip → domeniu
    readonly ConcurrentDictionary<string, byte> _ptrPending = new();
    int _ptrRunning;
    HashSet<string> _knownApps = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _seenThisSession = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string app, string ip, int port, bool udp), (long up, long down, string? host, string cc)> _bucket = new();
    long _bucketMinute;
    readonly LinkedList<(long t, double up, double down)> _rateHist = new();
    long _sessUp, _sessDown;
    double _rateUp, _rateDown;
    DateTime _lastTick = DateTime.UtcNow;
    System.Threading.Timer? _timer;
    int _ticking;
    public bool IsAdmin { get; }
    public WfpService? Wfp { get; private set; }
    public string? WfpError { get; private set; }
    public object BlockStatus { get; private set; } = new { level = "warn", engine = "", text = L.T("Se verifică...", "Checking...") };
    DateTime _lastBlockCheck = DateTime.MinValue;
    public (double lat, double lon, string? city)? Home { get; private set; }
    public HashSet<string> SentIcons { get; } = new();
    public volatile bool UiActive;
    public void ResetIcons() { lock (_state) SentIcons.Clear(); }
    DateTime _lastCleanup = DateTime.MinValue;

    public event Action<string>? Snapshot;          // JSON pentru UI
    public event Action<string, string>? Notify;    // titlu, text
    public event Action<double, double>? Rates;     // pentru tray

    public Engine()
    {
        Settings = Settings.LoadOrDefault();
        IsAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        Rules = new RuleEngine(Fw, Geo, Settings.Panic);
        Rules.Applied += OnRuleApplied;
        Traffic.DnsResolved += (name, ips) =>
        {
            foreach (var ip in ips) _hosts[ip.ToString()] = name;
            Rules.OnDns(name, ips);
        };
    }

    public void Start()
    {
        try { Db = new HistoryDb(); _knownApps = Db.KnownApps(); }
        catch (Exception e) { Log.W("DB: " + e.Message); }
        Traffic.Start();
        if (IsAdmin && Settings.BlockEngine != "winfw") TryWfp();
        RefreshBlockStatus();
        _ = Task.Run(async () =>
        {
            await Geo.InitAsync();
            await ResolveHomeAsync();
            if (IsAdmin)
            {
                if (Rules.Backend is WfpService) MigrateFrom(Fw);
                Rules.InitialSync();
            }
        });
        Geo.Changed += () => { if (Geo.Ready && Home == null) _ = ResolveHomeAsync(); };
        _timer = new System.Threading.Timer(_ => Tick(), null, 500, 1000);
    }

    public async Task ResolveHomeAsync()
    {
        if (!Settings.HomeAuto && Settings.HomeLat != null && Settings.HomeLon != null)
        { Home = (Settings.HomeLat.Value, Settings.HomeLon.Value, Settings.HomeCity); return; }
        if (!Geo.Ready) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var ipStr = (await http.GetStringAsync("https://api.ipify.org")).Trim();
            if (IPAddress.TryParse(ipStr, out var ip))
            {
                var g = Geo.Lookup(ip);
                if (!double.IsNaN(g.Lat)) Home = (g.Lat, g.Lon, g.City ?? g.Country);
            }
        }
        catch (Exception e) { Log.W("Home: " + e.Message); }
        Home ??= (Settings.HomeLat ?? 44.43, Settings.HomeLon ?? 26.10, Settings.HomeCity ?? "București");
    }

    static string Key(bool udp, int pid, int lport, IPAddress remote, int rport) =>
        $"{(udp ? 'U' : 'T')}|{pid}|{lport}|{remote}|{rport}";

    void Tick()
    {
        if ((DateTime.UtcNow - _lastBlockCheck).TotalSeconds >= 30) { _lastBlockCheck = DateTime.UtcNow; Task.Run(() => RefreshBlockStatus(true)); }
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try { TickCore(); }
        catch (Exception e) { Log.W("Tick: " + e); }
        finally { Interlocked.Exchange(ref _ticking, 0); }
    }

    void TickCore()
    {
        var now = DateTime.UtcNow;
        double elapsed = Math.Max(0.2, (now - _lastTick).TotalSeconds);
        _lastTick = now;
        var sockets = Native.GetSockets();
        var flows = Traffic.Drain();
        var blocked = Rules.BlockedAppKeys();
        bool showLan = Settings.ShowLan;
        double totUpRate = 0, totDownRate = 0;

        lock (_state)
        {
            foreach (var c in _conns.Values) { c.InTable = false; c.RateUp = c.RateDown = 0; }
            foreach (var a in _apps.Values) { a.Conns = 0; a.RateUp = a.RateDown = 0; a.Listening.Clear(); a.Countries.Clear(); }

            var tcpPidByEndpoint = new Dictionary<(int, string, int), int>();
            foreach (var r in sockets)
            {
                if (r.Pid == 0) continue;
                if (r.Udp || r.State == Native.TCP_STATE_LISTEN)
                {
                    var la = _procs.Get(r.Pid);
                    if (IPAddress.IsLoopback(r.LocalAddr)) continue;
                    GetStat(la).Listening.Add((r.Udp ? "UDP " : "TCP ") + r.LocalPort);
                    continue;
                }
                var remote = Native.Norm(r.RemoteAddr);
                if (IPAddress.IsLoopback(remote) || remote.Equals(IPAddress.Any) || remote.Equals(IPAddress.IPv6Any)) continue;
                tcpPidByEndpoint[(r.LocalPort, remote.ToString(), r.RemotePort)] = r.Pid;
                if (!showLan && Native.IsLocalOrPrivate(remote)) continue;
                var key = Key(false, r.Pid, r.LocalPort, remote, r.RemotePort);
                var c = GetConn(key, false, r.Pid, r.LocalPort, remote, r.RemotePort, now);
                c.State = r.State; c.Row = r; c.InTable = true;
            }

            long minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            if (_bucketMinute != 0 && minute != _bucketMinute) FlushBucket();
            _bucketMinute = minute;

            foreach (var (fk, fc) in flows)
            {
                var remote = fk.Remote;
                if (IPAddress.IsLoopback(remote)) continue;
                int pid = fk.Pid;
                if (!fk.Udp && (pid == 0 || pid == 4 || pid == -1) &&
                    tcpPidByEndpoint.TryGetValue((fk.LocalPort, remote.ToString(), fk.RemotePort), out var realPid)) pid = realPid;
                if (pid <= 0) pid = 4;
                bool lan = Native.IsLocalOrPrivate(remote);
                if (!showLan && lan) continue;
                var key = Key(fk.Udp, pid, fk.LocalPort, remote, fk.RemotePort);
                var c = GetConn(key, fk.Udp, pid, fk.LocalPort, remote, fk.RemotePort, now);
                c.TotUp += fc.Up; c.TotDown += fc.Down;
                c.RateUp = fc.Up / elapsed; c.RateDown = fc.Down / elapsed;
                c.LastActivity = now;
                _sessUp += fc.Up; _sessDown += fc.Down;
                totUpRate += c.RateUp; totDownRate += c.RateDown;
                var bk = (c.App.Key, remote.ToString(), fk.RemotePort, fk.Udp);
                _bucket.TryGetValue(bk, out var bv);
                _bucket[bk] = (bv.up + fc.Up, bv.down + fc.Down, HostOf(remote) ?? bv.host, c.Geo.Cc);
            }

            // curățare + agregare pe aplicații
            var dead = new List<string>();
            foreach (var c in _conns.Values)
            {
                double idle = (now - c.LastActivity).TotalSeconds;
                if (!c.InTable && idle > (c.Udp ? 30 : 4)) { dead.Add(c.Id); continue; }
                if (double.IsNaN(c.Geo.Lat) && c.Geo != GeoInfo.Lan && Geo.Ready && c.Geo.Cc == "") c.Geo = Geo.Lookup(c.Remote);
                var s = GetStat(c.App);
                s.Conns++; s.RateUp += c.RateUp; s.RateDown += c.RateDown;
                s.TotUp += (long)(c.RateUp * elapsed); s.TotDown += (long)(c.RateDown * elapsed);
                if (c.RateUp + c.RateDown > 0) s.LastActive = now;
                if (!string.IsNullOrEmpty(c.Geo.Cc) && c.Geo.Cc != "LAN") s.Countries.Add(c.Geo.Cc);
                if (HostOf(c.Remote) == null) QueuePtr(c.Remote);
                CheckNewApp(c);
            }
            foreach (var k in dead) _conns.Remove(k);

            _rateUp = totUpRate; _rateDown = totDownRate;
            _rateHist.AddLast((DateTimeOffset.UtcNow.ToUnixTimeSeconds(), totUpRate, totDownRate));
            while (_rateHist.Count > 180) _rateHist.RemoveFirst();

            // aplicațiile blocate apar mereu (ca să le poți debloca)
            foreach (var path in blocked)
                if (!_apps.ContainsKey(path))
                {
                    var app = _procs.ByKey(path) ?? (File.Exists(path) ? _procs.GetByPath(path) : new AppInfo { Key = path, Path = path, Name = Path.GetFileName(path) });
                    GetStat(app);
                }

            if (UiActive) Snapshot?.Invoke(BuildSnapshot(blocked));
        }
        Rates?.Invoke(totUpRate, totDownRate);

        if ((DateTime.UtcNow - _lastCleanup).TotalHours > 6)
        {
            _lastCleanup = DateTime.UtcNow;
            var days = Settings.HistoryDays;
            _ = Task.Run(() => { try { Db?.Maintain(days); } catch (Exception e) { Log.W("Istoric (întreținere): " + e.Message); } });
        }
    }

    Conn GetConn(string key, bool udp, int pid, int lport, IPAddress remote, int rport, DateTime now)
    {
        if (_conns.TryGetValue(key, out var c)) return c;
        c = new Conn
        {
            Id = key, Udp = udp, Pid = pid, App = _procs.Get(pid), LocalPort = lport, Remote = remote, RemotePort = rport,
            First = now, LastActivity = now, Geo = Geo.Lookup(remote)
        };
        _conns[key] = c;
        return c;
    }

    AppStat GetStat(AppInfo a)
    {
        if (!_apps.TryGetValue(a.Key, out var s)) { s = new AppStat { App = a }; _apps[a.Key] = s; }
        return s;
    }

    string? HostOf(IPAddress ip) => _hosts.TryGetValue(ip.ToString(), out var h) ? h : null;

    void QueuePtr(IPAddress ip)
    {
        if (Native.IsLocalOrPrivate(ip)) return;
        var s = ip.ToString();
        if (_hosts.ContainsKey(s) || !_ptrPending.TryAdd(s, 0)) return;
        if (Interlocked.Increment(ref _ptrRunning) > 4) { Interlocked.Decrement(ref _ptrRunning); _ptrPending.TryRemove(s, out _); return; }
        _ = Task.Run(async () =>
        {
            try
            {
                var e = await Dns.GetHostEntryAsync(ip).WaitAsync(TimeSpan.FromSeconds(4));
                if (!string.IsNullOrEmpty(e.HostName) && e.HostName != s) _hosts.TryAdd(s, e.HostName.ToLowerInvariant());
                else _hosts.TryAdd(s, "");
            }
            catch { _hosts.TryAdd(s, ""); }
            finally { Interlocked.Decrement(ref _ptrRunning); }
        });
    }

    void CheckNewApp(Conn c)
    {
        var a = c.App;
        if (a.Key is "idle" or "unknown" || _seenThisSession.Contains(a.Key)) return;
        if (c.Geo == GeoInfo.Lan && Native.IsLocalOrPrivate(c.Remote)) return; // doar ieșiri pe internet
        _seenThisSession.Add(a.Key);
        bool isNew = !_knownApps.Contains(a.Key);
        var host = HostOf(c.Remote);
        var dest = $"{(string.IsNullOrEmpty(host) ? c.Remote.ToString() : host)}:{c.RemotePort}" + (string.IsNullOrEmpty(c.Geo.Country) ? "" : $" ({c.Geo.Country})");
        _ = Task.Run(() =>
        {
            try
            {
                Db?.UpsertApp(a, isNew);
                if (isNew) Db?.AddEvent("new_app", a.Key, L.T("Prima conexiune la internet → ", "First internet connection → ") + dest);
            }
            catch { }
        });
        if (isNew)
        {
            _knownApps.Add(a.Key);
            if (Settings.NotifyNewApps) Notify?.Invoke(L.T("Aplicație nouă pe internet", "New app on the internet"), $"{a.Name} → {dest}");
        }
    }

    void FlushBucket()
    {
        if (_bucket.Count == 0 || Db == null) { _bucket.Clear(); return; }
        var m = _bucketMinute;
        var rows = _bucket.Select(kv => new HistoryDb.TrafficRow(m, kv.Key.app, kv.Key.ip, string.IsNullOrEmpty(kv.Value.host) ? null : kv.Value.host,
            kv.Value.cc ?? "", kv.Key.port, kv.Key.udp ? "UDP" : "TCP", kv.Value.up, kv.Value.down)).ToList();
        _bucket.Clear();
        _ = Task.Run(() => { try { Db.WriteTraffic(rows); } catch (Exception e) { Log.W("History: " + e.Message); } });
    }

    string BuildSnapshot(HashSet<string> blocked)
    {
        var custom = Rules.AppsWithCustomRules();
        var folders = Rules.BlockedFolders();
        bool InFolder(string k) { foreach (var f in folders) if (k.StartsWith(f, StringComparison.Ordinal)) return true; return false; }
        var apps = _apps.Values.Select(s => new
        {
            key = s.App.Key, name = s.App.Name, path = s.App.Path, publisher = s.App.Publisher, sys = s.App.IsSystem,
            icon = SentIcons.Add(s.App.Key) ? s.App.Icon : null,
            conns = s.Conns, up = s.RateUp, down = s.RateDown, totUp = s.TotUp, totDown = s.TotDown,
            listen = s.Listening.OrderBy(x => x).Take(20).ToArray(), countries = s.Countries.ToArray(),
            blocked = blocked.Contains(s.App.Key) || InFolder(s.App.Key), viaFolder = !blocked.Contains(s.App.Key) && InFolder(s.App.Key), rules = custom.Contains(s.App.Key),
            canBlock = s.App.Key != "idle" && s.App.Key != "unknown" && !s.App.Key.StartsWith("name:"),
            active = (DateTime.UtcNow - s.LastActive).TotalSeconds < 5
        }).ToList();

        var conns = _conns.Values
            .OrderByDescending(c => c.RateUp + c.RateDown).ThenByDescending(c => c.TotUp + c.TotDown).ThenByDescending(c => c.First)
            .Take(800)
            .Select(c => new
            {
                id = c.Id, app = c.App.Key, pid = c.Pid, proto = c.Udp ? "UDP" : "TCP", lport = c.LocalPort,
                ip = c.Remote.ToString(), rport = c.RemotePort, host = NullIfEmpty(HostOf(c.Remote)),
                cc = c.Geo.Cc, country = c.Geo.Country, city = c.Geo.City,
                lat = double.IsNaN(c.Geo.Lat) ? (double?)null : Math.Round(c.Geo.Lat, 3),
                lon = double.IsNaN(c.Geo.Lon) ? (double?)null : Math.Round(c.Geo.Lon, 3),
                state = c.Udp ? "UDP" : Native.TcpStateName(c.State), up = c.RateUp, down = c.RateDown,
                totUp = c.TotUp, totDown = c.TotDown, age = (long)(DateTime.UtcNow - c.First).TotalSeconds,
                blocked = blocked.Contains(c.App.Key) || InFolder(c.App.Key)
            }).ToList();

        var payload = new
        {
            type = "state",
            up = _rateUp, down = _rateDown, sessUp = _sessUp, sessDown = _sessDown,
            hist = _rateHist.Select(h => new[] { h.t, Math.Round(h.up), Math.Round(h.down) }),
            status = new
            {
                admin = IsAdmin, etw = Traffic.KernelOk, dns = Traffic.DnsOk, etwError = Traffic.Error,
                geo = Geo.Status, geoReady = Geo.Ready, block = BlockStatus, rules = Rules.LastError, panic = Rules.PanicOn
            },
            home = Home == null ? null : new { lat = Home.Value.lat, lon = Home.Value.lon, city = Home.Value.city },
            apps, conns
        };
        return JsonSerializer.Serialize(payload, Json.Opt);
    }

    static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    void OnRuleApplied(Rule r)
    {
        lock (_state)
        {
            foreach (var c in _conns.Values)
            {
                if (c.Row == null || c.Udp) continue;
                if (r.AppPath != null)
                {
                    if (r.Kind == "folder") { if (!c.App.Key.StartsWith(r.AppPath.TrimEnd((char)92).ToLowerInvariant() + (char)92, StringComparison.Ordinal)) continue; }
                    else if (!string.Equals(c.App.Key, r.AppPath, StringComparison.OrdinalIgnoreCase)) continue;
                }
                if (r.Direction == "in") continue;
                if (r.Ports != null && !PortIn(r.Ports, c.RemotePort)) continue;
                if (!Rules.RuleMatchesRemote(r, c.Remote, c.Geo, HostOf(c.Remote))) continue;
                Native.KillTcp(c.Row);
            }
        }
        if (r.Kind == "app") try { Db?.AddEvent("block", r.AppPath?.ToLowerInvariant(), L.T("Aplicație blocată", "App blocked")); } catch { }
        if (r.Kind == "folder") try { Db?.AddEvent("block", null, L.T("Folder blocat: ", "Folder blocked: ") + r.AppPath); } catch { }
    }

    static bool PortIn(string spec, int port)
    {
        foreach (var p in spec.Split(','))
        {
            var ab = p.Split('-');
            if (ab.Length == 1 && int.TryParse(ab[0], out var x) && x == port) return true;
            if (ab.Length == 2 && int.TryParse(ab[0], out var a) && int.TryParse(ab[1], out var b) && port >= a && port <= b) return true;
        }
        return false;
    }

    // ---------- comenzi din UI ----------
    public void SetAppBlocked(string key, bool block)
    {
        AppInfo? app;
        lock (_state) app = _apps.TryGetValue(key, out var s) ? s.App : _procs.ByKey(key);
        var path = app?.Path ?? key;
        Rules.SetAppBlocked(path, app?.Name ?? Path.GetFileName(path), block);
        if (!block) try { Db?.AddEvent("unblock", key, L.T("Aplicație deblocată", "App unblocked")); } catch { }
    }

    public bool KillConn(string id)
    {
        lock (_state) return _conns.TryGetValue(id, out var c) && c.Row != null && Native.KillTcp(c.Row);
    }

    // ---------------------------------------------------------------- motor de blocare
    bool TryWfp()
    {
        try
        {
            var w = new WfpService(); w.Open();
            Wfp = w; WfpError = null; Rules.Backend = w;
            Log.W("Motor blocare: WFP");
            return true;
        }
        catch (Exception e)
        {
            WfpError = e.Message; Wfp = null; Rules.Backend = Fw;
            Log.W("WFP indisponibil, folosesc Windows Firewall: " + e.Message);
            return false;
        }
    }

    /// <summary>Șterge regulile FallenSec din celălalt motor (ex. regulile Windows Firewall din v1.0).</summary>
    void MigrateFrom(IBlockBackend old)
    {
        try
        {
            int n = old switch { FirewallService f => f.RemoveAll(), WfpService w => w.RemoveAll(), _ => 0 };
            if (n > 0) Log.W($"Migrare: {n} reguli FallenSec șterse din {old.Name}");
        }
        catch (Exception e) { Log.W("Migrare " + old.Name + ": " + e.Message); }
    }

    /// <summary>Schimbă motorul de blocare (\"wfp\" sau \"winfw\") și re-aplică toate regulile.</summary>
    public string SwitchBlockEngine(string kind)
    {
        Settings.BlockEngine = kind == "winfw" ? "winfw" : "wfp"; Settings.Save();
        if (!IsAdmin) return L.T("Necesită administrator.", "Administrator rights required.");
        var old = Rules.Backend;
        if (Settings.BlockEngine == "wfp")
        {
            if (old is WfpService) return L.T("Folosești deja WFP.", "WFP is already in use.");
            if (!TryWfp()) { RefreshBlockStatus(true); return L.T("WFP nu poate fi pornit: ", "WFP cannot be started: ") + WfpError; }
        }
        else
        {
            if (old is FirewallService) return L.T("Folosești deja Windows Firewall.", "Windows Firewall is already in use.");
            Rules.Backend = Fw;
        }
        MigrateFrom(old);
        if (old is WfpService ow) { ow.Dispose(); if (ReferenceEquals(Wfp, ow)) Wfp = null; }
        Rules.RequestSync(null);
        RefreshBlockStatus(true);
        return L.T("Motorul de blocare este acum ", "The blocking engine is now ") + Rules.Backend.Name + L.T(". Regulile au fost re-aplicate.", ". Rules have been re-applied.");
    }

    public void RefreshBlockStatus(bool force = false)
    {
        if (!force && (DateTime.UtcNow - _lastBlockCheck).TotalSeconds < 30) return;
        _lastBlockCheck = DateTime.UtcNow;
        var tp = FirewallService.ThirdPartyActive();
        var off = Fw.OffProfiles();
        bool wfp = Rules.Backend is WfpService;
        string engine = wfp ? "WFP" : "Windows Firewall";
        if (!IsAdmin)
        {
            BlockStatus = new { level = "warn", engine, thirdParty = tp, text = L.T("Fără drepturi de administrator – blocarea nu este disponibilă.", "No administrator rights – blocking is not available."), banner = (string?)null, canEnableWinFw = false };
            return;
        }
        if (wfp)
        {
            var t = L.T("Blocarea folosește Windows Filtering Platform: funcționează indiferent dacă Windows Firewall e pornit sau nu.",
                        "Blocking uses the Windows Filtering Platform: it works whether Windows Firewall is on or off.");
            if (tp != null) t += L.T($" Firewall-ul {tp} este activ – sunt compatibile, ambele filtrează.", $" The {tp} firewall is active – they are compatible and both filter traffic.");
            BlockStatus = new { level = "ok", engine, thirdParty = tp, text = t, banner = (string?)null, canEnableWinFw = false };
            return;
        }
        string extra = WfpError != null && Settings.BlockEngine != "winfw" ? L.T(" (WFP indisponibil: ", " (WFP unavailable: ") + WfpError + ")" : "";
        if (off == null)
            BlockStatus = new { level = "bad", engine, thirdParty = tp, text = L.T("Nu pot accesa Windows Firewall.", "Cannot access Windows Firewall.") + extra,
                banner = (string?)L.T("Nu pot accesa Windows Firewall – blocările s-ar putea să nu aibă efect.", "Cannot access Windows Firewall – blocks may have no effect.") + extra, canEnableWinFw = false };
        else if (off.Count > 0 && tp != null)
            BlockStatus = new { level = "bad", engine, thirdParty = tp, text = L.T($"Firewall-ul {tp} a preluat rolul lui Windows Firewall.", $"The {tp} firewall has taken over from Windows Firewall.") + extra,
                banner = (string?)L.T($"Firewall-ul {tp} este activ și Windows Firewall e oprit, așa că blocările FallenSec prin Windows Firewall nu au efect. Treci motorul de blocare pe WFP (Setări) – funcționează împreună cu {tp}.",
                                      $"The {tp} firewall is active and Windows Firewall is off, so FallenSec blocks through Windows Firewall have no effect. Switch the blocking engine to WFP (Settings) – it works alongside {tp}.") + extra, canEnableWinFw = false };
        else if (off.Count > 0)
            BlockStatus = new { level = "bad", engine, thirdParty = tp, text = L.T("Windows Firewall este oprit pe: ", "Windows Firewall is off for: ") + string.Join(", ", off) + extra,
                banner = (string?)L.T("Windows Firewall este oprit pe profilul: ", "Windows Firewall is off for profile: ") + string.Join(", ", off) + L.T(". Blocările nu au efect acolo.", ". Blocks have no effect there.") + extra, canEnableWinFw = true };
        else
            BlockStatus = new { level = "ok", engine, thirdParty = tp, text = L.T("Blocarea folosește Windows Firewall (activ pe toate profilurile).", "Blocking uses Windows Firewall (on for all profiles).") + extra, banner = (string?)null, canEnableWinFw = false };
    }

    public void SetPanic(bool on)
    {
        Settings.Panic = on; Settings.Save();
        Rules.SetPanic(on);
        try { Db?.AddEvent(on ? "panic_on" : "panic_off", null, on ? L.T("Internet tăiat (buton panică)", "Internet cut off (panic button)") : L.T("Internet restabilit", "Internet restored")); } catch { }
    }

    public object AppList()
    {
        lock (_state)
            return _apps.Values.Where(a => a.App.Key is not ("idle" or "unknown") && !a.App.Key.StartsWith("name:"))
                .OrderBy(a => a.App.Name).Select(a => new { key = a.App.Key, name = a.App.Name, path = a.App.Path }).ToList();
    }

    public void ApplyStartup()
    {
        try
        {
            var exe = Environment.ProcessPath!;
            var psi = Settings.StartWithWindows
                ? new ProcessStartInfo("schtasks.exe", $"/Create /TN \"FallenSec\" /TR \"\\\"{exe}\\\" --tray\" /SC ONLOGON /RL HIGHEST /F")
                : new ProcessStartInfo("schtasks.exe", "/Delete /TN \"FallenSec\" /F");
            psi.CreateNoWindow = true; psi.UseShellExecute = false;
            Process.Start(psi)?.WaitForExit(5000);
        }
        catch (Exception e) { Log.W("Startup task: " + e.Message); }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        Wfp?.Dispose();
        lock (_state) FlushBucket();
        Thread.Sleep(200);
        Traffic.Dispose();
        Geo.Dispose();
        Db?.Dispose();
    }
}
