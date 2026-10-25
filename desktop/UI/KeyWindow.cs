using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI;

internal enum KeyChange { Saved, Removed }

/// <summary>
/// "Connect to NVIDIA": the installed app's setup for the user's own API key. Unlike the overlay windows this is a
/// normal window, because it needs the keyboard. The local server tries the key with NVIDIA first; then it's saved
/// encrypted (DPAPI) and the server restarts with it.
/// </summary>
internal sealed class KeyWindow : Window
{
    readonly ApiClient api;
    readonly KeyStore keys;
    readonly Action<KeyChange, string?> onChanged;
    readonly Action onNotNow;
    readonly PasswordBox box;
    readonly TextBlock status;
    readonly Border save;
    readonly DockPanel bar;
    string? unverified;
    bool busy;
    CancellationTokenSource? cts;

    public KeyWindow(ApiClient api, KeyStore keys, Action<KeyChange, string?> onChanged, Action onNotNow)
    {
        this.api = api;
        this.keys = keys;
        this.onChanged = onChanged;
        this.onNotNow = onNotNow;
        Title = "Connect NimbleWrite to NVIDIA";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = true;
        Background = Theme.Surface;
        Foreground = Theme.Text;
        FontFamily = UiKit.Font;
        UseLayoutRounding = true;
        TrySetIcon();

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        head.Children.Add(UiKit.Logo(36));
        var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(UiKit.Label("Connect to NVIDIA", 18, FontWeights.SemiBold));
        titles.Children.Add(UiKit.Label("Turn on AI suggestions, rewrites and tone checks", 12.5, null, Theme.Muted));
        head.Children.Add(titles);
        root.Children.Add(head);

        root.Children.Add(UiKit.Label("NimbleWrite's AI features run on NVIDIA's hosted models, using your own NVIDIA API key.", 13, null, null, wrap: true));
        root.Children.Add(Step("1", StepOne()));
        root.Children.Add(Step("2", UiKit.Label("Paste it here:", 13)));

        box = new PasswordBox
        {
            Height = 34,
            FontSize = 13.5,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 8, 0, 0),
            Background = Theme.Raised,
            Foreground = Theme.Text,
            BorderBrush = Theme.Line,
            CaretBrush = Theme.Text,
            ToolTip = "Your NVIDIA API key (starts with nvapi-)",
        };
        AutomationName(box, "NVIDIA API key");
        box.PasswordChanged += (_, _) =>
        {
            if (busy) return;
            unverified = null;
            SetSaveText("Save");
            Status("", Theme.Muted);
        };
        root.Children.Add(box);

        status = UiKit.Label(keys.HasKey ? "A key is already saved. Paste a new one to replace it." : "", 12.5, null, Theme.Muted, wrap: true);
        status.Margin = new Thickness(0, 8, 0, 0);
        // Screen readers announce each new status ("Checking…", "NVIDIA didn't accept that key…") as it appears.
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        root.Children.Add(status);

        var note = UiKit.Label(
            "Your key is stored encrypted for your Windows account and is only ever sent to NVIDIA. Without a key, NimbleWrite still checks spelling and grammar with its built-in rules.",
            11.5, null, Theme.Muted, wrap: true);
        note.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(note);

        bar = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
        save = UiKit.Button("Save", () => _ = SaveAsync(), Kind.Primary);
        DockPanel.SetDock(save, Dock.Right);
        bar.Children.Add(save);
        var later = UiKit.Button(keys.HasKey ? "Cancel" : "Not now", NotNow, Kind.Secondary);
        later.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(later, Dock.Right);
        bar.Children.Add(later);
        if (keys.HasKey)
        {
            var remove = UiKit.Button("Remove saved key", Remove, Kind.Ghost, "Turn AI features off and delete the key from this PC");
            DockPanel.SetDock(remove, Dock.Left);
            bar.Children.Add(remove);
        }
        root.Children.Add(bar);
        Content = root;

