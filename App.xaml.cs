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
    Forms.ToolStripMenuItem? _panicItem, _openItem, _exitItem;
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
        _mutex = new Mutex(true, "Global\\FallenSec_SingleInstance", out bool first);
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

        Engine = new Engine();
        Engine.Notify += (t, m) => Dispatcher.BeginInvoke(() => _tray?.ShowBalloonTip(4000, t, m, Forms.ToolTipIcon.Info));
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

        bool tray = e.Args.Contains("--tray") || Engine.Settings.StartMinimized;
        _win = new MainWindow(Engine);
        if (!tray) _win.Show();
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
        _tray.BalloonTipClicked += (_, _) => ShowMain();
    }

    public void ApplyTrayLanguage()
    {
        if (_openItem != null) _openItem.Text = L.T("Deschide FallenSec", "Open FallenSec");
        if (_panicItem != null) _panicItem.Text = L.T("Taie tot internetul (panică)", "Cut all internet (panic)");
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
        _tray.ShowBalloonTip(5000, L.T("FallenSec rulează în tray", "FallenSec is running in the tray"),
            L.T("Monitorizarea continuă în fundal. Click pe iconiță pentru a redeschide, click dreapta pentru meniu.",
                "Monitoring continues in the background. Click the icon to reopen it, right-click for the menu."), Forms.ToolTipIcon.Info);
    }

    public void ExitApp()
    {
        try { _tray!.Visible = false; _tray.Dispose(); } catch { }
        try { Engine.Dispose(); } catch { }
        _win?.ForceClose();
        Shutdown();
    }
}
