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

    public void Dispose()
    {
        Native.UnregisterHotKey(source.Handle, Id);
        source.Dispose();
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
