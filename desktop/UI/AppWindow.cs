using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI;

/// <summary>
/// A normal window that takes the keyboard (unlike the overlay, badge, card and panel): "Connect to NVIDIA",
/// Settings and About. Shares the app icon, the theme's colours and a dark title bar in dark mode.
/// </summary>
internal abstract class AppWindow : Window
{
    protected AppWindow()
    {
        Background = Theme.Surface;
        Foreground = Theme.Text;
        FontFamily = UiKit.Font;
        UseLayoutRounding = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TrySetIcon();
        SourceInitialized += (_, _) => DarkTitleBar();
    }

    /// <summary>Show and bring to the front, on top once so it isn't lost behind the app you were typing in.</summary>
    public void ShowInFront()
    {
        Topmost = true;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = false;
    }

    protected static Brush ErrorBrush => Theme.Dark ? Theme.B("#FF6B6F") : Theme.B("#C62D32");

    protected static Brush WarnBrush => Theme.Dark ? Theme.B("#F5B451") : Theme.B("#9A5B00");

    protected static Brush OkBrush => Theme.Dark ? Theme.B("#5FD394") : Theme.B("#0B6B3A");

    protected static void AutomationName(DependencyObject e, string name) => System.Windows.Automation.AutomationProperties.SetName(e, name);

    /// <summary>Show <paramref name="text"/> in a status line and have screen readers announce it.</summary>
    protected static void Announce(TextBlock status, string text, Brush brush)
    {
        status.Text = text;
        status.Foreground = brush;
        if (text.Length == 0) return;
        (System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(status)
         ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(status))
            ?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    /// <summary>A status line that screen readers read out when it changes.</summary>
    protected static TextBlock StatusLine(double top = 8)
    {
        var t = UiKit.Label("", 12.5, null, Theme.Muted, wrap: true);
        t.Margin = new Thickness(0, top, 0, 0);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(t, System.Windows.Automation.AutomationLiveSetting.Polite);
        return t;
    }

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

    /// <summary>Open a web page, a file or a folder with its usual app.</summary>
    internal static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Error("open", e);
        }
    }
}
