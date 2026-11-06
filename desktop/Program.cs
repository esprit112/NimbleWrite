using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NimbleWrite.Desktop.Core;
using NimbleWrite.Desktop.UI;

namespace NimbleWrite.Desktop;

internal static class Program
{
    const string MutexName = @"Local\NimbleWrite.Desktop";
    const string ShowSignal = @"Local\NimbleWrite.Desktop.Show";
    const string EditorSignal = @"Local\NimbleWrite.Desktop.Editor";
    const string QuitSignal = @"Local\NimbleWrite.Desktop.Quit";
    const string SettingsSignal = @"Local\NimbleWrite.Desktop.Settings";

    /// <summary>
    /// NimbleWrite.exe            start (or, if running, say so)
    /// NimbleWrite.exe --editor   open the web editor (starting NimbleWrite first if needed)
    /// NimbleWrite.exe --settings open the Settings window (starting NimbleWrite first if needed)
    /// NimbleWrite.exe --quit     ask the running copy to quit and wait until it has (used by the installer)
    /// NimbleWrite.exe --start-with-windows on|off   set "start with Windows" for this user and exit (used by the installer)
    /// NimbleWrite.exe --diag     write a live state snapshot (no text) for troubleshooting
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        int startup = Array.FindIndex(args, a => a.Equals("--start-with-windows", StringComparison.OrdinalIgnoreCase));
        if (startup >= 0) return SetStartup(startup + 1 < args.Length ? args[startup + 1] : "");
        bool quit = Has("--quit"), editor = Has("--editor"), settings = Has("--settings");
        using var mutex = new Mutex(true, MutexName, out bool first);
        // "Restart now" in Settings starts the new copy before the old one has quit: wait for it to finish.
        if (!first && Has(DesktopApp.RestartedFlag)) first = WaitUntilGone(mutex) == 0;
        if (!first)
        {
            Signal(quit ? QuitSignal : editor ? EditorSignal : settings ? SettingsSignal : ShowSignal);
            if (!quit) return 0;
            int gone = WaitUntilGone(mutex);
            if (gone == 0) mutex.ReleaseMutex();
            return gone;
        }
        if (quit) return 0; // nothing was running
        bool diag = Has("--diag") || Environment.GetEnvironmentVariable("NIMBLEWRITE_DIAG") == "1";
        var app = new DesktopApp(diag, editor, settings, ShowSignal, EditorSignal, QuitSignal, SettingsSignal);
        int code = app.Run();
        mutex.ReleaseMutex();
        return code;
    }

    static void Signal(string name)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(name);
            ev.Set();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// The installer's "Start NimbleWrite when I sign in" option. Setup runs this as the person who ran Setup, even
    /// when it installs for all users as an administrator, so it lands in their own settings. "off" only removes an
    /// entry that starts this copy.
    /// </summary>
    static int SetStartup(string value)
    {
        bool on = value.Equals("on", StringComparison.OrdinalIgnoreCase);
        if (!on && !value.Equals("off", StringComparison.OrdinalIgnoreCase)) return 2;
        try
        {
            if (on) DesktopConfig.SetStartWithWindows(true);
            else if (DesktopConfig.StartupRunsThisCopy) DesktopConfig.SetStartWithWindows(false);
            Log.Info($"start with Windows: {(on ? "on" : "off")} (installer)");
            return 0;
        }
        catch (Exception e)
        {
            Log.Error("start with Windows", e);
            return 1;
        }
    }

    /// <summary>
    /// 0 once the running copy has exited (its mutex is free), 1 if it's still running after 15 s. On 0 this thread
    /// owns the mutex: --quit releases it straight away; a restarted copy keeps it.
    /// </summary>
    static int WaitUntilGone(Mutex mutex)
    {
        bool gone;
        try
        {
            gone = mutex.WaitOne(TimeSpan.FromSeconds(15));
        }
        catch (AbandonedMutexException)
        {
            gone = true;
        }
        return gone ? 0 : 1;
    }
}

