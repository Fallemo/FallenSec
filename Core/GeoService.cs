using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using MaxMind.Db;

namespace FallenSec.Core;

public sealed record GeoInfo(string Cc, string Country, string? City, double Lat, double Lon)
{
    public static readonly GeoInfo Lan = new("LAN", "Rețea locală", null, double.NaN, double.NaN);
    public static readonly GeoInfo None = new("", "", null, double.NaN, double.NaN);
}

/// <summary>Geolocalizare IP offline cu bazele gratuite DB-IP Lite (CC BY 4.0).</summary>
public sealed class GeoService : IDisposable
{
    Reader? _city;
    readonly ConcurrentDictionary<IPAddress, GeoInfo> _cache = new();
    readonly ConcurrentDictionary<string, List<string>> _rangeCache = new();
    public string Status { get; private set; } = "Inițializare...";
    public bool Ready => _city != null;
    public event Action? Changed;

    string CityFile => Path.Combine(Paths.Geo, "dbip-city-lite.mmdb");
    string CountryFile => Path.Combine(Paths.Geo, "dbip-country-lite.mmdb");

    public async Task InitAsync(bool forceUpdate = false)
    {
        try
        {
            bool stale = !File.Exists(CityFile) || (DateTime.Now - File.GetLastWriteTime(CityFile)).TotalDays > 40;
            if (File.Exists(CityFile) && !forceUpdate) Open();
            if (stale || forceUpdate)
            {
                await DownloadAsync("city", CityFile);
                await DownloadAsync("country", CountryFile);
                Open();
            }
            else if (!File.Exists(CountryFile)) await DownloadAsync("country", CountryFile);
            Status = Ready ? "Bază geo DB-IP încărcată (" + File.GetLastWriteTime(CityFile).ToString("MMM yyyy") + ")" : Status;
        }
        catch (Exception e)
        {
            Status = "Geo indisponibil: " + e.Message;
            Log.W(Status);
        }
        Changed?.Invoke();
    }

    void Open()
    {
        var old = _city;
        _city = new Reader(CityFile, FileAccessMode.Memory);
        old?.Dispose();
        _cache.Clear(); _rangeCache.Clear();
    }

    async Task DownloadAsync(string kind, string target)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FallenSec/" + AppInfoConst.Version);
        var now = DateTime.UtcNow;
        foreach (var d in new[] { now, now.AddMonths(-1), now.AddMonths(-2) })
        {
            var url = $"https://download.db-ip.com/free/dbip-{kind}-lite-{d:yyyy-MM}.mmdb.gz";
            try
            {
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) continue;
                long total = resp.Content.Headers.ContentLength ?? 0;
                var tmp = target + ".download";
                await using (var src = await resp.Content.ReadAsStreamAsync())
                await using (var progress = new ProgressStream(src, total, p =>
                {
                    Status = $"Descarc baza geo ({kind}) {p:0}%";
                    Changed?.Invoke();
                }))
                await using (var gz = new GZipStream(progress, CompressionMode.Decompress))
                await using (var fs = File.Create(tmp))
                    await gz.CopyToAsync(fs);
                if (kind == "city") { _city?.Dispose(); _city = null; }
                File.Move(tmp, target, true);
                return;
            }
            catch (Exception e) { Log.W($"Geo download {url}: {e.Message}"); }
        }
        throw new Exception("nu am putut descărca baza DB-IP");
    }

    public GeoInfo Lookup(IPAddress ip)
    {
        ip = Native.Norm(ip);
        if (Native.IsLocalOrPrivate(ip)) return GeoInfo.Lan;
        if (_cache.TryGetValue(ip, out var g)) return g;
        var r = _city;
        if (r == null) return GeoInfo.None;
        try
        {
            var d = r.Find<Dictionary<string, object>>(ip);
            g = d == null ? GeoInfo.None : Parse(d);
        }
        catch { g = GeoInfo.None; }
        if (_cache.Count > 50000) _cache.Clear();
        _cache[ip] = g;
        return g;
    }

    static GeoInfo Parse(Dictionary<string, object> d)
    {
        string cc = "", country = ""; string? city = null; double lat = double.NaN, lon = double.NaN;
        if (d.TryGetValue("country", out var c) && c is Dictionary<string, object> cd)
        {
            cc = cd.TryGetValue("iso_code", out var ic) ? ic?.ToString() ?? "" : "";
            country = Name(cd) ?? cc;
        }
        if (d.TryGetValue("city", out var ci) && ci is Dictionary<string, object> cid) city = Name(cid);
        if (d.TryGetValue("location", out var l) && l is Dictionary<string, object> ld)
        {
            if (ld.TryGetValue("latitude", out var la)) lat = Convert.ToDouble(la);
            if (ld.TryGetValue("longitude", out var lo)) lon = Convert.ToDouble(lo);
        }
        return new GeoInfo(cc, country, city, lat, lon);
    }

    static string? Name(Dictionary<string, object> d) =>
        d.TryGetValue("names", out var n) && n is Dictionary<string, object> nd && nd.TryGetValue("en", out var en) ? en?.ToString() : null;

    /// <summary>Toate intervalele IP (CIDR) ale unei țări, din baza country-lite.</summary>
    public List<string> CountryRanges(string cc)
    {
        cc = cc.ToUpperInvariant();
        if (_rangeCache.TryGetValue(cc, out var cached)) return cached;
        var list = new List<string>();
        if (!File.Exists(CountryFile)) return list;
        using var r = new Reader(CountryFile, FileAccessMode.Memory);
        var seen = new HashSet<string>();
        foreach (var node in r.FindAll<Dictionary<string, object>>())
        {
            if (node.Data == null || !node.Data.TryGetValue("country", out var c) || c is not Dictionary<string, object> cd) continue;
            if (!(cd.TryGetValue("iso_code", out var ic) && string.Equals(ic?.ToString(), cc, StringComparison.OrdinalIgnoreCase))) continue;
            var start = node.Start; int prefix = node.PrefixLength;
            if (start.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var b = start.GetAddressBytes();
                bool v4compat = prefix >= 96 && b.Take(10).All(x => x == 0) &&
                                ((b[10] == 0 && b[11] == 0) || (b[10] == 0xFF && b[11] == 0xFF));
                if (v4compat) { start = new IPAddress(b[12..16]); prefix -= 96; }
                else if (b[0] == 0x20 && b[1] == 0x02) continue; // alias 6to4
            }
            var s = $"{start}/{prefix}";
            if (seen.Add(s)) list.Add(s);
        }
        _rangeCache[cc] = list;
        return list;
    }

    public void Dispose() => _city?.Dispose();

    sealed class ProgressStream : Stream
    {
        readonly Stream _s; readonly long _total; readonly Action<double> _cb; long _read; int _last = -1;
        public ProgressStream(Stream s, long total, Action<double> cb) { _s = s; _total = total; _cb = cb; }
        public override int Read(byte[] buffer, int offset, int count) { int n = _s.Read(buffer, offset, count); Report(n); return n; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { int n = await _s.ReadAsync(buffer, ct); Report(n); return n; }
        void Report(int n)
        {
            _read += n; if (_total <= 0) return;
            int p = (int)(_read * 100 / _total);
            if (p != _last) { _last = p; _cb(p); }
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => _total; public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}
