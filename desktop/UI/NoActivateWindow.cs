using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI;

/// <summary>
/// A topmost window that never takes focus, so the app you're typing in keeps the keyboard while you click our
/// buttons. Positioned in physical pixels (the coordinates UI Automation gives us).
/// </summary>
/// <remarks>
/// Typing mode: a text box in the window (an <see cref="InputBox"/>) can ask for the keyboard. The window then becomes
/// activatable and takes the foreground (allowed, because the click on it was the last input). Enter, Escape, or a
/// click anywhere outside the text box hands the keyboard straight back to <see cref="TypingReturnTo"/>, the app the
/// user was writing in, with its selection intact. Tested in Windows 11 Notepad and a Chromium textarea.
/// </remarks>
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
        // A click on a button (or anywhere but a text box) ends typing first, so the button's action, which may type
        // into the app, runs with the keyboard back where it belongs.
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (Typing && !InTextBox(e.OriginalSource)) EndTyping(restore: true);
        };
        Deactivated += (_, _) =>
        {
            if (Typing) EndTyping(restore: false); // the user clicked into another window: it has the keyboard now
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible && Typing) EndTyping(restore: true);
        };
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

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg != Native.WM_MOUSEACTIVATE || Typing) return IntPtr.Zero;
        handled = true;
        return new IntPtr(Native.MA_NOACTIVATE);
    }

    // ---- typing mode ----

    /// <summary>True while a text box in this window has the keyboard.</summary>
    public bool Typing { get; private set; }

    /// <summary>The window to give the keyboard back to when typing ends (the app the user was writing in).</summary>
    public Func<IntPtr>? TypingReturnTo { get; set; }

    /// <summary>Typing ended. True when the keyboard went back to <see cref="TypingReturnTo"/>.</summary>
    public event Action<bool>? TypingEnded;

    /// <summary>Take the keyboard so a text box here can be typed into. False if Windows didn't allow it.</summary>
    public bool BeginTyping()
    {
        if (Typing) return true;
        var hwnd = Handle;
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex & ~Native.WS_EX_NOACTIVATE));
        Typing = true;
        Native.SetForegroundWindow(hwnd);
        Activate();
        if (Native.GetForegroundWindow() == hwnd) return true;
        Log.Info("typing mode: Windows didn't give the panel the keyboard");
        EndTyping(restore: false);
        return false;
    }

    /// <summary>Stop taking the keyboard; with <paramref name="restore"/>, hand it back to the app the user was writing in.</summary>
    public void EndTyping(bool restore)
    {
        if (!Typing) return;
        Typing = false;
        var hwnd = Handle;
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_NOACTIVATE));
        bool back = false;
        if (restore && TypingReturnTo?.Invoke() is { } target && target != IntPtr.Zero)
        {
            Native.SetForegroundWindow(target);
            back = Native.GetForegroundWindow() == target;
            if (!back) Log.Info("typing mode: couldn't give the keyboard back to the app");
        }
        Keyboard.ClearFocus();
        TypingEnded?.Invoke(back);
    }

    static bool InTextBox(object? source)
    {
        var d = source as DependencyObject;
        while (d != null)
        {
            if (d is TextBoxBase) return true;
            d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    // ---- placement ----
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
