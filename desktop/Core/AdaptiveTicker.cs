using System;

namespace NimbleWrite.Desktop.Core;

/// <summary>
/// How often the engine looks at the focused field. Every look is a cross-process UI Automation call that the
/// target app pays for too (Chromium rebuilds the text each time), so look often only while something is happening:
/// every 120 ms for 3 s after any change, then every 250 ms, then every 500 ms once nothing has changed for 20 s.
/// Any change (text, focus, the field moving, a command from the UI) snaps it straight back to 120 ms.
/// </summary>
public sealed class AdaptiveTicker
{
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan Medium = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan FastFor = TimeSpan.FromSeconds(3);
    static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(20);

    DateTime activeAt;

    public AdaptiveTicker(DateTime now) => activeAt = now;

    /// <summary>Something changed at <paramref name="at"/>.</summary>
    public void Activity(DateTime at)
    {
        if (at > activeAt) activeAt = at;
    }

    public TimeSpan Interval(DateTime now)
    {
        var quiet = now - activeAt;
        return quiet < FastFor ? Fast : quiet < SlowAfter ? Medium : Slow;
    }

    /// <summary>When to look next, given when we last looked: brought forward as soon as there's activity.</summary>
    public DateTime NextTick(DateTime lastTick, DateTime now) => lastTick + Interval(now);
}
