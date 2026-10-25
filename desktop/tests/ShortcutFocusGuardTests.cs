using System;
using NimbleWrite.Desktop.Core;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

/// <summary>
/// Ctrl+Alt+N in Windows 11 Notepad: Windows keeps the N from Notepad, Notepad sees a lone Alt and moves focus to its
/// menu bar ~75 ms after the keys come up. These pin down when NimbleWrite puts focus back, and when it must not.
/// </summary>
public sealed class ShortcutFocusGuardTests
{
    static readonly DateTime T0 = new(2026, 10, 25, 17, 0, 0, DateTimeKind.Utc);

    static DateTime At(int ms) => T0.AddMilliseconds(ms);

    [Fact]
    public void NotepadsMenuJumpIsPutBackOnceTheKeysAreUp()
    {
        var guard = new ShortcutFocusGuard();
        guard.Pressed(At(0));
        guard.Observe(At(120), keysDown: true);
        Assert.Equal(FocusJump.Wait, guard.Decide(At(240), keysDown: true, sameWindow: true, UiaIds.MenuItem));
        Assert.Equal(FocusJump.PutBack, guard.Decide(At(360), keysDown: false, sameWindow: true, UiaIds.MenuItem));
    }

    [Fact]
    public void AJumpThatComesJustAfterTheKeysAreUpIsStillOurs()
    {
        var guard = new ShortcutFocusGuard();
        guard.Pressed(At(0));
        guard.Observe(At(120), keysDown: false); // keys up; Notepad hasn't moved focus yet
        Assert.True(guard.Watching(At(500)));
        Assert.Equal(FocusJump.PutBack, guard.Decide(At(240), keysDown: false, sameWindow: true, UiaIds.MenuItem));
    }

    [Fact]
    public void IfPuttingFocusBackDoesNotStickEscapeIsPressedOnceThenItGivesUp()
    {
        var guard = new ShortcutFocusGuard();
        guard.Pressed(At(0));
        Assert.Equal(FocusJump.PutBack, guard.Decide(At(120), false, true, UiaIds.MenuBar));
        Assert.Equal(FocusJump.Escape, guard.Decide(At(240), false, true, UiaIds.MenuBar));
        Assert.Equal(FocusJump.NotOurs, guard.Decide(At(360), false, true, UiaIds.MenuBar));
        Assert.False(guard.Watching(At(361)));
    }

    [Fact]
    public void FocusMovingToAnotherAppIsTheUserLeaving()
    {
        var guard = new ShortcutFocusGuard();
        guard.Pressed(At(0));
        Assert.Equal(FocusJump.NotOurs, guard.Decide(At(120), false, sameWindow: false, UiaIds.MenuItem));
        Assert.Equal(FocusJump.NotOurs, guard.Decide(At(240), false, sameWindow: true, UiaIds.MenuItem)); // stopped for good
    }

    [Fact]
    public void FocusMovingToAnotherFieldOrButtonIsTheUserLeaving()
    {
        foreach (int type in new[] { UiaIds.Edit, UiaIds.Document, UiaIds.ComboBox, 50000 /* Button */ })
        {
            var guard = new ShortcutFocusGuard();
            guard.Pressed(At(0));
            Assert.Equal(FocusJump.NotOurs, guard.Decide(At(120), false, true, type));
        }
    }

    [Fact]
    public void OnlyWhereALoneAltSendsFocusCounts()
    {
        Assert.True(ShortcutFocusGuard.IsAltTarget(UiaIds.MenuItem)); // Notepad's File, Edit, View
        Assert.True(ShortcutFocusGuard.IsAltTarget(UiaIds.MenuBar));
        Assert.True(ShortcutFocusGuard.IsAltTarget(UiaIds.Menu));
        Assert.True(ShortcutFocusGuard.IsAltTarget(UiaIds.TabItem)); // Office's ribbon tabs
        Assert.False(ShortcutFocusGuard.IsAltTarget(UiaIds.Edit));
        Assert.False(ShortcutFocusGuard.IsAltTarget(UiaIds.Document));
    }

    [Fact]
    public void AJumpLongAfterTheKeysCameUpIsNotOurs()
    {
        var guard = new ShortcutFocusGuard();
        guard.Pressed(At(0));
        guard.Observe(At(100), keysDown: false);
        var late = At(100) + ShortcutFocusGuard.AfterKeysUp + TimeSpan.FromMilliseconds(1);
        Assert.False(guard.Watching(late));
        Assert.Equal(FocusJump.NotOurs, guard.Decide(late, false, true, UiaIds.MenuItem));
    }

    [Fact]
    public void KeysHeldForAgesStopTheWatch()
    {
        var guard = new ShortcutFocusGuard();
        guard.Pressed(At(0));
        var late = T0 + ShortcutFocusGuard.Longest + TimeSpan.FromMilliseconds(1);
        Assert.Equal(FocusJump.NotOurs, guard.Decide(late, keysDown: true, true, UiaIds.MenuItem));
    }

    [Fact]
    public void KeysPressedAgainAfterTheShortcutAreTheUsersOwnMenu()
    {
        var guard = new ShortcutFocusGuard();
        guard.Pressed(At(0));
        guard.Observe(At(120), keysDown: false);
        guard.Observe(At(360), keysDown: true); // e.g. Alt+F to open File on purpose
        Assert.False(guard.Watching(At(361)));
        Assert.Equal(FocusJump.NotOurs, guard.Decide(At(480), keysDown: false, true, UiaIds.MenuItem));
    }

    [Fact]
    public void NothingIsWatchedBeforeTheShortcut()
    {
        var guard = new ShortcutFocusGuard();
        Assert.False(guard.Watching(T0));
        Assert.Equal(FocusJump.NotOurs, guard.Decide(T0, false, true, UiaIds.MenuItem));
    }

    [Fact]
    public void EachPressStartsAfresh()
    {
        var guard = new ShortcutFocusGuard();
        guard.Pressed(At(0));
        Assert.Equal(FocusJump.PutBack, guard.Decide(At(120), false, true, UiaIds.MenuItem));
        Assert.Equal(FocusJump.Escape, guard.Decide(At(240), false, true, UiaIds.MenuItem));
        guard.Pressed(At(5000)); // the shortcut again, to close the panel
        Assert.Equal(FocusJump.PutBack, guard.Decide(At(5120), false, true, UiaIds.MenuItem));
    }
}
