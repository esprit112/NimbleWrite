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

    /// <summary>
    /// NimbleWrite.exe            start (or, if running, say so)
    /// NimbleWrite.exe --editor   open the web editor (starting NimbleWrite first if needed)
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
        bool quit = Has("--quit"), editor = Has("--editor");
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            Signal(quit ? QuitSignal : editor ? EditorSignal : ShowSignal);
            return quit ? WaitUntilGone(mutex) : 0;
        }
        if (quit) return 0; // nothing was running
        bool diag = Has("--diag") || Environment.GetEnvironmentVariable("NIMBLEWRITE_DIAG") == "1";
        var app = new DesktopApp(diag, editor, ShowSignal, EditorSignal, QuitSignal);
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

    /// <summary>--quit: 0 once the running copy has exited (its mutex is free), 1 if it's still running after 15 s.</summary>
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
        if (!gone) return 1;
        mutex.ReleaseMutex();
        return 0;
    }
}

/// <summary>Tray app: no main window, just the engine, the overlay windows, the tray icon and the hotkey.</summary>
internal sealed class DesktopApp : Application
{
    readonly bool diag;
    readonly bool openEditorAtStart;
    readonly string showSignal;
    readonly string editorSignal;
    readonly string quitSignal;
    readonly KeyStore keys = new();
    DesktopConfig config = null!;
    ApiClient api = null!;
    Engine engine = null!;
    UiController ui = null!;
    Tray tray = null!;
    HotkeyListener? hotkey;
    KeyWindow? keyWindow;
    bool quitting;

    public DesktopApp(bool diag, bool openEditor, string showSignal, string editorSignal, string quitSignal)
    {
        this.diag = diag;
        openEditorAtStart = openEditor;
        this.showSignal = showSignal;
        this.editorSignal = editorSignal;
        this.quitSignal = quitSignal;
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

        Theme.Load();
        config = DesktopConfig.Load();
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
        tray = new Tray(config, engine, api.BaseUrl, Quit, ServerLauncher.Packaged ? ShowKeyWindow : null, () => keys.HasKey);
        ui.Bind(engine, tray);
        hotkey = new HotkeyListener(config.Hotkey, () =>
        {
            engine.ShortcutPressed(); // first, so the engine is ready for the app's reaction to the shortcut's Alt
            ui.TogglePanel();
        });
        if (!hotkey.Registered) Log.Info($"shortcut {config.Hotkey} is taken by another app");
        engine.Start();
        ListenForSignals();
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
            tray.Balloon("NimbleWrite is on", $"Write in any app: suggestions appear as you type. {config.Hotkey} opens the assistant.");
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
        engine.Dispose();
        hotkey?.Dispose();
        tray.Dispose();
        api.Dispose();
        ServerLauncher.StopIfStarted();
        Shutdown();
    }
}
