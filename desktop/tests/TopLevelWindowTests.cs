using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using NimbleWrite.Desktop.Core;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

/// <summary>
/// UI Automation's SetFocus on a Win32 edit inside a form (NimbleWrite's own Refocus does this) leaves Windows reporting
/// the edit's child hwnd as the foreground window. The engine compares windows by their top-level window, so that
/// state doesn't make it think the user left the app.
/// </summary>
public sealed class TopLevelWindowTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(int ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr h);

    const uint WS_CHILD = 0x40000000;

    [Fact]
    public void AChildWindowResolvesToItsTopLevelWindow() => WpfTestKit.OnSta(() =>
    {
        var w = new Window { Width = 200, Height = 100, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -3000 };
        var top = new WindowInteropHelper(w).EnsureHandle();
        var edit = CreateWindowEx(0, "EDIT", "", WS_CHILD, 0, 0, 100, 20, top, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, edit);
        try
        {
            Assert.Equal(top, Native.TopLevel(edit));
            Assert.Equal(top, Native.TopLevel(top));
            Assert.Equal(IntPtr.Zero, Native.TopLevel(IntPtr.Zero));
        }
        finally
        {
            DestroyWindow(edit);
            w.Close();
        }
    });

    [Fact]
    public void TheEngineComparesForegroundWindowsByTopLevelWindow()
    {
        // Every place that compares the foreground window with the field's window must go through ForegroundTopLevel.
        string root = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !System.IO.File.Exists(System.IO.Path.Combine(root, "desktop", "NimbleWrite.Desktop.csproj")); i++)
            root = System.IO.Path.GetDirectoryName(root.TrimEnd('\\', '/'))!;
        foreach (var file in new[] { "Core/Engine.cs", "Core/EngineActions.cs", "UI/NoActivateWindow.cs" })
        {
            string src = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "desktop", file));
            foreach (var line in src.Split('\n'))
            {
                if (!line.Contains("GetForegroundWindow()") || line.TrimStart().StartsWith("//")) continue;
                // BeginTyping checks its own (top-level) window directly; that's the one allowed exception.
                Assert.True(line.Contains("== hwnd)"), $"{file}: compare top-level windows (Native.ForegroundTopLevel()), not GetForegroundWindow(): {line.Trim()}");
            }
        }
    }
}