        SourceInitialized += (_, _) => DarkTitleBar();
        // On top once so it isn't lost behind the app you were typing in; after that it behaves like any window.
        ContentRendered += (_, _) =>
        {
            Topmost = false;
            box.Focus();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = SaveAsync();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                NotNow();
            }
        };
        Closed += (_, _) => cts?.Cancel();
    }

    TextBlock StepOne()
    {
        var t = new TextBlock { FontFamily = UiKit.Font, FontSize = 13, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap };
        t.Inlines.Add(new Run("Sign in at "));
        var link = new Hyperlink(new Run("build.nvidia.com")) { NavigateUri = new Uri("https://build.nvidia.com/"), Foreground = Theme.BrandText };
        link.RequestNavigate += (_, e) =>
        {
            Open(e.Uri.AbsoluteUri);
            e.Handled = true;
        };
        t.Inlines.Add(link);
        t.Inlines.Add(new Run(" and create an API key. It starts with nvapi-."));
        return t;
    }

    static Grid Step(string n, UIElement content)
    {
        var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(UiKit.Label(n + ".", 13, FontWeights.SemiBold, Theme.BrandText));
        Grid.SetColumn(content, 1);
        g.Children.Add(content);
        return g;
    }

    async Task SaveAsync()
    {
        if (busy) return;
        string? key = KeyStore.Normalize(box.Password);
        if (key == null)
        {
            Status("That doesn't look like an NVIDIA API key. Keys from build.nvidia.com start with nvapi-.", ErrorBrush);
            return;
        }
        if (unverified != null && key == unverified)
        {
            // Second click after "couldn't check it": the user wants it saved anyway.
            Commit(key, "Your key is saved. AI suggestions start as soon as NVIDIA can be reached.");
            return;
        }
        SetBusy(true);
        Status("Checking your key with NVIDIA…", Theme.Muted);
        cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        KeyTestResult result;
        try
        {
            result = await api.TestKeyAsync(key, cts.Token);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ApiException or JsonException or NotSupportedException)
        {
            result = new KeyTestResult { Reason = "network", Message = "NimbleWrite's checker isn't running, so the key couldn't be tested." };
        }
        finally
        {
            SetBusy(false);
        }
        if (!IsLoaded) return; // closed while checking
        if (result.Ok)
        {
            Commit(key, result.Message);
            return;
        }
        if (result.Reason is "invalid" or "rejected")
        {
            Status(result.Message ?? "NVIDIA didn't accept that key.", ErrorBrush);
            box.Focus();
            box.SelectAll();
            return;
        }
        unverified = key;
        Status((result.Message ?? "The key couldn't be checked right now.") + " You can save it anyway.", WarnBrush);
        SetSaveText("Save anyway");
    }

    void Commit(string key, string? caveat)
    {
        try
        {
            keys.Save(key);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            Log.Error("save NVIDIA key", e);
            Status("Couldn't save the key on this PC: " + e.Message, ErrorBrush);
            return;
        }
        box.Clear();
        Close();
        onChanged(KeyChange.Saved, caveat);
    }

    void Remove()
    {
        if (busy) return;
        keys.Delete();
        Close();
        onChanged(KeyChange.Removed, null);
    }

    void NotNow()
    {
        if (busy) cts?.Cancel();
        Close();
        onNotNow();
    }

    void SetBusy(bool on)
    {
        busy = on;
        box.IsEnabled = !on;
        bar.IsEnabled = !on;
        bar.Opacity = on ? 0.6 : 1;
    }

    void SetSaveText(string text) => ((TextBlock)save.Child).Text = text;

    void Status(string text, Brush brush)
    {
        status.Text = text;
        status.Foreground = brush;
        if (text.Length > 0)
        {
            (System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(status)
             ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(status))
                ?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
    }

    static Brush ErrorBrush => Theme.Dark ? Theme.B("#FF6B6F") : Theme.B("#C62D32");

    static Brush WarnBrush => Theme.Dark ? Theme.B("#F5B451") : Theme.B("#9A5B00");

    static void AutomationName(DependencyObject e, string name) => System.Windows.Automation.AutomationProperties.SetName(e, name);

    void TrySetIcon()
    {
        try
        {
            string ico = Path.Combine(AppContext.BaseDirectory, "nimblewrite.ico");
            if (File.Exists(ico)) Icon = BitmapFrame.Create(new Uri(ico));
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException or FileFormatException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    void DarkTitleBar()
    {
        if (!Theme.Dark) return;
        int on = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Error("open url", e);
        }
    }
}
