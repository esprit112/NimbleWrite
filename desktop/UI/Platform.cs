using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI;

/// <summary>A global shortcut (default Ctrl+Alt+N) that toggles the assistant for the focused field.</summary>
public sealed class HotkeyListener : IDisposable
{
    const int Id = 0x4E57;
    readonly HwndSource source;
    readonly Action onPressed;

    public bool Registered { get; }

    public HotkeyListener(string spec, Action onPressed)
    {
        this.onPressed = onPressed;
        source = new HwndSource(new HwndSourceParameters("NimbleWrite hotkey") { WindowStyle = 0, ParentWindow = new IntPtr(-3) }); // message-only window
        source.AddHook(Hook);
        if (TryParse(spec, out uint mods, out uint vk)) Registered = Native.RegisterHotKey(source.Handle, Id, mods | Native.MOD_NOREPEAT, vk);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && w.ToInt32() == Id)
        {
            handled = true;
            onPressed();
        }
        return IntPtr.Zero;
    }

    /// <summary>Parse "Ctrl+Alt+N" style shortcuts. At least one modifier is required.</summary>
    public static bool TryParse(string spec, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        foreach (var raw in spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= Native.MOD_CONTROL;
                    break;
                case "alt":
                    mods |= Native.MOD_ALT;
                    break;
                case "shift":
                    mods |= Native.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    mods |= Native.MOD_WIN;
                    break;
                default:
                    if (raw.Length == 1 && char.IsAsciiLetterOrDigit(raw[0])) vk = char.ToUpperInvariant(raw[0]);
                    else if (Enum.TryParse<Key>(raw, true, out var key) && key != Key.None) vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    else return false;
                    break;
            }
        }
        return vk != 0 && mods != 0;
    }

    /// <summary>
    /// The shortcut for a key press, written the way desktop.json stores it ("Ctrl+Alt+N"), or null while only
    /// modifiers are held, or when it would get in the way of typing (no Ctrl, Alt or Win: N or Shift+A alone).
    /// </summary>
    internal static string? Format(ModifierKeys mods, Key key)
    {
        if (key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt
            or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return null;
        if ((mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0) return null;
        var parts = new System.Collections.Generic.List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString());
        string spec = string.Join("+", parts);
        return TryParse(spec, out _, out _) ? spec : null;
    }

    public void Dispose()
    {
        Native.UnregisterHotKey(source.Handle, Id);
        source.Dispose();
    }
}

/// <summary>
/// Tells the engine the moment focus moves or another window comes to the front (Windows focus events, delivered on
/// this thread's message loop), so the engine can look at the new field straight away even while it's polling slowly.
/// Only the fact that something changed is passed on: no window titles, no text.
/// </summary>
internal sealed class FocusWatcher : IDisposable
{
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_OBJECT_FOCUS = 0x8005;
    const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnhookWinEvent(IntPtr hook);

    readonly WinEventProc proc; // kept alive for as long as the hooks are
    readonly IntPtr foreground, focus;

    public FocusWatcher(Action changed)
    {
        proc = (_, _, _, _, _, _, _) => changed();
        foreground = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        focus = SetWinEventHook(EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (foreground == IntPtr.Zero || focus == IntPtr.Zero) Log.Info("focus events unavailable; NimbleWrite notices new text boxes on its next look instead");
    }

    public void Dispose()
    {
        if (foreground != IntPtr.Zero) UnhookWinEvent(foreground);
        if (focus != IntPtr.Zero) UnhookWinEvent(focus);
    }
}

/// <summary>
/// Clipboard work for the engine, done on the UI thread. Used only to paste multi-line replacements; the previous
/// clipboard contents are put back afterwards, and our text is kept out of clipboard history.
/// </summary>
internal sealed class ClipboardBridge : IClipboardBridge
{
    readonly Dispatcher ui;

    public ClipboardBridge(Dispatcher ui) => this.ui = ui;

    public object? Put(string text) => ui.Invoke(() =>
    {
        DataObject? saved = null;
        try
        {
            var cur = Clipboard.GetDataObject();
            if (cur != null)
            {
                saved = new DataObject();
                foreach (var format in cur.GetFormats(false))
                {
                    try
                    {
                        var d = cur.GetData(format);
                        if (d != null) saved.SetData(format, d);
                    }
                    catch (Exception)
                    {
                        // Some formats (e.g. delayed-render ones) can't be copied; skip them.
                    }
                }
            }
        }
        catch (Exception)
        {
        }
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        for (int i = 0; i < 6; i++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                break;
            }
            catch (COMException)
            {
                Thread.Sleep(30); // another app has the clipboard open
            }
        }
        return (object?)saved;
    });

    public void Restore(object? saved) => ui.Invoke(() =>
    {
        try
        {
            if (saved is DataObject d && d.GetFormats(false).Length > 0) Clipboard.SetDataObject(d, true);
            else Clipboard.Clear();
        }
        catch (Exception e)
        {
            Log.Error("clipboard restore", e);
        }
    });
}
