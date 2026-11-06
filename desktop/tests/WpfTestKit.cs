using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

/// <summary>Running WPF for real in tests: an STA thread, a dispatcher to finish awaits on, the visual tree, PNGs.</summary>
internal static class WpfTestKit
{
    /// <summary>Run <paramref name="test"/> on a fresh STA thread with a dispatcher synchronisation context, so awaits come back to it.</summary>
    public static void OnSta(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                test();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        failure?.Throw();
    }

    /// <summary>Pump the dispatcher until <paramref name="task"/> is done (its continuations run on this thread), then rethrow any failure.</summary>
    public static void Wait(Task? task, int timeoutMs = 10_000)
    {
        if (task == null) return;
        var d = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => d.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false), TaskScheduler.Default);
        using var timer = new Timer(_ => d.BeginInvoke(() => frame.Continue = false), null, timeoutMs, Timeout.Infinite);
        Dispatcher.PushFrame(frame);
        Assert.True(task.IsCompleted, "Timed out waiting for the window's request.");
        // Let the UI work queued after the await run too.
        var after = new DispatcherFrame();
        d.BeginInvoke(DispatcherPriority.ContextIdle, () => after.Continue = false);
        Dispatcher.PushFrame(after);
        task.GetAwaiter().GetResult();
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    public static void Save(BitmapSource bmp, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }
}
