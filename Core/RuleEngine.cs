using System.Net;
using System.Text.RegularExpressions;

namespace FallenSec.Core;

public sealed class Rule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public bool Enabled { get; set; } = true;
    /// <summary>"app" = blocare totală aplicație (slider), "custom" = regulă detaliată</summary>
    public string Kind { get; set; } = "custom";
    public string? AppPath { get; set; }           // null = toate aplicațiile
    public string? AppName { get; set; }
    public string Direction { get; set; } = "out"; // out | in | both
    public string Protocol { get; set; } = "any";  // any | tcp | udp
    public string Target { get; set; } = "any";    // any | ip | domain | country
    public string? Value { get; set; }             // IP/CIDR/interval, domeniu, cod țară
    public string? Ports { get; set; }             // ex: 80,443,1000-2000
    public string? Note { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    public List<string> ResolvedIps { get; set; } = new(); // pentru domenii
    public string? Error { get; set; }
    public int FwCount { get; set; }
}

/// <summary>Păstrează regulile și le sincronizează cu Windows Firewall.</summary>
public sealed class RuleEngine
{
    IBlockBackend _fw;
    public IBlockBackend Backend { get => _fw; set => _fw = value; }
    readonly GeoService _geo;
    readonly object _lock = new();
    List<Rule> _rules;
    readonly SemaphoreSlim _syncGate = new(1, 1);
    int _syncPending;
    public bool PanicOn { get; private set; }
    public string? LastError { get; private set; }

    public event Action? Changed;
    /// <summary>Se apelează după ce o regulă a fost aplicată (pentru a tăia conexiunile existente).</summary>
    public event Action<Rule>? Applied;

    public RuleEngine(IBlockBackend fw, GeoService geo, bool panic)
    {
        _fw = fw; _geo = geo; PanicOn = panic;
        _rules = Json.Load<List<Rule>>(Paths.Rules) ?? new();
    }

    public List<Rule> Snapshot() { lock (_lock) return _rules.Select(Clone).ToList(); }
    static Rule Clone(Rule r) => System.Text.Json.JsonSerializer.Deserialize<Rule>(System.Text.Json.JsonSerializer.Serialize(r, Json.Opt), Json.Opt)!;

    public HashSet<string> BlockedAppKeys()
    {
        lock (_lock)
            return _rules.Where(r => r.Enabled && r.Kind == "app" && r.AppPath != null)
                         .Select(r => r.AppPath!.ToLowerInvariant()).ToHashSet();
    }

    /// <summary>Aplicații care au cel puțin o regulă custom activă.</summary>
    public HashSet<string> AppsWithCustomRules()
    {
        lock (_lock)
            return _rules.Where(r => r.Enabled && r.Kind == "custom" && r.AppPath != null)
                         .Select(r => r.AppPath!.ToLowerInvariant()).ToHashSet();
    }

    void Save() { lock (_lock) Json.Save(Paths.Rules, _rules); }

    public void SetAppBlocked(string path, string name, bool block)
    {
        Rule? applied = null;
        lock (_lock)
        {
            var existing = _rules.FirstOrDefault(r => r.Kind == "app" && string.Equals(r.AppPath, path, StringComparison.OrdinalIgnoreCase));
            if (block)
            {
                if (existing == null) { existing = new Rule { Kind = "app", AppPath = path, AppName = name, Direction = "both" }; _rules.Add(existing); }
                existing.Enabled = true;
                applied = existing;
            }
            else if (existing != null) _rules.Remove(existing);
        }
        Save(); Changed?.Invoke();
        RequestSync(applied);
    }

    public void Upsert(Rule r)
    {
        Validate(r);
        lock (_lock)
        {
            var i = _rules.FindIndex(x => x.Id == r.Id);
            if (i >= 0) { r.ResolvedIps = r.Target == "domain" && _rules[i].Value == r.Value ? _rules[i].ResolvedIps : new(); _rules[i] = r; }
            else _rules.Add(r);
        }
        Save(); Changed?.Invoke();
        if (r.Target == "domain" && r.Value != null) _ = ResolveDomainAsync(r.Id, r.Value);
        RequestSync(r.Enabled ? r : null);
    }

    public void SetEnabled(string id, bool en)
    {
        Rule? r;
        lock (_lock) { r = _rules.FirstOrDefault(x => x.Id == id); if (r != null) r.Enabled = en; }
        Save(); Changed?.Invoke(); RequestSync(en ? r : null);
    }

    public void Delete(string id)
    {
        lock (_lock) _rules.RemoveAll(x => x.Id == id);
        Save(); Changed?.Invoke(); RequestSync(null);
    }

    public void SetPanic(bool on) { PanicOn = on; Changed?.Invoke(); RequestSync(null); }

