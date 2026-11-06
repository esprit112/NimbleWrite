using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI;

/// <summary>
/// About NimbleWrite: versions (this app's and its checker's), which copy this is, the checker's status, and
/// buttons for the logs, diagnostics (no text, no key), the licence, the GitHub page and a manual update check.
/// </summary>
internal sealed class AboutWindow : AppWindow
{
    readonly ISettingsHost host;
    readonly Grid facts = new();
    readonly TextBlock status;
    HealthInfo? health;
    CancellationTokenSource? cts;

    public AboutWindow(ISettingsHost host)
    {
        this.host = host;
        Title = "About NimbleWrite";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        head.Children.Add(UiKit.Logo(48));
        var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(UiKit.Label("NimbleWrite", 22, FontWeights.SemiBold));
        titles.Children.Add(UiKit.Label($"Version {Shown(UpdateCheck.DesktopVersion)} \u00B7 writing help in every app", 12.5, null, Theme.Muted));
        head.Children.Add(titles);
        root.Children.Add(head);

        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new Border
        {
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Child = facts,
        });
        RenderFacts();

        status = StatusLine(10);
        root.Children.Add(status);

        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(UiKit.Spaced(UiKit.Button("Check for updates", () => _ = CheckForUpdatesAsync(), Kind.Primary, "Asks GitHub for the latest release. Nothing else is sent."), 8, 8));
        buttons.Children.Add(UiKit.Spaced(UiKit.Button("Copy diagnostics", CopyDiagnostics, Kind.Secondary, "Versions and settings for a bug report. No text from your apps, and never your key."), 8, 8));
        buttons.Children.Add(UiKit.Spaced(UiKit.Button("Open logs folder", () => Open(Log.Dir), Kind.Secondary), 8, 8));
        buttons.Children.Add(UiKit.Spaced(UiKit.Button("Licences", OpenLicence, Kind.Ghost, "NimbleWrite's licence and the notices for the components it includes"), 8, 8));
        buttons.Children.Add(UiKit.Spaced(UiKit.Button("GitHub page", () => host.OpenUrl(UpdateCheck.ProjectPage), Kind.Ghost), 8, 8));
        root.Children.Add(buttons);
        Content = new Border { Background = Theme.Surface, Padding = new Thickness(24, 20, 24, 18), Child = root };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
        Closed += (_, _) => cts?.Cancel();
        Loading = LoadAsync();
    }

    /// <summary>The checker's status request.</summary>
    internal Task Loading { get; }

    internal string ServerVersionText { get; private set; } = "";

    async Task LoadAsync()
    {
        health = await host.Api.HealthInfoAsync(CancellationToken.None);
        RenderFacts();
    }

    void RenderFacts()
    {
        facts.Children.Clear();
        facts.RowDefinitions.Clear();
        string desktop = Shown(UpdateCheck.DesktopVersion);
        var h = health;
        bool loading = Loading == null || !Loading.IsCompleted;
        string server = h == null ? (loading ? "\u2026" : "not running") : Shown(h.Version ?? "");
        ServerVersionText = server;
        // An app and checker from different releases (e.g. an old checker still running after an update) misbehave in odd ways.
        bool differs = h?.Version is { Length: > 0 } v && UpdateCheck.DesktopVersion.Length > 0 && !string.Equals(v, UpdateCheck.DesktopVersion, StringComparison.OrdinalIgnoreCase);
        if (differs) server += " (different from the app: quit and reopen NimbleWrite)";

        Row("Desktop app", desktop);
        Row("Checker", server, differs ? WarnBrush : null);
        Row("Copy", host.Packaged ? "Installed" : "Development copy (run from the source folder)");
        Row("Runs on", $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}");
        Row("Address", $"127.0.0.1:{SettingsWindow.PortOf(host.Config.ServerUrl)} (this PC only)");
        if (h != null)
        {
            Row("NVIDIA", h.NimConfigured ? "Connected" : "Not connected: built-in checks only", h.NimConfigured ? OkBrush : Theme.Text);
            Row("Web search", h.SearchConfigured ? "Set up" : "Not set up");
            if (h.Models != null) Row("Models", string.Join("\n", new[] { h.Models.Check, h.Models.Write, h.Models.Translate }.Where(m => m.Length > 0).Distinct()));
        }
    }

    void Row(string label, string value, Brush? brush = null)
    {
        int r = facts.RowDefinitions.Count;
        facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = UiKit.Label(label, 12.5, null, Theme.Muted);
        l.Margin = new Thickness(0, 3, 8, 3);
        Grid.SetRow(l, r);
        facts.Children.Add(l);
        var v = UiKit.Label(value, 12.5, null, brush ?? Theme.Text, wrap: true);
        v.Margin = new Thickness(0, 3, 0, 3);
        Grid.SetRow(v, r);
        Grid.SetColumn(v, 1);
        facts.Children.Add(v);
    }

    static string Shown(string version) => version.Length == 0 ? "unknown" : version;

    async Task CheckForUpdatesAsync()
    {
        cts?.Cancel();
        cts = new CancellationTokenSource();
        Announce(status, "Asking GitHub for the latest release\u2026", Theme.Muted);
        try
        {
            var r = await UpdateCheck.CheckAsync(UpdateCheck.DesktopVersion, null, cts.Token);
            Announce(status, r.Message, r.Newer ? OkBrush : Theme.Muted);
            if (r.Newer) host.OpenUrl(r.Url);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Announce(status, "Couldn't reach GitHub. Check your internet connection and try again.", WarnBrush);
        }
    }

    void CopyDiagnostics()
    {
        string text = Diagnostics(UpdateCheck.DesktopVersion, health, host.Config, host.Packaged, host.HasKey, host.ShortcutRegistered, SafeStartWithWindows());
        try
        {
            Clipboard.SetText(text);
            Announce(status, "Diagnostics copied. Paste them into your bug report.", OkBrush);
        }
        catch (Exception)
        {
            Announce(status, "Couldn't use the clipboard just now. Try again.", WarnBrush);
        }
    }

    bool SafeStartWithWindows()
    {
        try
        {
            return host.StartWithWindows;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// What "Copy diagnostics" puts on the clipboard: versions, switches and counts. Never any text from your apps or
    /// documents, never the key, no app names (just how many are turned off) and no paths with your user name.
    /// </summary>
    internal static string Diagnostics(string desktopVersion, HealthInfo? h, DesktopConfig config, bool packaged, bool hasKey, bool shortcutRegistered, bool startWithWindows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("NimbleWrite diagnostics (no text from your apps or documents, no keys)");
        sb.AppendLine($"Desktop {Shown(desktopVersion)} ({(packaged ? "installed copy" : "development copy")})");
        sb.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}; Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        if (h == null) sb.AppendLine("Checker: not running");
        else
        {
            sb.AppendLine($"Checker {Shown(h.Version ?? "")}, port {SettingsWindow.PortOf(config.ServerUrl)}, {(h.Packaged ? "bundled" : "development server")}");
            sb.AppendLine($"NVIDIA: {(h.NimConfigured ? "connected" : "not connected")}; web search: {(h.SearchConfigured ? "set up" : "not set up")}");
            if (h.Models != null) sb.AppendLine($"Models: check={h.Models.Check}, write={h.Models.Write}, translate={h.Models.Translate}");
        }
        if (packaged) sb.AppendLine($"Own NVIDIA key saved: {(hasKey ? "yes" : "no")}");
        bool paused = DateTime.Now < config.PausedUntil;
        sb.AppendLine($"Turned on: {(config.Enabled ? "yes" : "no")}; paused: {(paused ? "yes" : "no")}; underlines: {(config.ShowUnderlines ? "on" : "off")}; start with Windows: {(startWithWindows ? "yes" : "no")}");
        sb.AppendLine($"Shortcut: {config.Hotkey} ({(shortcutRegistered ? "working" : "not registered")})");
        int defaults = config.DisabledApps.Count(a => DesktopConfig.DefaultDisabledApps.Contains(a, StringComparer.OrdinalIgnoreCase));
        sb.AppendLine($"Apps turned off: {config.DisabledApps.Count} ({defaults} from the default list)");
        sb.AppendLine($"Appearance: {config.Theme} ({(Theme.Dark ? "dark" : "light")}); panel tab: {config.LastTab}");
        sb.AppendLine($"Generated {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        return sb.ToString();
    }

    void OpenLicence()
    {
        string? file = LicenceFile(AppContext.BaseDirectory);
        if (file != null) Open(file);
        else host.OpenUrl(UpdateCheck.ProjectPage + "/blob/main/LICENSE");
    }

    /// <summary>
    /// The installed app's third-party notices, else a LICENSE beside the app, else (a development build) the one at
    /// the root of the NimbleWrite source folder above it. Never some other folder's LICENSE further up.
    /// </summary>
    internal static string? LicenceFile(string appDir)
    {
        string notices = Path.Combine(appDir, "licenses", "THIRD-PARTY-NOTICES.txt");
        if (File.Exists(notices)) return notices;
        if (File.Exists(Path.Combine(appDir, "LICENSE"))) return Path.Combine(appDir, "LICENSE");
        for (var d = new DirectoryInfo(appDir).Parent; d != null; d = d.Parent)
        {
            string licence = Path.Combine(d.FullName, "LICENSE");
            if (File.Exists(licence) && File.Exists(Path.Combine(d.FullName, "desktop", "NimbleWrite.Desktop.csproj"))) return licence;
        }
        return null;
    }

    internal BitmapSource RenderForTest()
    {
        var root = (FrameworkElement)Content;
        root.Measure(new Size(Width, double.PositiveInfinity));
        root.Arrange(new Rect(new Size(Width, root.DesiredSize.Height)));
        root.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(Width), (int)Math.Ceiling(root.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        bmp.Freeze();
        return bmp;
    }
}
