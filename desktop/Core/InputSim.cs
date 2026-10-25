using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace NimbleWrite.Desktop.Core;

/// <summary>Synthesised keyboard input, used to apply fixes inside other apps.</summary>
internal static class InputSim
{
    public const ushort VK_BACK = 0x08, VK_ESCAPE = 0x1B, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_C = 0x43, VK_V = 0x56;

    /// <summary>Wait until Ctrl/Alt/Shift/Win are released, so typed text can't turn into shortcuts.</summary>
    public static bool WaitForModifiersReleased(int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (!ModifiersDown()) return true;
            Thread.Sleep(15);
        }
        return false;
    }

    /// <summary>Is Ctrl, Alt, Shift or Win held down right now?</summary>
    public static bool ModifiersDown() => IsDown(VK_SHIFT) || IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN);

    static bool IsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// Type text as Unicode key events (keyboard-layout independent), one character per SendInput call with a short
    /// pause. A single burst can be garbled by apps that fall behind on synthesised input — Windows 11 Notepad repeats
    /// the last letter — so callers still verify the result. Line breaks are skipped: paste multi-line text instead.
    /// </summary>
    public static void TypeText(string text, int gapMs = 8)
    {
        int size = Marshal.SizeOf<Native.INPUT>();
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\r' || ch == '\n') continue;
            var keys = new List<Native.INPUT>(4) { Key(0, ch, Native.KEYEVENTF_UNICODE), Key(0, ch, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP) };
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                char low = text[++i]; // keep a surrogate pair in one call so it can't be split
                keys.Add(Key(0, low, Native.KEYEVENTF_UNICODE));
                keys.Add(Key(0, low, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP));
            }
            Native.SendInput((uint)keys.Count, keys.ToArray(), size);
            Thread.Sleep(gapMs);
        }
    }

    public static void Press(ushort vk) => Send(new List<Native.INPUT> { Key(vk, '\0', 0), Key(vk, '\0', Native.KEYEVENTF_KEYUP) });

    public static void Chord(ushort modifier, ushort vk) => Send(new List<Native.INPUT>
    {
        Key(modifier, '\0', 0), Key(vk, '\0', 0), Key(vk, '\0', Native.KEYEVENTF_KEYUP), Key(modifier, '\0', Native.KEYEVENTF_KEYUP),
    });

    static Native.INPUT Key(ushort vk, char scan, uint flags) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    static void Send(List<Native.INPUT> inputs)
    {
        const int chunk = 128;
        int size = Marshal.SizeOf<Native.INPUT>();
        for (int i = 0; i < inputs.Count; i += chunk)
        {
            var part = inputs.GetRange(i, Math.Min(chunk, inputs.Count - i)).ToArray();
            Native.SendInput((uint)part.Length, part, size);
            if (i + chunk < inputs.Count) Thread.Sleep(4);
        }
    }
}
