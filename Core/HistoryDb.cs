using Microsoft.Data.Sqlite;

namespace FallenSec.Core;

public sealed class HistoryDb : IDisposable
{
    readonly SqliteConnection _c;
    readonly object _lock = new();

    public HistoryDb()
    {
        _c = new SqliteConnection($"Data Source={Paths.History}");
        _c.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
        Exec(@"
CREATE TABLE IF NOT EXISTS traffic(minute INTEGER NOT NULL, app TEXT NOT NULL, ip TEXT, host TEXT, cc TEXT, port INTEGER, proto TEXT, up INTEGER, down INTEGER);
CREATE INDEX IF NOT EXISTS ix_traffic_minute ON traffic(minute);
CREATE INDEX IF NOT EXISTS ix_traffic_app ON traffic(app, minute);
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

    public void Cleanup(int days)
    {
        lock (_lock)
        {
            using var cmd = _c.CreateCommand();
            cmd.CommandText = "DELETE FROM traffic WHERE minute < $m; DELETE FROM events WHERE ts < $t;";
            cmd.Parameters.AddWithValue("$m", (Now - days * 86400L) / 60);
            cmd.Parameters.AddWithValue("$t", Now - days * 86400L);
            cmd.ExecuteNonQuery();
        }
    }

    public void Clear()
    {
        lock (_lock) Exec("DELETE FROM traffic; DELETE FROM events; VACUUM;");
    }

    /// <summary>Statistici pentru pagina Istoric.</summary>
    public object Query(int hours, string? appFilter)
    {
        lock (_lock)
        {
            long from = (Now - hours * 3600L) / 60;
            int bucket = hours <= 1 ? 1 : hours <= 24 ? 15 : hours <= 168 ? 60 : 360; // minute
            string af = appFilter != null ? " AND t.app=$app" : "";

            List<object> Rows(string sql, Func<SqliteDataReader, object> map)
            {
                using var cmd = _c.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$from", from);
                cmd.Parameters.AddWithValue("$b", bucket);
                if (appFilter != null) cmd.Parameters.AddWithValue("$app", appFilter);
                using var r = cmd.ExecuteReader();
                var l = new List<object>();
                while (r.Read()) l.Add(map(r));
                return l;
            }
            static string? S(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

            var timeline = Rows($"SELECT (t.minute/$b)*$b AS b, SUM(t.up), SUM(t.down) FROM traffic t WHERE t.minute>=$from{af} GROUP BY b ORDER BY b",
                r => new { t = r.GetInt64(0) * 60, up = r.GetInt64(1), down = r.GetInt64(2) });
            var apps = Rows($"SELECT t.app, a.name, a.path, a.icon, SUM(t.up) u, SUM(t.down) d, COUNT(DISTINCT t.ip), COUNT(DISTINCT t.cc) FROM traffic t LEFT JOIN apps a ON a.key=t.app WHERE t.minute>=$from{af} GROUP BY t.app ORDER BY u+d DESC LIMIT 50",
                r => new { key = r.GetString(0), name = S(r, 1) ?? r.GetString(0), path = S(r, 2), icon = S(r, 3), up = r.GetInt64(4), down = r.GetInt64(5), ips = r.GetInt64(6), countries = r.GetInt64(7) });
            var countries = Rows($"SELECT t.cc, SUM(t.up), SUM(t.down), COUNT(DISTINCT t.app) FROM traffic t WHERE t.minute>=$from{af} GROUP BY t.cc ORDER BY SUM(t.up+t.down) DESC LIMIT 40",
                r => new { cc = r.GetString(0), up = r.GetInt64(1), down = r.GetInt64(2), apps = r.GetInt64(3) });
            var hosts = Rows($"SELECT COALESCE(t.host, t.ip) h, t.cc, SUM(t.up), SUM(t.down), COUNT(DISTINCT t.app), MAX(t.minute) FROM traffic t WHERE t.minute>=$from{af} GROUP BY h ORDER BY SUM(t.up+t.down) DESC LIMIT 60",
                r => new { host = r.GetString(0), cc = S(r, 1), up = r.GetInt64(2), down = r.GetInt64(3), apps = r.GetInt64(4), last = r.GetInt64(5) * 60 });
            var ports = Rows($"SELECT t.port, t.proto, SUM(t.up+t.down) FROM traffic t WHERE t.minute>=$from{af} GROUP BY t.port, t.proto ORDER BY 3 DESC LIMIT 15",
                r => new { port = r.GetInt64(0), proto = r.GetString(1), bytes = r.GetInt64(2) });

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
            return new { type = "history", hours, bucket = bucket * 60, app = appFilter, timeline, apps, countries, hosts, ports, events };
        }
    }

    public void Dispose() { lock (_lock) _c.Dispose(); }
}
