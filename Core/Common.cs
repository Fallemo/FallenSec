using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FallenSec.Core;

public static class AppInfoConst
{
    public const string Version = "1.4";
    public const string Copyright = "© 2026 Fallen";
}

/// <summary>Limba interfeței: "ro" sau "en". Setarea "auto" urmează limba Windows.</summary>
public static class L
{
    public static bool En { get; private set; }
    public static string Code => En ? "en" : "ro";

    public static void Set(string? setting)
    {
        En = setting switch
        {
            "en" => true,
            "ro" => false,
            _ => !System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ro", StringComparison.OrdinalIgnoreCase),
        };
    }

    public static string T(string ro, string en) => En ? en : ro;
}

public static class Paths
{
    public static readonly string Data = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FallenSec");
    public static readonly string Geo = Path.Combine(Data, "geo");
    public static string Rules => Path.Combine(Data, "rules.json");
    public static string Settings => Path.Combine(Data, "settings.json");
    public static string History => Path.Combine(Data, "history.db");
    public static string Log => Path.Combine(Data, "fallensec.log");
    public static readonly string WebCache = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FallenSec", "WebView2");

    static Paths()
    {
        try { Directory.CreateDirectory(Data); Directory.CreateDirectory(Geo); } catch { }
    }
}

public static class Log
{
    static readonly object L = new();
    public static void W(string msg)
    {
        try
        {
            lock (L)
            {
                var fi = new FileInfo(Paths.Log);
                if (fi.Exists && fi.Length > 2_000_000) fi.Delete();
                File.AppendAllText(Paths.Log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
            }
        }
        catch { }
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Opt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };
    public static readonly JsonSerializerOptions Pretty = new(Opt) { WriteIndented = true };

    public static T? Load<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Opt) : null; }
        catch (Exception e) { Log.W($"Load {path}: {e.Message}"); return null; }
    }
    public static void Save<T>(string path, T obj)
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(obj, Pretty));
            File.Move(tmp, path, true);
        }
        catch (Exception e) { Log.W($"Save {path}: {e.Message}"); }
    }
}

public sealed class Settings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public string BlockEngine { get; set; } = "wfp";
    public bool TrayHintShown { get; set; }
    public bool NotifyNewApps { get; set; } = true;
    public bool ShowLan { get; set; } = true;
    /// <summary>0 = păstrează istoricul pentru totdeauna (implicit din v1.4).</summary>
    public int HistoryDays { get; set; } = 0;
    public int SettingsVersion { get; set; }
    public bool HomeAuto { get; set; } = true;
    public double? HomeLat { get; set; }
    public double? HomeLon { get; set; }
    public string? HomeCity { get; set; }
    public bool Panic { get; set; }
    /// <summary>"auto" (limba Windows), "ro" sau "en".</summary>
    public string Language { get; set; } = "auto";
    /// <summary>Verifică pe GitHub dacă există versiune nouă (la pornire și apoi o dată pe zi).</summary>
    public bool AutoUpdateCheck { get; set; } = true;

    public static Settings LoadOrDefault()
    {
        var s = Json.Load<Settings>(Paths.Settings);
        if (s == null) return new Settings { SettingsVersion = 2 };
        if (s.SettingsVersion < 2)
        {
            // v1.4: istoricul nu mai expiră implicit; vechiul implicit (30 de zile) devine „pentru totdeauna”
            if (s.HistoryDays == 30) s.HistoryDays = 0;
            s.SettingsVersion = 2;
            s.Save();
        }
        return s;
    }
    public void Save() => Json.Save(Paths.Settings, this);
}

public sealed class AppInfo
{
    public string Key { get; init; } = "";
    public string Path { get; init; } = "";
    public string Name { get; set; } = "";
    public string? Publisher { get; set; }
    public bool IsSystem { get; set; }
    [JsonIgnore] public string? Icon { get; set; }
}

/// <summary>PID → aplicație (cu cache și iconițe).</summary>
public sealed class ProcessCache
{
    sealed class Entry { public AppInfo App = null!; public long Start; public DateTime Checked; }
    readonly Dictionary<int, Entry> _byPid = new();
    readonly Dictionary<string, AppInfo> _byKey = new(StringComparer.OrdinalIgnoreCase);
    static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    // L.Set() este apelat în App.OnStartup înainte de orice acces la aceste câmpuri
    public static readonly AppInfo SystemApp = new() { Key = "system", Path = "System", Name = L.T("Sistem Windows (kernel)", "Windows System (kernel)"), Publisher = "Microsoft", IsSystem = true };
    public static readonly AppInfo IdleApp = new() { Key = "idle", Path = "", Name = L.T("Conexiuni închise (TIME_WAIT)", "Closed connections (TIME_WAIT)"), IsSystem = true };
    static readonly AppInfo Unknown = new() { Key = "unknown", Path = "", Name = L.T("Proces necunoscut", "Unknown process"), IsSystem = true };

    public IEnumerable<AppInfo> Known => _byKey.Values;
    public AppInfo? ByKey(string key) => _byKey.TryGetValue(key, out var a) ? a : null;

    public AppInfo Get(int pid)
    {
        if (pid == 0) return IdleApp;
        if (pid == 4) return SystemApp;
        var now = DateTime.UtcNow;
        if (_byPid.TryGetValue(pid, out var e) && (now - e.Checked).TotalSeconds < 15) return e.App;

        var path = Native.GetProcessPath(pid, out long start);
        if (e != null && path != null && e.Start == start) { e.Checked = now; return e.App; }
        AppInfo app;
        if (path == null)
        {
            string name = "";
            try { name = Process.GetProcessById(pid).ProcessName; } catch { }
            app = string.IsNullOrEmpty(name) ? Unknown : GetByName(name + ".exe");
        }
        else app = GetByPath(path);
        _byPid[pid] = new Entry { App = app, Start = start, Checked = now };
        if (_byPid.Count > 4000) _byPid.Clear();
        return app;
    }

    AppInfo GetByName(string name)
    {
        var key = "name:" + name.ToLowerInvariant();
        if (_byKey.TryGetValue(key, out var a)) return a;
        a = new AppInfo { Key = key, Path = name, Name = name, IsSystem = true };
        _byKey[key] = a;
        return a;
    }

    public AppInfo GetByPath(string path)
    {
        var key = path.ToLowerInvariant();
        if (_byKey.TryGetValue(key, out var a)) return a;
        string name = System.IO.Path.GetFileName(path); string? pub = null;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(vi.FileDescription)) name = vi.FileDescription.Trim();
            pub = string.IsNullOrWhiteSpace(vi.CompanyName) ? null : vi.CompanyName.Trim();
        }
        catch { }
        var exe = System.IO.Path.GetFileName(path);
        if (exe.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase)) name = L.T("Servicii Windows (svchost)", "Windows services (svchost)");
        a = new AppInfo
        {
            Key = key, Path = path, Name = name, Publisher = pub,
            IsSystem = path.StartsWith(WinDir, StringComparison.OrdinalIgnoreCase),
            Icon = ExtractIcon(path)
        };
        _byKey[key] = a;
        return a;
    }

    static string? ExtractIcon(string path)
    {
        try
        {
            using var ico = Icon.ExtractAssociatedIcon(path);
            if (ico == null) return null;
            using var bmp = ico.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch { return null; }
    }
}