    static readonly Regex PortsRx = new(@"^\s*\d{1,5}(\s*-\s*\d{1,5})?(\s*,\s*\d{1,5}(\s*-\s*\d{1,5})?)*\s*$");
    static readonly Regex DomainRx = new(@"^(\*\.)?([a-z0-9-]+\.)+[a-z0-9-]{2,}$");

    static void Validate(Rule r)
    {
        r.Value = r.Value?.Trim();
        r.Ports = string.IsNullOrWhiteSpace(r.Ports) ? null : r.Ports.Replace(" ", "");
        if (r.Ports != null && !PortsRx.IsMatch(r.Ports)) throw new ArgumentException("Porturi invalide (ex: 80,443,8000-8100)");
        if (r.Ports != null && r.Protocol == "any") { /* vom crea reguli TCP + UDP */ }
        switch (r.Target)
        {
            case "domain":
                r.Value = r.Value?.ToLowerInvariant().TrimEnd('.');
                if (r.Value == null || !DomainRx.IsMatch(r.Value)) throw new ArgumentException("Domeniu invalid (ex: facebook.com)");
                if (r.Value.StartsWith("*.")) r.Value = r.Value[2..];
                break;
            case "country":
                r.Value = r.Value?.ToUpperInvariant();
                if (r.Value == null || r.Value.Length != 2) throw new ArgumentException("Alege o țară");
                break;
            case "ip":
                if (string.IsNullOrWhiteSpace(r.Value)) throw new ArgumentException("Introdu un IP, CIDR sau interval");
                r.Value = string.Join(",", r.Value.Split(new[] { ',', ';', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                break;
        }
        if (r.Kind == "custom" && r.AppPath == null && r.Target == "any" && r.Ports == null)
            throw new ArgumentException("O regulă pentru toate aplicațiile trebuie să aibă o destinație sau porturi (altfel taie tot internetul – folosește butonul de panică).");
    }

    // ---------- domenii ----------
    public static bool DomainMatches(string query, string domain) =>
        query == domain || query.EndsWith("." + domain, StringComparison.Ordinal);

    public void OnDns(string name, List<IPAddress> ips)
    {
        bool changed = false; Rule? hit = null;
        lock (_lock)
        {
            foreach (var r in _rules)
            {
                if (r.Target != "domain" || r.Value == null || !DomainMatches(name, r.Value)) continue;
                foreach (var ip in ips)
                {
                    var s = ip.ToString();
                    if (!r.ResolvedIps.Contains(s)) { r.ResolvedIps.Add(s); changed = true; hit = r; }
                }
                if (r.ResolvedIps.Count > 2000) r.ResolvedIps.RemoveRange(0, r.ResolvedIps.Count - 2000);
            }
        }
        if (changed) { Save(); RequestSync(hit); }
    }

    async Task ResolveDomainAsync(string id, string domain)
    {
        var all = new List<IPAddress>();
        foreach (var h in new[] { domain, "www." + domain })
            try { all.AddRange(await Dns.GetHostAddressesAsync(h)); } catch { }
        if (all.Count > 0) OnDns(domain, all.Select(Native.Norm).ToList());
    }

    public bool RuleMatchesRemote(Rule r, IPAddress ip, GeoInfo geo, string? host)
    {
        return r.Target switch
        {
            "any" => true,
            "country" => string.Equals(geo.Cc, r.Value, StringComparison.OrdinalIgnoreCase),
            "domain" => (host != null && r.Value != null && DomainMatches(host, r.Value)) || r.ResolvedIps.Contains(ip.ToString()),
            "ip" => IpMatches(r.Value ?? "", ip),
            _ => false
        };
    }

    static bool IpMatches(string spec, IPAddress ip)
    {
        foreach (var part in spec.Split(','))
        {
            try
            {
                if (part.Contains('/'))
                {
                    var n = IPNetwork.Parse(part);
                    if (n.Contains(ip)) return true;
                }
                else if (part.Contains('-'))
                {
                    var ab = part.Split('-');
                    var a = IPAddress.Parse(ab[0]).GetAddressBytes(); var b = IPAddress.Parse(ab[1]).GetAddressBytes(); var x = ip.GetAddressBytes();
                    if (a.Length == x.Length && Cmp(x, a) >= 0 && Cmp(x, b) <= 0) return true;
                }
                else if (IPAddress.Parse(part).Equals(ip)) return true;
            }
            catch { }
        }
        return false;
        static int Cmp(byte[] p, byte[] q) { for (int i = 0; i < p.Length; i++) if (p[i] != q[i]) return p[i].CompareTo(q[i]); return 0; }
    }

    // ---------- sincronizare ----------
    public void RequestSync(Rule? applied)
    {
        Interlocked.Exchange(ref _syncPending, 1);
        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            await _syncGate.WaitAsync();
            try
            {
                if (Interlocked.Exchange(ref _syncPending, 0) == 1) SyncNow();
                if (applied != null) Applied?.Invoke(applied);
            }
            finally { _syncGate.Release(); }
        });
    }

    void SyncNow()
    {
        try
        {
            var desired = new Dictionary<string, FwSpec>();
            List<Rule> rules;
            lock (_lock) rules = _rules.ToList();
            foreach (var r in rules)
            {
                r.Error = null;
                if (!r.Enabled) { r.FwCount = 0; continue; }
                try
                {
                    var specs = BuildSpecs(r).ToList();
                    r.FwCount = specs.Count;
                    foreach (var s in specs) desired[s.Name] = s;
                }
                catch (Exception e) { r.Error = e.Message; }
            }
            if (PanicOn)
                foreach (var s in new[] { new FwSpec("FallenSec [PANIC] out", null, FirewallService.DirOut, FirewallService.ProtoAny, FirewallService.InternetRanges, null),
                                          new FwSpec("FallenSec [PANIC] in", null, FirewallService.DirIn, FirewallService.ProtoAny, FirewallService.InternetRanges, null) })
                    desired[s.Name] = s;

            var existing = _fw.ListOurRules();
            foreach (var (name, desc) in existing)
                if (!desired.TryGetValue(name, out var s) || desc != "FallenSec h=" + s.Hash())
                    _fw.Remove(name);
            existing = _fw.ListOurRules();
            foreach (var (name, s) in desired)
            {
                if (existing.ContainsKey(name)) continue;
                try { _fw.Add(s); }
                catch (Exception e)
                {
                    var id = Regex.Match(name, @"\[(\w+)\]").Groups[1].Value;
                    var r = rules.FirstOrDefault(x => x.Id == id);
                    if (r != null) r.Error = _fw.Name + ": " + (e.InnerException?.Message ?? e.Message);
                    Log.W($"FW add {name}: {e.Message}");
                }
            }
            LastError = null;
        }
        catch (Exception e)
        {
            LastError = "Sincronizare reguli eșuată (" + _fw.Name + "): " + e.Message;
            Log.W(LastError);
        }
        Changed?.Invoke();
    }

    IEnumerable<FwSpec> BuildSpecs(Rule r)
    {
        var dirs = r.Direction switch
        {
            "in" => new[] { FirewallService.DirIn },
            "both" => new[] { FirewallService.DirOut, FirewallService.DirIn },
            _ => new[] { FirewallService.DirOut }
        };
        int[] protos = r.Protocol switch
        {
            "tcp" => new[] { FirewallService.ProtoTcp },
            "udp" => new[] { FirewallService.ProtoUdp },
            _ => r.Ports != null ? new[] { FirewallService.ProtoTcp, FirewallService.ProtoUdp } : new[] { FirewallService.ProtoAny }
        };
        List<string?> addrChunks;
        switch (r.Target)
        {
            case "ip": addrChunks = new() { r.Value }; break;
            case "domain":
                if (r.ResolvedIps.Count == 0) yield break; // încă nu știm IP-urile – niciodată regulă fără adrese!
                addrChunks = Chunk(r.ResolvedIps, 500);
                break;
            case "country":
                var ranges = _geo.CountryRanges(r.Value!);
                if (ranges.Count == 0) throw new Exception("Nu am intervale IP pentru țara " + r.Value + " (baza geo lipsește?)");
                addrChunks = Chunk(ranges, 1000);
                break;
            default: addrChunks = new() { null }; break;
        }
        foreach (var d in dirs)
            foreach (var p in protos)
                for (int i = 0; i < addrChunks.Count; i++)
                {
                    var name = $"FallenSec [{r.Id}] {(d == FirewallService.DirOut ? "out" : "in")}{(p == 6 ? " tcp" : p == 17 ? " udp" : "")}{(addrChunks.Count > 1 ? " #" + i : "")} - {r.AppName ?? "Toate aplicațiile"}";
                    yield return new FwSpec(name, r.AppPath, d, p, addrChunks[i], r.Ports);
                }
    }

    static List<string?> Chunk(List<string> items, int size)
    {
        var res = new List<string?>();
        for (int i = 0; i < items.Count; i += size) res.Add(string.Join(",", items.Skip(i).Take(size)));
        return res;
    }

    /// <summary>La pornire: aplică regulile salvate (repară eventuale diferențe).</summary>
    public void InitialSync()
    {
        foreach (var r in Snapshot().Where(x => x.Target == "domain" && x.Value != null && x.Enabled))
            _ = ResolveDomainAsync(r.Id, r.Value!);
        RequestSync(null);
    }
}