/// <summary>Tray app: no main window, just the engine, the overlay windows, the tray icon and the hotkey.</summary>
internal sealed class DesktopApp : Application, ISettingsHost
{
    /// <summary>Passed to the new copy by "Restart now", so it waits for this one to quit instead of saying it's already running.</summary>
    internal const string RestartedFlag = "--restarted";

    readonly bool diag;
    readonly bool openEditorAtStart;
    readonly bool openSettingsAtStart;
    readonly string showSignal;
    readonly string editorSignal;
    readonly string quitSignal;
    readonly string settingsSignal;
    readonly KeyStore keys = new();
    DesktopConfig config = null!;
    ApiClient api = null!;
    Engine engine = null!;
    UiController ui = null!;
    Tray tray = null!;
    HotkeyListener? hotkey;
    FocusWatcher? focusWatcher;
    KeyWindow? keyWindow;
    SettingsWindow? settingsWindow;
    AboutWindow? aboutWindow;
    bool quitting;

    public DesktopApp(bool diag, bool openEditor, bool openSettings, string showSignal, string editorSignal, string quitSignal, string settingsSignal)
    {
        this.diag = diag;
        openEditorAtStart = openEditor;
        openSettingsAtStart = openSettings;
        this.showSignal = showSignal;
        this.editorSignal = editorSignal;
        this.quitSignal = quitSignal;
        this.settingsSignal = settingsSignal;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("UI", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("fatal", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("task", args.Exception);
            args.SetObserved();
        };

        config = DesktopConfig.Load();
        Theme.Load(config.Theme);
        ServerLauncher.Configure(config.ServerUrl, keys.Load);
        api = new ApiClient(config.ServerUrl);
        ui = new UiController(config);
        var d = Dispatcher;
        engine = new Engine(config, api,
            s => d.InvokeAsync(() => ui.OnState(s)),
            g => d.InvokeAsync(() => ui.OnGeometry(g)),
            n => d.InvokeAsync(() => ui.OnNotice(n)),
            new ClipboardBridge(d),
            diag);
        tray = new Tray(config, engine, api.BaseUrl, Quit, ServerLauncher.Packaged ? ShowKeyWindow : null, () => keys.HasKey, () => hotkey?.Registered == true,
            () => ShowSettings(), ShowAbout);
        ui.Bind(engine, tray, api);
        ui.ShowSettings = () => ShowSettings("writing");
        hotkey = NewHotkey(config.Hotkey);
        if (!hotkey.Registered)
        {
            Log.Info(HotkeyListener.TryParse(config.Hotkey, out _, out _)
                ? $"shortcut {config.Hotkey} is taken by another app"
                : $"shortcut \"{config.Hotkey}\" in desktop.json isn't valid");
        }
        engine.Start();
        focusWatcher = new FocusWatcher(engine.Wake);
        ListenForSignals();
        if (openSettingsAtStart) ShowSettings();
        _ = ConnectAsync();
        Log.Info($"NimbleWrite desktop started{(ServerLauncher.Packaged ? " (installed copy)" : "")}{(diag ? " (diagnostics on)" : "")}");
    }

    async Task ConnectAsync()
    {
        var health = await WaitForServerAsync();
        if (health == null)
        {
            if (ServerLauncher.Packaged) tray.Balloon("NimbleWrite couldn't start its checker", @"Quit and reopen NimbleWrite. If it keeps happening, see %LOCALAPPDATA%\NimbleWrite\server.log.");
            else tray.Balloon("NimbleWrite can't reach its server", "Run npm start in the NimbleWrite folder, then try again.");
            return;
        }
        if (openEditorAtStart) OpenEditor();
        if (!config.Welcomed)
        {
            config.Welcomed = true;
            config.Save();
            string assistant = hotkey?.Registered == true ? $"{config.Hotkey} opens the assistant." : "Click the N badge on a text box to open the assistant.";
            tray.Balloon("NimbleWrite is on", $"Write in any app: suggestions appear as you type. {assistant}");
        }
        if (ServerLauncher.Packaged && !health.NimConfigured && !keys.HasKey && !config.KeyPromptDismissed) ShowKeyWindow();
    }

    /// <summary>The server's status, starting it first if it isn't running (waits up to 20 s). Null if it never answers.</summary>
    async Task<HealthInfo?> WaitForServerAsync()
    {
        var h = await api.HealthInfoAsync(CancellationToken.None);
        if (h == null && (ServerLauncher.TryStart() || ServerLauncher.OwnsServer))
        {
            for (int i = 0; i < 40 && h == null; i++)
            {
                await Task.Delay(500);
                h = await api.HealthInfoAsync(CancellationToken.None);
            }
        }
        return h;
    }

    HotkeyListener NewHotkey(string spec) => new(spec, () =>
    {
        engine.ShortcutPressed(); // first, so the engine is ready for the app's reaction to the shortcut's Alt
        ui.TogglePanel();
    });

    void ShowSettings(string? section = null)
    {
        if (quitting) return;
        if (settingsWindow == null)
        {
            settingsWindow = new SettingsWindow(this, section);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
        }
        else if (section != null) settingsWindow.ShowSection(section);
        settingsWindow.ShowInFront();
    }

    void ShowAbout()
    {
        if (quitting) return;
        if (aboutWindow == null)
        {
            aboutWindow = new AboutWindow(this);
            aboutWindow.Closed += (_, _) => aboutWindow = null;
        }
        aboutWindow.ShowInFront();
    }

    // ---- ISettingsHost: what the Settings and About windows change ----

    DesktopConfig ISettingsHost.Config => config;
    ApiClient ISettingsHost.Api => api;
    string ISettingsHost.EditorUrl => api.BaseUrl;
    bool ISettingsHost.Packaged => ServerLauncher.Packaged;
    bool ISettingsHost.HasKey => keys.HasKey;
    bool ISettingsHost.ShortcutRegistered => hotkey?.Registered == true;
    string? ISettingsHost.LastApp => engine.LastProcess;
    string? ISettingsHost.LastAppName => engine.LastAppName;

    bool ISettingsHost.StartWithWindows
    {
        get => DesktopConfig.StartsWithWindows;
        set => DesktopConfig.SetStartWithWindows(value);
    }

    void ISettingsHost.ConfigChanged()
    {
        config.Save();
        tray.Refresh();
    }

    void ISettingsHost.SetAppDisabled(string process, bool off) => config.SetAppDisabled(process, off);

    void ISettingsHost.ResetApps() => config.ResetDisabledApps();

    /// <summary>Take the new shortcut only if Windows gives it to us; until then the old one keeps working.</summary>
    bool ISettingsHost.TryShortcut(string spec)
    {
        var next = NewHotkey(spec);
        if (!next.Registered)
        {
            next.Dispose();
            Log.Info($"shortcut {spec} is taken by another app; keeping {config.Hotkey}");
            return false;
        }
        hotkey?.Dispose();
        hotkey = next;
        config.Hotkey = spec;
        config.Save();
        Log.Info($"shortcut changed to {spec}");
        return true;
    }

    void ISettingsHost.ShowKeyWindow()
    {
        if (ServerLauncher.Packaged) ShowKeyWindow();
    }

    void ISettingsHost.OpenUrl(string url) => AppWindow.Open(url);

    /// <summary>Start a new copy (which waits until this one has gone), then quit.</summary>
    void ISettingsHost.Restart()
    {
        if (Environment.ProcessPath is not { } exe) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe, RestartedFlag) { UseShellExecute = false });
        }
        catch (Exception e)
        {
            Log.Error("restart", e);
            tray.Balloon("NimbleWrite couldn't restart", "Quit NimbleWrite from the tray icon and open it again.");
            return;
        }
        Log.Info("restarting");
        Quit();
    }

    void ShowKeyWindow()
    {
        if (quitting) return;
        if (keyWindow != null)
        {
            keyWindow.Activate();
            return;
        }
        keyWindow = new KeyWindow(api, keys, (change, caveat) => _ = ApplyKeyAsync(change, caveat), () =>
        {
            if (keys.HasKey || config.KeyPromptDismissed) return;
            config.KeyPromptDismissed = true;
            config.Save();
            tray.Balloon("NimbleWrite is using its built-in checks", "Connect to NVIDIA from the tray icon whenever you want AI suggestions.");
        });
        keyWindow.Closed += (_, _) => keyWindow = null;
        keyWindow.Show();
        keyWindow.Activate();
    }

    /// <summary>A key was saved or removed: restart the server this app runs so it picks the change up.</summary>
    async Task ApplyKeyAsync(KeyChange change, string? caveat)
    {
        Log.Info(change == KeyChange.Saved ? "NVIDIA key saved" : "NVIDIA key removed");
        if (!ServerLauncher.OwnsServer && await api.HealthAsync(CancellationToken.None))
        {
            // Another NimbleWrite server holds the port (e.g. a development copy), so this app can't restart it.
            tray.Balloon(change == KeyChange.Saved ? "Key saved" : "Key removed", "Quit and reopen NimbleWrite to apply the change.");
            return;
        }
        await Task.Run(ServerLauncher.Restart);
        HealthInfo? h = null;
        for (int i = 0; i < 40 && h == null; i++)
        {
            await Task.Delay(500);
            h = await api.HealthInfoAsync(CancellationToken.None);
        }
        engine.ResetChecks();
        if (h == null)
        {
            tray.Balloon("NimbleWrite couldn't restart its checker", "Quit and reopen NimbleWrite.");
            return;
        }
        if (change == KeyChange.Removed) tray.Balloon("NVIDIA key removed", "NimbleWrite now uses its built-in spelling and grammar checks only.");
        else tray.Balloon("NimbleWrite is connected to NVIDIA", caveat ?? "AI suggestions, rewrites and tone checks are on.");
    }

    void OpenEditor()
    {
        try
        {
            Process.Start(new ProcessStartInfo(api.BaseUrl) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Error("open editor", e);
        }
    }

    async Task OpenEditorWhenReadyAsync()
    {
        if (await WaitForServerAsync() != null) OpenEditor();
    }

    void ListenForSignals()
    {
        var handles = new WaitHandle[]
        {
            new EventWaitHandle(false, EventResetMode.AutoReset, showSignal),
            new EventWaitHandle(false, EventResetMode.AutoReset, editorSignal),
            new EventWaitHandle(false, EventResetMode.AutoReset, quitSignal),
            new EventWaitHandle(false, EventResetMode.AutoReset, settingsSignal),
        };
        var t = new Thread(() =>
        {
            while (true)
            {
                switch (WaitHandle.WaitAny(handles))
                {
                    case 0:
                        Dispatcher.InvokeAsync(() => tray.Balloon("NimbleWrite is already running", "Look for the N icon in the notification area."));
                        break;
                    case 1:
                        Dispatcher.InvokeAsync(() => _ = OpenEditorWhenReadyAsync());
                        break;
                    case 3:
                        Dispatcher.InvokeAsync(() => ShowSettings());
                        break;
                    default:
                        Dispatcher.InvokeAsync(Quit);
                        return;
                }
            }
        }) { IsBackground = true, Name = "NimbleWrite signal listener" };
        t.Start();
    }

    void Quit()
    {
        if (quitting) return;
        quitting = true;
        Log.Info("quitting");
        keyWindow?.Close();
        settingsWindow?.Close();
        aboutWindow?.Close();
        focusWatcher?.Dispose();
        engine.Dispose();
        hotkey?.Dispose();
        tray.Dispose();
        api.Dispose();
        ServerLauncher.StopIfStarted();
        Shutdown();
    }
}
