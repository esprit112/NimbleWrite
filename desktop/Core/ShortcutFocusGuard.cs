using System;

namespace NimbleWrite.Desktop.Core;

/// <summary>What the engine should do about focus that has left the field just after our shortcut.</summary>
internal enum FocusJump
{
    /// <summary>Not caused by the shortcut: treat it as the user leaving the field.</summary>
    NotOurs,

    /// <summary>The shortcut's keys are still held: keep the field and wait for them to come up.</summary>
    Wait,

    /// <summary>Move keyboard focus back into the field.</summary>
    PutBack,

    /// <summary>Putting focus back didn't stick (a classic menu loop ignores it): press Escape once to close the menu.</summary>
    Escape,
}

/// <summary>
/// Our shortcut has Alt in it, and Windows keeps the letter key from the app, so the app sees Alt pressed and released on
/// its own. Many apps then move keyboard focus to their menu bar: Notepad on Windows 11 does it every time, whatever order
/// the keys come up in, and no extra key slipped into the input stops it. So for a moment after the shortcut, focus landing
/// on a menu in the same window isn't the user leaving the field: once the keys are up, focus goes back where they were
/// typing. Pure logic; the engine supplies the clock and the facts.
/// </summary>
internal sealed class ShortcutFocusGuard
{
    /// <summary>How long after the keys come up a jump still counts as the shortcut's doing.</summary>
    public static readonly TimeSpan AfterKeysUp = TimeSpan.FromMilliseconds(900);

    /// <summary>Keys held longer than this mean the user is doing something else.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(5);

    DateTime pressedAt = DateTime.MinValue;
    DateTime keysUpAt = DateTime.MinValue;
    int tries;

    /// <summary>The shortcut was pressed (Windows reports it while the keys are still down).</summary>
    public void Pressed(DateTime now)
    {
        pressedAt = now;
        keysUpAt = DateTime.MinValue;
        tries = 0;
    }

    /// <summary>True from the shortcut until a focus jump could no longer be its doing.</summary>
    public bool Watching(DateTime now) =>
        pressedAt != DateTime.MinValue && now - pressedAt <= Longest
        && (keysUpAt == DateTime.MinValue || now - keysUpAt <= AfterKeysUp);

    /// <summary>
    /// Call on every tick while <see cref="Watching"/>, with whether Ctrl, Alt, Shift or Win is held. Notes when the
    /// shortcut's keys have all come up; keys going down again after that are the user's own, so watching stops.
    /// </summary>
    public void Observe(DateTime now, bool keysDown)
    {
        if (!Watching(now)) return;
        if (!keysDown && keysUpAt == DateTime.MinValue) keysUpAt = now;
        else if (keysDown && keysUpAt != DateTime.MinValue) Stop();
    }

    /// <summary>
    /// Focus has left the field. <paramref name="sameWindow"/>: it is still in the field's own window and app;
    /// <paramref name="controlType"/>: the UI Automation control type that has focus now.
    /// </summary>
    public FocusJump Decide(DateTime now, bool keysDown, bool sameWindow, int controlType)
    {
        Observe(now, keysDown);
        if (!Watching(now)) return FocusJump.NotOurs;
        if (!sameWindow || !IsAltTarget(controlType))
        {
            Stop(); // another app, or a real field or button: the user has moved on
            return FocusJump.NotOurs;
        }
        if (keysDown) return FocusJump.Wait; // still the shortcut's own keys: moving focus now could start it all again
        tries++;
        if (tries == 1) return FocusJump.PutBack;
        if (tries == 2) return FocusJump.Escape;
        Stop();
        return FocusJump.NotOurs;
    }

    /// <summary>Where a lone Alt sends keyboard focus: the menu bar, its menus, or (Office) the ribbon's tabs.</summary>
    public static bool IsAltTarget(int controlType) =>
        controlType is UiaIds.Menu or UiaIds.MenuBar or UiaIds.MenuItem or UiaIds.TabItem;

    void Stop() => pressedAt = DateTime.MinValue;
}
