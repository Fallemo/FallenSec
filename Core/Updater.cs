using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace FallenSec.Core;

public sealed record UpdateInfo(Version Version, string Tag, string Notes, string ZipUrl, string ZipName, string ShaUrl, long Size, string PageUrl);

/// <summary>
/// Actualizări prin GitHub Releases. Ultima versiune publicată are atașate FallenSec-vX.Y.zip
/// (folderul FallenSec complet) și FallenSec-vX.Y.zip.sha256. Zip-ul se verifică OBLIGATORIU
/// (SHA-256), se dezarhivează lângă aplicație, apoi fiecare fișier îl înlocuiește pe cel vechi:
/// fișierele în uz (exe, dll) nu pot fi suprascrise, dar pot fi redenumite (*.fsold), iar la
/// următoarea pornire sunt șterse. Dacă ceva eșuează la mijloc, totul revine la versiunea veche.
/// </summary>
public static class Updater
{
    public const string Repo = "Fallemo/FallenSec";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases";
    const string OldSuffix = ".fsold";
    static readonly Regex ZipName = new(@"^FallenSec-v[0-9.]+\.zip$", RegexOptions.IgnoreCase);

    static readonly HttpClient Http = CreateClient();
    static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("FallenSec-Updater/" + AppInfoConst.Version);
        return h;
    }

    public static Version Current => Version.Parse(AppInfoConst.Version);
    static string AppDir => AppContext.BaseDirectory.TrimEnd('\\', '/');
    static string StagingDir => Path.Combine(AppDir, ".update");

    /// <summary>null = nu există nicio versiune publicată care să poată fi instalată automat.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await Http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var r = doc.RootElement;
        var tag = r.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;

        string? zip = null, zipName = null; long size = 0;
        var assets = r.GetProperty("assets").EnumerateArray().ToList();
        foreach (var a in assets)
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (!ZipName.IsMatch(name)) continue;
            zip = a.GetProperty("browser_download_url").GetString(); zipName = name;
            size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            break;
        }
        if (zip == null || zipName == null) return null;
        string? sha = assets.Where(a => string.Equals(a.GetProperty("name").GetString(), zipName + ".sha256", StringComparison.OrdinalIgnoreCase))
                            .Select(a => a.GetProperty("browser_download_url").GetString()).FirstOrDefault();
        if (sha == null) return null; // fără sumă de control nu instalăm nimic automat

        var notes = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var page = r.TryGetProperty("html_url", out var hu) ? hu.GetString() ?? ReleasesPage : ReleasesPage;
        return new UpdateInfo(v, tag, notes, zip, zipName, sha, size, page);
    }

    public static bool IsNewer(UpdateInfo u) => u.Version > Current;

    /// <summary>Descarcă, verifică și instalează peste folderul curent. Nu repornește aplicația.</summary>
    public static async Task InstallAsync(UpdateInfo u, IProgress<(long done, long? total)> progress, CancellationToken ct)
    {
        CleanupStaging();
        Directory.CreateDirectory(StagingDir);
        string zipPath = Path.Combine(StagingDir, u.ZipName);
        string extract = Path.Combine(StagingDir, "x");
        try
        {
            // 1. descărcare
            using (var resp = await Http.GetAsync(u.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength ?? (u.Size > 0 ? u.Size : null);
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                var buf = new byte[1 << 16];
                long done = 0, last = 0; int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (done - last > 256 * 1024) { progress.Report((done, total)); last = done; }
                }
                progress.Report((done, total));
            }

            // 2. verificare SHA-256 (obligatorie)
            var expected = (await Http.GetStringAsync(u.ShaUrl, ct)).Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
            string actual;
            await using (var fs = File.OpenRead(zipPath))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            if (expected.Length != 64 || actual != expected)
                throw new InvalidDataException(L.T("Fișierul descărcat nu se potrivește (SHA-256 greșit). Încearcă din nou.",
                                                   "The downloaded file does not match (wrong SHA-256). Please try again."));

            // 3. dezarhivare (cu protecție la căi care ies din folder)
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(extract);
            string extractFull = Path.GetFullPath(extract) + Path.DirectorySeparatorChar;
            using (var za = ZipFile.OpenRead(zipPath))
            {
                foreach (var e in za.Entries)
                {
                    var dest = Path.GetFullPath(Path.Combine(extract, e.FullName));
                    if (!dest.StartsWith(extractFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Zip invalid: " + e.FullName);
                    if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest, true);
                }
            }
            string root = File.Exists(Path.Combine(extract, "FallenSec.exe")) ? extract : Path.Combine(extract, "FallenSec");
            string newExe = Path.Combine(root, "FallenSec.exe");
            if (!File.Exists(newExe) || !File.Exists(Path.Combine(root, "FallenSec.dll")))
                throw new InvalidDataException(L.T("Arhiva nu conține FallenSec.", "The archive does not contain FallenSec."));
            var fv = FileVersionInfo.GetVersionInfo(newExe).FileVersion ?? "";
            if (!fv.StartsWith(u.Version.ToString(2)))
                throw new InvalidDataException(L.T($"Versiunea din arhivă ({fv}) nu corespunde cu {u.Tag}.", $"The archive version ({fv}) does not match {u.Tag}."));

            // 4. înlocuire, cu revenire completă la eroare
            Apply(root);
        }
        finally
        {
            CleanupStaging();
        }
    }

    static void Apply(string root)
    {
        var renamed = new List<(string target, string old)>();
        var placed = new List<string>();
        try
        {
            foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, f);
                var target = Path.Combine(AppDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    var old = target + OldSuffix;
                    try { if (File.Exists(old)) File.Delete(old); }
                    catch { old = target + OldSuffix + "-" + DateTime.Now.Ticks; }
                    File.Move(target, old);
                    renamed.Add((target, old));
                }
                File.Move(f, target);
                placed.Add(target);
            }
        }
        catch
        {
            foreach (var p in placed) try { File.Delete(p); } catch { }
            foreach (var (target, old) in renamed) try { File.Move(old, target, true); } catch { }
            throw;
        }
    }

    /// <summary>La pornire: șterge fișierele vechi rămase după o actualizare și folderul temporar.</summary>
    public static void CleanupOld()
    {
        try
        {
            foreach (var f in Directory.GetFiles(AppDir, "*" + OldSuffix + "*", SearchOption.AllDirectories))
                try { File.Delete(f); } catch { }
        }
        catch { }
        CleanupStaging();
    }

    static void CleanupStaging()
    {
        try { if (Directory.Exists(StagingDir)) Directory.Delete(StagingDir, true); } catch { }
    }
}
