using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FallenSec.Core;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FallenSec;

public sealed class MainWindow : Window
{
    readonly Engine _engine;
    readonly WebView2 _web;
    bool _ready, _forceClose;
    WindowState _lastState = WindowState.Normal;

    public MainWindow(Engine engine)
    {
        _engine = engine;
        Title = "FallenSec";
        Width = 1440; Height = 900; MinWidth = 980; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(8, 12, 24));
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/fallensec.ico")); } catch { }
        _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 8, 12, 24) };
        Content = _web;
        Loaded += async (_, _) => await InitWeb();
        IsVisibleChanged += (_, _) => UpdateActive();
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) _lastState = WindowState;
            else if (_engine.Settings.MinimizeToTray) { HideToTray(); return; }
            UpdateActive();
        };
        _engine.Snapshot += json => Post(json);
        _engine.Rules.Changed += () => Dispatcher.BeginInvoke(SendRules);
        _engine.Geo.Changed += () => { };
    }

    void UpdateActive() => _engine.UiActive = _ready && IsVisible && WindowState != WindowState.Minimized;

    async Task InitWeb()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Paths.WebCache);
            await _web.EnsureCoreWebView2Async(env);
            var cw = _web.CoreWebView2;
            var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            cw.SetVirtualHostNameToFolderMapping("app.fallensec", root, CoreWebView2HostResourceAccessKind.Allow);
            cw.Settings.AreDefaultContextMenusEnabled = false;
            cw.Settings.IsZoomControlEnabled = false;
            cw.Settings.IsStatusBarEnabled = false;
            cw.Settings.AreDevToolsEnabled = Environment.GetCommandLineArgs().Contains("--dev");
            cw.WebMessageReceived += OnMessage;
            cw.NewWindowRequested += (_, a) =>
            {
                a.Handled = true;
                try { Process.Start(new ProcessStartInfo(a.Uri) { UseShellExecute = true }); } catch { }
            };
            cw.Navigate(PageUrl);
        }
        catch (Exception e)
        {
            Log.W("WebView2: " + e);
            MessageBox.Show(L.T("Nu pot porni interfața (WebView2):\n", "Cannot start the interface (WebView2):\n") + e.Message, "FallenSec", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void Post(string json)
    {
        if (!_ready) return;
        Dispatcher.BeginInvoke(() =>
        {
            try { _web.CoreWebView2?.PostWebMessageAsJson(json); } catch { }
        });
    }

    static string PageUrl => "https://app.fallensec/index.html?lang=" + L.Code;

    void Send(object o) => Post(JsonSerializer.Serialize(o, Json.Opt));
    void Toast(string text, string kind = "info") => Send(new { type = "toast", kind, text });
    void SendRules() => Send(new { type = "rules", rules = _engine.Rules.Snapshot() });

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement m;
        try { m = JsonDocument.Parse(e.WebMessageAsJson).RootElement; } catch { return; }
        string cmd = m.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";
        string S(string p) => m.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        bool B(string p) => m.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;
        try
        {
            switch (cmd)
            {
                case "ready":
                    _ready = true;
                    _engine.ResetIcons();
                    UpdateActive();
                    Send(new
                    {
                        type = "init", version = AppInfoConst.Version, copyright = AppInfoConst.Copyright,
                        settings = _engine.Settings, rules = _engine.Rules.Snapshot(), dataDir = Paths.Data
                    });
                    break;
                case "toggleApp":
                    if (!_engine.IsAdmin) { Toast(L.T("Pentru blocare, pornește FallenSec ca administrator.", "To block apps, run FallenSec as administrator."), "error"); break; }
                    _engine.SetAppBlocked(S("key"), B("block"));
                    break;
                case "saveRule":
                    if (!_engine.IsAdmin) { Toast(L.T("Pentru reguli, pornește FallenSec ca administrator.", "To use rules, run FallenSec as administrator."), "error"); break; }
                    var rule = m.GetProperty("rule").Deserialize<Rule>(Json.Opt)!;
                    try { _engine.Rules.Upsert(rule); Toast(L.T("Regula a fost salvată.", "Rule saved."), "ok"); }
                    catch (ArgumentException ex) { Toast(ex.Message, "error"); }
                    break;
                case "toggleRule": _engine.Rules.SetEnabled(S("id"), B("enabled")); break;
                case "deleteRule": _engine.Rules.Delete(S("id")); Toast(L.T("Regula a fost ștearsă.", "Rule deleted."), "ok"); break;
                case "history":
                    int hours = m.TryGetProperty("hours", out var h) ? h.GetInt32() : 24;
                    var app = S("app"); var appF = string.IsNullOrEmpty(app) ? null : app;
                    Task.Run(() =>
                    {
                        try { if (_engine.Db != null) Send(_engine.Db.Query(hours, appF)); }
                        catch (Exception ex) { Log.W("History query: " + ex); }
                    });
                    break;
                case "clearHistory":
                    Task.Run(() => { _engine.Db?.Clear(); Toast(L.T("Istoricul a fost șters.", "History cleared."), "ok"); });
                    break;
                case "saveSettings":
                    var ns = m.GetProperty("settings").Deserialize<Settings>(Json.Opt)!;
                    var st = _engine.Settings;
                    bool startupChanged = st.StartWithWindows != ns.StartWithWindows;
                    bool homeChanged = st.HomeAuto != ns.HomeAuto || st.HomeLat != ns.HomeLat || st.HomeLon != ns.HomeLon;
                    st.StartWithWindows = ns.StartWithWindows; st.StartMinimized = ns.StartMinimized; st.CloseToTray = ns.CloseToTray; st.MinimizeToTray = ns.MinimizeToTray;
                    st.NotifyNewApps = ns.NotifyNewApps; st.ShowLan = ns.ShowLan; st.HistoryDays = Math.Clamp(ns.HistoryDays, 1, 365);
                    st.HomeAuto = ns.HomeAuto; st.HomeLat = ns.HomeLat; st.HomeLon = ns.HomeLon; st.HomeCity = ns.HomeCity;
                    st.Save();
                    if (startupChanged) Task.Run(_engine.ApplyStartup);
                    if (homeChanged) Task.Run(_engine.ResolveHomeAsync);
                    Toast(L.T("Setările au fost salvate.", "Settings saved."), "ok");
                    break;
                case "setLanguage":
                    var lang = S("language");
                    _engine.Settings.Language = lang is "ro" or "en" ? lang : "auto";
                    _engine.Settings.Save();
                    L.Set(_engine.Settings.Language);
                    ((App)Application.Current).ApplyTrayLanguage();
                    _engine.RefreshBlockStatus(true);
                    // reîncarcă interfața în noua limbă (unele denumiri din sistem se schimbă după repornire)
                    _ready = false;
                    _web.CoreWebView2?.Navigate(PageUrl + "#settings");
                    break;
                case "panic":
                    if (!_engine.IsAdmin) { Toast(L.T("Necesită administrator.", "Administrator rights required."), "error"); break; }
                    _engine.SetPanic(B("on"));
                    break;
                case "killConn":
                    Toast(_engine.KillConn(S("id")) ? L.T("Conexiunea a fost închisă.", "Connection closed.") : L.T("Nu am putut închide conexiunea (doar TCP IPv4).", "Could not close the connection (TCP IPv4 only)."), "info");
                    break;
                case "reveal":
                    var p = S("path");
                    if (File.Exists(p)) Process.Start("explorer.exe", $"/select,\"{p}\"");
                    break;
                case "geoUpdate":
                    Task.Run(async () => { await _engine.Geo.InitAsync(true); Toast(_engine.Geo.Status); });
                    break;
                case "enableFirewall":
                    Task.Run(() =>
                    {
                        try { _engine.Fw.EnableAllProfiles(); _engine.RefreshBlockStatus(true); Toast(L.T("Windows Firewall a fost pornit.", "Windows Firewall turned on."), "ok"); }
                        catch (Exception ex) { Toast(L.T("Eroare: ", "Error: ") + ex.Message, "error"); }
                    });
                    break;
                case "appList":
                    Send(new { type = "appList", apps = _engine.AppList() });
                    break;
                case "browseApp":
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("Aplicații (*.exe)|*.exe|Toate fișierele|*.*", "Applications (*.exe)|*.exe|All files|*.*"), Title = L.T("Alege aplicația", "Choose the application") };
                    if (dlg.ShowDialog(this) == true)
                    {
                        string name = Path.GetFileName(dlg.FileName);
                        try { var vi = FileVersionInfo.GetVersionInfo(dlg.FileName); if (!string.IsNullOrWhiteSpace(vi.FileDescription)) name = vi.FileDescription; } catch { }
                        Send(new { type = "browsedApp", path = dlg.FileName, name });
                    }
                    break;
                case "blockEngine":
                    var kind = S("engine");
                    Task.Run(() => Toast(_engine.SwitchBlockEngine(kind), "info"));
                    break;
                case "hideToTray":
                    HideToTray();
                    break;
                case "exit":
                    ((App)Application.Current).ExitApp();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.W($"Cmd {cmd}: {ex}");
            Toast(L.T("Eroare: ", "Error: ") + ex.Message, "error");
        }
    }

    public void HideToTray()
    {
        Hide();
        WindowState = _lastState;
        UpdateActive();
        ((App)Application.Current).TrayHint();
    }

    public void ForceClose() { _forceClose = true; try { Close(); } catch { } }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose && _engine.Settings.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnClosing(e);
        if (!_forceClose)
        {
            _forceClose = true;
            Dispatcher.BeginInvoke(() => ((App)Application.Current).ExitApp());
        }
    }
}
