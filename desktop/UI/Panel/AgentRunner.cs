using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>
/// Runs a tool against the selection (or all the text): captures it through the shared checks, calls the server, and
/// shows the result. Shows what it's working on with a Cancel button, the error if it fails, and when the result is
/// from. A newer run, Cancel or a new text box makes any older call's result be ignored.
/// </summary>
internal sealed class AgentRunner : StackPanel
{
    readonly IPanelActions host;
    readonly TextBlock status;
    readonly Border cancel;
    CancellationTokenSource? cts;
    int seq;

    public AgentRunner(IPanelActions host)
    {
        this.host = host;
        Margin = new Thickness(0, 8, 0, 6);
        var row = new DockPanel();
        cancel = UiKit.Button("Cancel", Cancel, Kind.Ghost);
        cancel.Visibility = Visibility.Collapsed;
        cancel.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(cancel, Dock.Right);
        row.Children.Add(cancel);
        status = UiKit.Label("", 12, null, Theme.Muted, wrap: true);
        status.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        row.Children.Add(status);
        Children.Add(row);
        Visibility = Visibility.Collapsed;
    }

    public bool Busy => cts != null;

    public string Status => status.Text;

    /// <summary>
    /// Capture the text (at most <paramref name="maxChars"/>), check it with <paramref name="check"/>, then
    /// <paramref name="call"/> the server and <paramref name="show"/> what comes back.
    /// </summary>
    public async Task Run<T>(int maxChars, string verb, string working, Func<TextScope, string?>? check, Func<TextScope, CancellationToken, Task<T>> call, Action<T, TextScope> show)
    {
        cts?.Cancel();
        int my = ++seq;
        var mine = cts = new CancellationTokenSource();
        SetStatus("Reading the text…", busy: true);
        var (scope, problem) = await host.CaptureAsync(maxChars, verb);
        if (my != seq) return;
        problem ??= scope == null ? "Click into a text box first." : check?.Invoke(scope);
        if (problem != null)
        {
            Finish(my, problem, error: true);
            return;
        }
        SetStatus($"{working}", busy: true);
        try
        {
            var result = await call(scope!, mine.Token);
            if (my != seq) return;
            show(result, scope!);
            Finish(my, $"{(scope!.Whole ? "Checked all the text" : "Checked your selection")} at {DateTime.Now:HH:mm}.", error: false);
        }
        catch (OperationCanceledException)
        {
            if (my == seq) Finish(my, "Stopped.", error: false);
        }
        catch (Exception e)
        {
            if (my == seq) Finish(my, Describe(e), error: true);
        }
    }

    /// <summary>A plain message for a failed call.</summary>
    internal static string Describe(Exception e) => e switch
    {
        ApiException api => api.Message,
        HttpRequestException => "Can't reach NimbleWrite's server right now. Try again in a moment.",
        _ => e.Message,
    };

    /// <summary>"Write a bit more first" when a tool needs more text than the scope holds.</summary>
    internal static string? TooFewWords(TextScope scope, int min) =>
        TextModel.CountWords(scope.Text) < min ? $"Write a bit more first (at least {min} words)." : null;

    public void Cancel()
    {
        if (cts == null) return;
        seq++;
        cts.Cancel();
        cts = null;
        SetStatus("Stopped.", busy: false);
    }

    /// <summary>Forget everything (a new text box): stop any call and hide the status.</summary>
    public void Reset()
    {
        seq++;
        cts?.Cancel();
        cts = null;
        cancel.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
    }

    void Finish(int my, string text, bool error)
    {
        if (my != seq) return;
        cts = null;
        SetStatus(text, busy: false);
        status.Foreground = error ? Scales.Brush(Level.Bad) : Theme.Muted;
    }

    void SetStatus(string text, bool busy)
    {
        status.Text = text;
        status.Foreground = Theme.Muted;
        cancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Visibility = Visibility.Visible;
    }
}
