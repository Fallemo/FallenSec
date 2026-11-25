using Microsoft.Data.Sqlite;

namespace FallenSec.Core;

public sealed class HistoryDb : IDisposable
{
    readonly SqliteConnection _c;
    readonly object _lock = new();

    public HistoryDb(string? path = null)
    {
        _c = new SqliteConnection($"Data Source={path ?? Paths.History}");
        _c.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY;");
        Exec(@"
CREATE TABLE IF NOT EXISTS traffic(minute INTEGER NOT NULL, app TEXT NOT NULL, ip TEXT, host TEXT, cc TEXT, port INTEGER, proto TEXT, up INTEGER, down INTEGER);
CREATE INDEX IF NOT EXISTS ix_traffic_minute ON traffic(minute);
CREATE INDEX IF NOT EXISTS ix_traffic_app ON traffic(app, minute);
CREATE TABLE IF NOT EXISTS traffic_h(hour INTEGER NOT NULL, app TEXT NOT NULL, ip TEXT, host TEXT, cc TEXT, port INTEGER, proto TEXT, up INTEGER, down INTEGER);
CREATE INDEX IF NOT EXISTS ix_traffich_hour ON traffic_h(hour);
CREATE INDEX IF NOT EXISTS ix_traffich_app ON traffic_h(app, hour);
CREATE TABLE IF NOT EXISTS meta(k TEXT PRIMARY KEY, v INTEGER);
CREATE TABLE IF NOT EXISTS apps(key TEXT PRIMARY KEY, name TEXT, path TEXT, first_seen INTEGER, last_seen INTEGER, icon TEXT);
CREATE TABLE IF NOT EXISTS events(ts INTEGER NOT NULL, kind TEXT, app TEXT, detail TEXT);
CREATE INDEX IF NOT EXISTS ix_events_ts ON events(ts);");
    }

    void Exec(string sql) { using var cmd = _c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public HashSet<string> KnownApps()
    {
        lock (_lock)
        {
            var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = _c.CreateCommand(); cmd.CommandText = "SELECT key FROM apps";
            using var r = cmd.ExecuteReader();
            while (r.Read()) s.Add(r.GetString(0));
            return s;
        }
    }

    public void UpsertApp(AppInfo a, bool isNew)
    {
        lock (_lock)
        {
            using var cmd = _c.CreateCommand();
            cmd.CommandText = isNew
                ? "INSERT OR REPLACE INTO apps(key,name,path,first_seen,last_seen,icon) VALUES($k,$n,$p,$t,$t,$i)"
                : "UPDATE apps SET last_seen=$t, name=$n, icon=COALESCE($i, icon) WHERE key=$k";
            cmd.Parameters.AddWithValue("$k", a.Key);
            cmd.Parameters.AddWithValue("$n", a.Name);
            cmd.Parameters.AddWithValue("$p", a.Path);
            cmd.Parameters.AddWithValue("$t", Now);
            cmd.Parameters.AddWithValue("$i", (object?)a.Icon ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void AddEvent(string kind, string? app, string detail)
    {
        lock (_lock)
        {
            using var cmd = _c.CreateCommand();
            cmd.CommandText = "INSERT INTO events(ts,kind,app,detail) VALUES($t,$k,$a,$d)";
            cmd.Parameters.AddWithValue("$t", Now);
            cmd.Parameters.AddWithValue("$k", kind);
            cmd.Parameters.AddWithValue("$a", (object?)app ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$d", detail);
            cmd.ExecuteNonQuery();
        }
    }

    public sealed record TrafficRow(long Minute, string App, string Ip, string? Host, string Cc, int Port, string Proto, long Up, long Down);

    public void WriteTraffic(IEnumerable<TrafficRow> rows)
    {
        lock (_lock)
        {
            using var tx = _c.BeginTransaction();
            using var cmd = _c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO traffic(minute,app,ip,host,cc,port,proto,up,down) VALUES($m,$a,$i,$h,$c,$p,$pr,$u,$d)";
            var pm = cmd.Parameters.Add("$m", SqliteType.Integer); var pa = cmd.Parameters.Add("$a", SqliteType.Text);
            var pi = cmd.Parameters.Add("$i", SqliteType.Text); var ph = cmd.Parameters.Add("$h", SqliteType.Text);
            var pc = cmd.Parameters.Add("$c", SqliteType.Text); var pp = cmd.Parameters.Add("$p", SqliteType.Integer);
            var ppr = cmd.Parameters.Add("$pr", SqliteType.Text);
            var pu = cmd.Parameters.Add("$u", SqliteType.Integer); var pd = cmd.Parameters.Add("$d", SqliteType.Integer);
            foreach (var r in rows)
            {
                pm.Value = r.Minute; pa.Value = r.App; pi.Value = r.Ip; ph.Value = (object?)r.Host ?? DBNull.Value;
                pc.Value = r.Cc; pp.Value = r.Port; ppr.Value = r.Proto; pu.Value = r.Up; pd.Value = r.Down;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    /// <summary>Câte zile păstrăm traficul la nivel de minut; mai vechi de atât îl comprimăm pe ore.</summary>
    public const int DetailDays = 30;
    /// <summary>După câte zile comprimăm orele în zile (miezul nopții local).</summary>
    public const int HourDays = 90;

    /// <summary>
    /// Întreținere periodică: comprimă pe ore traficul mai vechi de <see cref="DetailDays"/> zile
    /// (totalurile rămân exacte) și, dacă <paramref name="keepDays"/> &gt; 0, șterge ce e mai vechi de atât.
    /// keepDays = 0 înseamnă „păstrează pentru totdeauna”.
    /// </summary>
    public void Maintain(int keepDays, int detailDays = DetailDays, int hourDays = HourDays)
    {
        lock (_lock)
        {
            long cutHour = (Now - detailDays * 86400L) / 3600;   // ore întregi, ca o oră să nu fie împărțită între tabele
            using (var tx = _c.BeginTransaction())
            using (var cmd = _c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO traffic_h(hour,app,ip,host,cc,port,proto,up,down)
  SELECT minute/60, app, MIN(ip), COALESCE(host, ip), cc, port, proto, SUM(up), SUM(down)
  FROM traffic WHERE minute < $cut
  GROUP BY minute/60, app, COALESCE(host, ip), cc, port, proto;
DELETE FROM traffic WHERE minute < $cut;";
                cmd.Parameters.AddWithValue("$cut", cutHour * 60);
                int n = cmd.ExecuteNonQuery();
                tx.Commit();
                if (n > 0) Log.W($"Istoric: {n} rânduri comprimate pe ore");
            }
            // treapta a doua: orele mai vechi de hourDays zile devin rânduri pe zi (aliniate la miezul nopții local).
            // Procesăm doar intervalul nou față de ultima rulare (meta.daily_done), ca munca să nu crească în timp.
            int offH = (int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalHours);
            long dayCut = (Now - hourDays * 86400L) / 3600;
            dayCut = (dayCut + offH) / 24 * 24 - offH;
            long done;
            using (var cmd = _c.CreateCommand()) { cmd.CommandText = "SELECT v FROM meta WHERE k='daily_done'"; done = cmd.ExecuteScalar() is long d ? d : 0; }
            if (dayCut > done)
            {
                using var tx = _c.BeginTransaction();
                using var cmd = _c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
CREATE TEMP TABLE IF NOT EXISTS fs_day AS SELECT * FROM traffic_h WHERE 0;
DELETE FROM fs_day;
INSERT INTO fs_day(hour,app,ip,host,cc,port,proto,up,down)
  SELECT ((hour+$off)/24)*24-$off, app, MIN(ip), host, cc, port, proto, SUM(up), SUM(down)
  FROM traffic_h WHERE hour >= $done AND hour < $cut
  GROUP BY ((hour+$off)/24), app, host, cc, port, proto;
DELETE FROM traffic_h WHERE hour >= $done AND hour < $cut;
INSERT INTO traffic_h SELECT * FROM fs_day;
DELETE FROM fs_day;
INSERT OR REPLACE INTO meta(k,v) VALUES('daily_done', $cut);";
                cmd.Parameters.AddWithValue("$off", offH);
                cmd.Parameters.AddWithValue("$done", done);
                cmd.Parameters.AddWithValue("$cut", dayCut);
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            if (keepDays > 0)
            {
                using var cmd = _c.CreateCommand();
                cmd.CommandText = "DELETE FROM traffic WHERE minute < $m; DELETE FROM traffic_h WHERE hour < $h; DELETE FROM events WHERE ts < $t;";
                long t = Now - keepDays * 86400L;
                cmd.Parameters.AddWithValue("$m", t / 60);
                cmd.Parameters.AddWithValue("$h", t / 3600);
                cmd.Parameters.AddWithValue("$t", t);
                cmd.ExecuteNonQuery();
            }
        }
    }

    public void Clear()
    {
        lock (_lock) Exec("DELETE FROM traffic; DELETE FROM traffic_h; DELETE FROM events; VACUUM;");
    }

    /// <summary>Primul moment (unix sec) pentru care avem trafic salvat.</summary>
    long FirstTs()
    {
        using var cmd = _c.CreateCommand();
        cmd.CommandText = "SELECT MIN(x) FROM (SELECT MIN(hour)*3600 x FROM traffic_h UNION ALL SELECT MIN(minute)*60 FROM traffic)";
        var v = cmd.ExecuteScalar();
        return v is long l ? l : Now;
    }

    /// <summary>Statistici pentru pagina Istoric. hours = 0 înseamnă „tot istoricul”.</summary>
    public object Query(int hours, string? appFilter)
    {
        lock (_lock)
        {
            long now = Now;
            long fromTs = hours > 0 ? now - hours * 3600L : Math.Min(FirstTs(), now - 3600);
            double spanH = (now - fromTs) / 3600.0;
            // mărimea unei bare din grafic, în minute
            int bucket = spanH <= 1.01 ? 1 : spanH <= 24.1 ? 15 : spanH <= 168.1 ? 60 : spanH <= 745 ? 360 : spanH <= 24 * 400 ? 1440 : 10080;
            // barele de o zi/săptămână încep la miezul nopții local, nu UTC
            int off = bucket >= 1440 ? (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes : 0;
            long from = fromTs / 60;
            long startMin = (from + off) / bucket * bucket - off;
            string af = appFilter != null ? " AND app=$app" : "";
            // ultimele 30 de zile: pe minut (traffic); mai vechi: comprimate pe ore (traffic_h)
            // citim perioada o singură dată într-un tabel temporar (în memorie), apoi calculăm statisticile din el
            using (var cmd = _c.CreateCommand())
            {
                cmd.CommandText = $@"DROP TABLE IF EXISTS temp.src;
CREATE TEMP TABLE src AS
  SELECT minute, app, ip, host, cc, port, proto, up, down FROM traffic WHERE minute>=$from{af}
  UNION ALL
  SELECT hour*60, app, ip, host, cc, port, proto, up, down FROM traffic_h WHERE hour>=$fromH{af};";
                cmd.Parameters.AddWithValue("$from", from);
                cmd.Parameters.AddWithValue("$fromH", from / 60);
                if (appFilter != null) cmd.Parameters.AddWithValue("$app", appFilter);
                cmd.ExecuteNonQuery();
            }

            List<object> Rows(string sql, Func<SqliteDataReader, object> map)
            {
                using var cmd = _c.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$b", bucket);
                cmd.Parameters.AddWithValue("$off", off);
                using var r = cmd.ExecuteReader();
                var l = new List<object>();
                while (r.Read()) l.Add(map(r));
                return l;
            }
            static string? S(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

            var timeline = Rows("SELECT ((t.minute+$off)/$b)*$b-$off AS b, SUM(t.up), SUM(t.down) FROM src t GROUP BY b ORDER BY b",
                r => new { t = r.GetInt64(0) * 60, up = r.GetInt64(1), down = r.GetInt64(2) });
            var apps = Rows("SELECT t.app, a.name, a.path, a.icon, SUM(t.up) u, SUM(t.down) d, COUNT(DISTINCT t.ip), COUNT(DISTINCT t.cc) FROM src t LEFT JOIN apps a ON a.key=t.app GROUP BY t.app ORDER BY u+d DESC LIMIT 50",
                r => new { key = r.GetString(0), name = S(r, 1) ?? r.GetString(0), path = S(r, 2), icon = S(r, 3), up = r.GetInt64(4), down = r.GetInt64(5), ips = r.GetInt64(6), countries = r.GetInt64(7) });
            var countries = Rows("SELECT t.cc, SUM(t.up), SUM(t.down), COUNT(DISTINCT t.app) FROM src t GROUP BY t.cc ORDER BY SUM(t.up+t.down) DESC LIMIT 40",
                r => new { cc = S(r, 0) ?? "", up = r.GetInt64(1), down = r.GetInt64(2), apps = r.GetInt64(3) });
            var hosts = Rows("SELECT COALESCE(t.host, t.ip) h, t.cc, SUM(t.up), SUM(t.down), COUNT(DISTINCT t.app), MAX(t.minute) FROM src t GROUP BY h ORDER BY SUM(t.up+t.down) DESC LIMIT 60",
                r => new { host = S(r, 0) ?? "?", cc = S(r, 1), up = r.GetInt64(2), down = r.GetInt64(3), apps = r.GetInt64(4), last = r.GetInt64(5) * 60 });
            var ports = Rows("SELECT t.port, t.proto, SUM(t.up+t.down) FROM src t GROUP BY t.port, t.proto ORDER BY 3 DESC LIMIT 15",
                r => new { port = r.GetInt64(0), proto = S(r, 1) ?? "", bytes = r.GetInt64(2) });
            // sumar pe luni (ora locală) pentru perioadele lungi
            var months = spanH > 24 * 28
                ? Rows("SELECT strftime('%Y-%m', t.minute*60, 'unixepoch', 'localtime') mo, SUM(t.up), SUM(t.down) FROM src t GROUP BY mo ORDER BY mo",
                    r => new { month = r.GetString(0), up = r.GetInt64(1), down = r.GetInt64(2) })
                : new List<object>();

            List<object> events;
            using (var cmd = _c.CreateCommand())
            {
                cmd.CommandText = "SELECT e.ts, e.kind, e.app, e.detail, a.name FROM events e LEFT JOIN apps a ON a.key=e.app WHERE e.ts>=$t" + (appFilter != null ? " AND e.app=$app" : "") + " ORDER BY e.ts DESC LIMIT 200";
                cmd.Parameters.AddWithValue("$t", from * 60);
                if (appFilter != null) cmd.Parameters.AddWithValue("$app", appFilter);
                using var r = cmd.ExecuteReader();
                events = new();
                while (r.Read()) events.Add(new { ts = r.GetInt64(0), kind = r.GetString(1), app = S(r, 2), detail = S(r, 3), appName = S(r, 4) });
            }
            Exec("DROP TABLE IF EXISTS temp.src");
            return new { type = "history", hours, bucket = bucket * 60, start = startMin * 60, since = FirstTs(), app = appFilter, timeline, apps, countries, hosts, ports, months, events };
        }
    }

    public void Dispose() { lock (_lock) _c.Dispose(); }
}

/// <summary>Test pe o copie a bazei: comprimă (detailDays zile) și verifică totalurile. --history-test &lt;db&gt; [detailDays]</summary>
public static class HistoryTest
{
    public static string Run(string db, int detailDays, int hourDays)
    {
        var sb = new System.Text.StringBuilder();
        long Count(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L); }
        string Totals(HistoryDb h, int hours)
        {
            var q = System.Text.Json.JsonSerializer.Serialize(h.Query(hours, null));
            using var d = System.Text.Json.JsonDocument.Parse(q);
            long up = 0, down = 0; int n = 0;
            foreach (var t in d.RootElement.GetProperty("timeline").EnumerateArray()) { up += t.GetProperty("up").GetInt64(); down += t.GetProperty("down").GetInt64(); n++; }
            long appsUp = d.RootElement.GetProperty("apps").EnumerateArray().Sum(a => a.GetProperty("up").GetInt64());
            return $"h={hours} bucket={d.RootElement.GetProperty("bucket")} bare={n} up={up} down={down} months={d.RootElement.GetProperty("months").GetArrayLength()} hosts={d.RootElement.GetProperty("hosts").GetArrayLength()} appsUp={appsUp}";
        }
        using (var c = new SqliteConnection($"Data Source={db}")) { c.Open();
            sb.AppendLine($"înainte: traffic={Count(c, "SELECT COUNT(*) FROM traffic")} sumUp={Count(c, "SELECT COALESCE(SUM(up),0) FROM traffic")} sumDown={Count(c, "SELECT COALESCE(SUM(down),0) FROM traffic")}"); }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (var h = new HistoryDb(db))
        {
            foreach (var hr in new[] { 24, 168, 8760, 0 }) sb.AppendLine("  " + Totals(h, hr));
            sw.Restart(); h.Maintain(0, detailDays, hourDays); sb.AppendLine($"Maintain({detailDays} zile minut, {hourDays} zile oră): {sw.ElapsedMilliseconds} ms");
            foreach (var hr in new[] { 24, 168, 8760, 0 }) { sw.Restart(); var t = Totals(h, hr); sb.AppendLine($"  {t}  ({sw.ElapsedMilliseconds} ms)"); }
        }
        using (var c = new SqliteConnection($"Data Source={db}")) { c.Open();
            sb.AppendLine($"după: traffic={Count(c, "SELECT COUNT(*) FROM traffic")} traffic_h={Count(c, "SELECT COUNT(*) FROM traffic_h")} sumUp={Count(c, "SELECT COALESCE(SUM(up),0) FROM traffic")+Count(c, "SELECT COALESCE(SUM(up),0) FROM traffic_h")} sumDown={Count(c, "SELECT COALESCE(SUM(down),0) FROM traffic")+Count(c, "SELECT COALESCE(SUM(down),0) FROM traffic_h")}");
            using var v = c.CreateCommand(); v.CommandText = "VACUUM"; v.ExecuteNonQuery(); }
        sb.AppendLine($"mărime după VACUUM: {new FileInfo(db).Length / 1024 / 1024.0:0.0} MB");
        return sb.ToString();
    }
}
