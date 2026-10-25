using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using NimbleWrite.Desktop.UI;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

public sealed class UiKitTests
{
    [Fact]
    public void ButtonsAreInvokableButtonsForScreenReadersButNeverTakeFocus() => OnSta(() =>
    {
        int clicks = 0;
        var save = UiKit.Button("Save", () => clicks++, Kind.Primary);
        var peer = UIElementAutomationPeer.CreatePeerForElement(save);

        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
        Assert.Equal("Save", peer.GetName());
        Assert.False(peer.IsKeyboardFocusable());
        Assert.False(save.Focusable);

        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
        invoke.Invoke();
        Assert.Equal(0, clicks); // queued, so the action can safely close its own window
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        Assert.Equal(1, clicks);
    });

    [Fact]
    public void AnIconButtonIsNamedAfterItsTooltip() => OnSta(() =>
    {
        var close = UiKit.Clickable(UiKit.Dot(Theme.Brand), () => { }, Kind.Ghost, "Close");
        Assert.Equal("Close", UIElementAutomationPeer.CreatePeerForElement(close).GetName());
    });

    static void OnSta(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        failure?.Throw();
    }
}
