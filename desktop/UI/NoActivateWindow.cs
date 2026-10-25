using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI;

/// <summary>
/// A topmost window that never takes focus, so the app you're typing in keeps the keyboard while you click our
/// buttons. Positioned in physical pixels (the coordinates UI Automation gives us).
/// </summary>
internal class NoActivateWindow : Window
{
    readonly bool clickThrough;
    bool placedOnce;

    public NoActivateWindow(bool clickThrough)
    {
        this.clickThrough = clickThrough;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        FontFamily = UiKit.Font;
        UseLayoutRounding = true;
        SourceInitialized += OnSourceInitialized;
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        ex |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
        if (clickThrough) ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
    }

    static IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg != Native.WM_MOUSEACTIVATE) return IntPtr.Zero;
        handled = true;
        return new IntPtr(Native.MA_NOACTIVATE);
    }

    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    public void MoveTo(double x, double y) =>
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, (int)Math.Round(x), (int)Math.Round(y), 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    /// <summary>Move and size in physical pixels. Keeps its place in the z-order after the first time, so cards stay on top.</summary>
    public void Place(Rect r)
    {
        uint flags = Native.SWP_NOACTIVATE | (placedOnce ? Native.SWP_NOZORDER : 0);
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, (int)Math.Floor(r.X), (int)Math.Floor(r.Y), (int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height), flags);
        placedOnce = true;
    }

    public void BringToFront() => Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    public bool ContainsScreenPoint(Point p)
    {
        if (!IsVisible) return false;
        var r = Native.WindowRect(Handle);
        return p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
    }

    /// <summary>Show without a flash at the default position: invisible until placed.</summary>
    protected void ShowHidden()
    {
        if (IsVisible) return;
        Opacity = 0;
        Show();
    }

    /// <summary>
    /// Position a card-like window (with a shadow margin) next to an anchor rectangle: below it, or above if there's no
    /// room, clamped to the work area. All maths in physical pixels.
    /// </summary>
    protected void PlaceNear(Rect anchor, bool alignRight = false)
    {
        UpdateLayout();
        double s = Native.ScaleAt(anchor.Left + 1, anchor.Bottom - 1);
        double m = UiKit.ShadowMargin * s, w = ActualWidth * s, h = ActualHeight * s, gap = 4 * s;
        var wa = Native.WorkAreaAt(anchor.Left + 1, anchor.Bottom - 1);
        double x = alignRight ? anchor.Right - w + m : anchor.Left - m;
        double y = anchor.Bottom + gap - m;
        if (y + h - m > wa.Bottom) y = anchor.Top - gap - h + m;
        x = Math.Max(wa.Left - m, Math.Min(x, wa.Right - w + m));
        y = Math.Max(wa.Top - m, y);
        MoveTo(x, y);
        Opacity = 1;
    }
}
