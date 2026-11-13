using System.Diagnostics;
using System.Windows;
using FallenSec.Core;
using Forms = System.Windows.Forms;

namespace FallenSec;

public partial class App : System.Windows.Application
{
    Mutex? _mutex;
    EventWaitHandle? _showEvent;
    public Engine Engine { get; private set; } = null!;
    Forms.NotifyIcon? _tray;
    Forms.ToolStripMenuItem? _panicItem, _openItem, _updItem, _exitItem;
    MainWindow? _win;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // limba trebuie stabilită înaintea oricărui text (inclusiv câmpurile statice din Core)
        try { L.Set(Settings.LoadOrDefault().Language); } catch { L.Set(null); }
        if (e.Args.Contains("--wfptest"))
        {
            var res = WfpService.SelfTest();
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "fallensec-wfptest.txt"), res); } catch { }
            Shutdown();
            return;
        }
        if (e.Args.Contains("--update-test"))
        {
            UpdateTest(e.Args.Contains("--force"));
            Shutdown();
            return;
        }

        // După o actualizare, instanța veche încă se închide: așteptăm să elibereze mutex-ul.
        bool afterUpdate = e.Args.Contains("--after-update");
        _mutex = new Mutex(true, "Global\\FallenSec_SingleInstance", out bool first);
        if (!first && afterUpdate)
        {
            try { first = _mutex.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { first = true; }
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\FallenSec_Show");
        if (!first)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }
        new Thread(() =>
        {
            while (_showEvent.WaitOne()) Dispatcher.BeginInvoke(ShowMain);
        }) { IsBackground = true }.Start();

        DispatcherUnhandledException += (_, ex) => { Log.W("UI: " + ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.W("Fatal: " + ex.ExceptionObject);

        Updater.CleanupOld();
        if (afterUpdate) Log.W("Actualizat la " + AppInfoConst.Version);

        Engine = new Engine();
        Engine.Notify += (t, m) => Dispatcher.BeginInvoke(() => { _balloonIsUpdate = false; _tray?.ShowBalloonTip(4000, t, m, Forms.ToolTipIcon.Info); });
        Engine.Rates += (u, d) => Dispatcher.BeginInvoke(() =>
        {
            if (_tray != null)
            {
                var txt = $"FallenSec  ↑ {Fmt(u)}/s  ↓ {Fmt(d)}/s";
                _tray.Text = txt.Length > 63 ? txt[..63] : txt;
            }
        });
        Engine.Rules.Changed += () => Dispatcher.BeginInvoke(() => { if (_panicItem != null) _panicItem.Checked = Engine.Rules.PanicOn; });
        Engine.Start();
        CreateTray();

        bool tray = !afterUpdate && (e.Args.Contains("--tray") || Engine.Settings.StartMinimized);
        _win = new MainWindow(Engine);
        if (!tray) _win.Show();
        if (afterUpdate)
        {
            _balloonIsUpdate = false;
            _tray?.ShowBalloonTip(5000, L.T("FallenSec actualizat", "FallenSec updated"),
                L.T($"Rulezi acum versiunea {AppInfoConst.Version}. Regulile și setările au rămas neschimbate.",
                    $"You are now running version {AppInfoConst.Version}. Your rules and settings are unchanged."), Forms.ToolTipIcon.Info);
        }

        // verificare automată: la 20 s după pornire, apoi o dată pe zi
        _updTimer = new System.Threading.Timer(_ => Dispatcher.BeginInvoke(() => CheckForUpdates(manual: false)),
            null, TimeSpan.FromSeconds(20), TimeSpan.FromHours(24));
    }

    static string Fmt(double b) => b >= 1 << 20 ? $"{b / (1 << 20):0.0} MB" : b >= 1024 ? $"{b / 1024:0} KB" : $"{b:0} B";

    void CreateTray()
    {
        var sri = GetResourceStream(new Uri("pack://application:,,,/Assets/fallensec.ico"));
        var menu = new Forms.ContextMenuStrip();
        _openItem = new Forms.ToolStripMenuItem("", null, (_, _) => ShowMain());
        menu.Items.Add(_openItem);
        _panicItem = new Forms.ToolStripMenuItem("") { Checked = Engine.Rules.PanicOn };
        _panicItem.Click += (_, _) => Engine.SetPanic(!Engine.Rules.PanicOn);
        menu.Items.Add(_panicItem);
        _updItem = new Forms.ToolStripMenuItem("", null, (_, _) => { ShowMain(); CheckForUpdates(manual: true); });
        menu.Items.Add(_updItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _exitItem = new Forms.ToolStripMenuItem("", null, (_, _) => ExitApp());
        menu.Items.Add(_exitItem);
        ApplyTrayLanguage();
        _tray = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(sri.Stream),
            Text = "FallenSec",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) ShowMain(); };
        _tray.BalloonTipClicked += (_, _) =>
        {
            ShowMain();
            if (_balloonIsUpdate && UpdateAvail != null) PushUpdate(AvailableState(UpdateAvail, open: true));
        };
    }

    public void ApplyTrayLanguage()
    {
        if (_openItem != null) _openItem.Text = L.T("Deschide FallenSec", "Open FallenSec");
        if (_panicItem != null) _panicItem.Text = L.T("Taie tot internetul (panică)", "Cut all internet (panic)");
        if (_updItem != null) _updItem.Text = L.T("Verifică actualizări", "Check for updates");
        if (_exitItem != null) _exitItem.Text = L.T("Ieșire", "Exit");
    }

    public void ShowMain()
    {
        _win ??= new MainWindow(Engine);
        _win.Show();
        if (_win.WindowState == WindowState.Minimized) _win.WindowState = WindowState.Normal;
        _win.Activate();
        _win.Topmost = true; _win.Topmost = false;
    }

    public void TrayHint()
    {
        if (Engine.Settings.TrayHintShown || _tray == null) return;
        Engine.Settings.TrayHintShown = true;
        Engine.Settings.Save();
        _balloonIsUpdate = false;
        _tray.ShowBalloonTip(5000, L.T("FallenSec rulează în tray", "FallenSec is running in the tray"),
            L.T("Monitorizarea continuă în fundal. Click pe iconiță pentru a redeschide, click dreapta pentru meniu.",
                "Monitoring continues in the background. Click the icon to reopen it, right-click for the menu."), Forms.ToolTipIcon.Info);
    }

    public void ExitApp()
    {
        try { _updTimer?.Dispose(); } catch { }
        try { _tray!.Visible = false; _tray.Dispose(); } catch { }
        try { Engine.Dispose(); } catch { }
        _win?.ForceClose();
        Shutdown();
    }

    // ---------------------------------------------------------------- actualizări
    System.Threading.Timer? _updTimer;
    bool _updBusy, _balloonIsUpdate;
    string? _updNotifiedTag;
    public UpdateInfo? UpdateAvail { get; private set; }
    /// <summary>Ultima stare trimisă interfeței (retrimisă când pagina se reîncarcă).</summary>
    public Dictionary<string, object?>? UpdateState { get; private set; }

    void PushUpdate(Dictionary<string, object?> st)
    {
        st["type"] = "update";
        st["current"] = AppInfoConst.Version;
        UpdateState = st;
        _win?.SendUpdate(st);
    }

    static Dictionary<string, object?> AvailableState(UpdateInfo u, bool open) => new()
    {
        ["state"] = "available", ["tag"] = u.Tag, ["notes"] = u.Notes, ["page"] = u.PageUrl, ["size"] = u.Size, ["open"] = open,
    };

    /// <summary>manual = cerută de utilizator (arată și „ai ultima versiune” / erorile).</summary>
    public async void CheckForUpdates(bool manual)
    {
        if (_updBusy) { if (manual && UpdateState != null) PushUpdate(new(UpdateState) { ["open"] = true }); return; }
        if (!manual && !Engine.Settings.AutoUpdateCheck) return;
        _updBusy = true;
        if (manual) PushUpdate(new() { ["state"] = "checking", ["open"] = true });
        try
        {
            var u = await Updater.CheckAsync();
            if (u == null || !Updater.IsNewer(u))
            {
                UpdateAvail = null;
                if (manual) PushUpdate(new() { ["state"] = "latest", ["open"] = true });
                return;
            }
            UpdateAvail = u;
            PushUpdate(AvailableState(u, open: manual));
            if (!manual && _updNotifiedTag != u.Tag && _tray != null)
            {
                _updNotifiedTag = u.Tag;
                _balloonIsUpdate = true;
                _tray.ShowBalloonTip(10000, L.T($"FallenSec {u.Tag} disponibil", $"FallenSec {u.Tag} available"),
                    L.T("Click aici ca să vezi noutățile și să actualizezi.", "Click here to see what's new and update."), Forms.ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            Log.W("Update check: " + ex.Message);
            if (manual) PushUpdate(new() { ["state"] = "error", ["error"] = L.T("Nu am putut verifica actualizările: ", "Could not check for updates: ") + ex.Message, ["open"] = true });
        }
        finally { _updBusy = false; }
    }

    public async void InstallUpdate()
    {
        var u = UpdateAvail;
        if (u == null || _updBusy) return;
        _updBusy = true;
        var progress = new Progress<(long done, long? total)>(p =>
            PushUpdate(new() { ["state"] = "downloading", ["tag"] = u.Tag, ["done"] = p.done, ["total"] = p.total, ["open"] = true }));
        try
        {
            PushUpdate(new() { ["state"] = "downloading", ["tag"] = u.Tag, ["done"] = 0L, ["total"] = u.Size, ["open"] = true });
            // descărcarea, dezarhivarea și înlocuirea rulează în afara firului UI; progresul revine pe UI
            await Task.Run(() => Updater.InstallAsync(u, progress, CancellationToken.None));
            Log.W($"Update {u.Tag} instalat, repornesc");
            PushUpdate(new() { ["state"] = "restarting", ["tag"] = u.Tag, ["open"] = true });
            await Task.Delay(1200);
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--after-update") { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(L.T("Actualizarea e instalată, dar nu am putut reporni aplicația: ", "The update is installed, but the app could not restart: ")
                    + ex.Message + L.T("\nPornește FallenSec manual.", "\nPlease start FallenSec manually."), "FallenSec");
            }
            ExitApp();
        }
        catch (UnauthorizedAccessException)
        {
            _updBusy = false;
            PushUpdate(new() { ["state"] = "error", ["open"] = true, ["error"] = L.T(
                "Nu am drept de scriere în folderul aplicației. Pornește FallenSec ca administrator sau descarcă manual de pe pagina versiunii.",
                "No write access to the app folder. Run FallenSec as administrator or download it manually from the release page.") });
        }
        catch (Exception ex)
        {
            _updBusy = false;
            Log.W("Update install: " + ex);
            PushUpdate(new() { ["state"] = "error", ["open"] = true, ["error"] = L.T("Actualizarea nu a reușit (versiunea curentă a rămas neatinsă): ", "The update failed (the current version was left untouched): ") + ex.Message });
        }
    }

    /// <summary>FallenSec.exe --update-test [--force]: verifică și instalează ultima versiune peste folderul curent,
    /// fără interfață și fără repornire. --force instalează chiar dacă versiunea nu e mai nouă. Rezultat: %TEMP%\fallensec-update-test.txt</summary>
    static void UpdateTest(bool force)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Curent: {AppInfoConst.Version}, folder: {AppContext.BaseDirectory}");
        try
        {
            // pe un fir separat: firul UI e blocat aici, iar await-urile n-ar mai avea unde reveni
            Task.Run(async () =>
            {
                Updater.CleanupOld();
                var u = await Updater.CheckAsync();
                sb.AppendLine(u == null ? "Nicio versiune instalabilă (lipsă zip/sha256)" : $"Ultima publicată: {u.Tag}, zip: {u.ZipName}, mai nouă: {Updater.IsNewer(u)}");
                if (u != null && (force || Updater.IsNewer(u)))
                {
                    long last = 0;
                    await Updater.InstallAsync(u, new Progress<(long done, long? total)>(p => last = p.done), CancellationToken.None);
                    sb.AppendLine($"Instalat OK ({last} bytes descărcați)");
                }
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex) { sb.AppendLine("EROARE: " + ex); }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "fallensec-update-test.txt"), sb.ToString());
    }
}
