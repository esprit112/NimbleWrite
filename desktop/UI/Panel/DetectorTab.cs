using System;
using System.Windows;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>AI Detector: how much of the text reads as AI-generated, which passages, and why; Humanize sends a passage on.</summary>
internal sealed class DetectorTab : PanelTab
{
    AgentRunner runner = null!;
    StackPanel results = null!;

    public DetectorTab(IPanelActions host) : base(host)
    {
    }

    public override string Id => "detector";
    public override string Label => "AI Detector";
    public override string Glyph => Icons.Detector;

    protected override FrameworkElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Hint("Estimate how much of your text reads as AI-generated, combining an NVIDIA NIM model's judgement with writing statistics."));
        var go = UiKit.Button("Check for AI text", Go, Kind.Primary);
        go.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(go);
        runner = new AgentRunner(Host);
        root.Children.Add(runner);
        results = new StackPanel();
        root.Children.Add(results);
        return root;
    }

    void Go()
    {
        results.Children.Clear();
        _ = runner.Run(60_000, "check", "Checking the writing… (usually 5–20 s)", s => AgentRunner.TooFewWords(s, 15),
            (scope, ct) => Host.Api.DetectAsync(scope.Text, ct), Show);
    }

    internal void Show(AiDetectResult r, TextScope scope)
    {
        _ = View;
        results.Children.Clear();
        results.Children.Add(Widgets.Headline($"{Math.Round(r.Score)}%", Scales.Brush(Scales.AiLikely(r.Score)), "of your text appears AI-generated", r.Verdict.Length > 0 ? r.Verdict : null));
        foreach (var s in r.Signals) results.Children.Add(Widgets.Pair(s.Label, s.Value));
        if (r.Passages.Count > 0)
        {
            var head = UiKit.Label("Passages", 13, FontWeights.SemiBold);
            head.Margin = new Thickness(0, 10, 0, 4);
            results.Children.Add(head);
        }
        foreach (var p in r.Passages)
        {
            var brush = Scales.Brush(Scales.AiLikely(p.Score));
            var body = new StackPanel();
            var top = new DockPanel();
            if (p.Score >= 50)
            {
                var humanize = UiKit.Button("Humanize", () => Humanize(scope, p.Text), Kind.Ghost, "Rewrite just this passage in the Humanizer");
                DockPanel.SetDock(humanize, Dock.Right);
                top.Children.Add(humanize);
            }
            top.Children.Add(UiKit.Label($"{Math.Round(p.Score)}% AI-like", 12, FontWeights.SemiBold, brush));
            body.Children.Add(top);
            var text = UiKit.Label(p.Text, 12.5, null, Theme.Text, wrap: true);
            text.MaxHeight = 58; // about three lines
            text.TextTrimming = TextTrimming.WordEllipsis;
            body.Children.Add(text);
            foreach (var reason in p.Reasons) body.Children.Add(UiKit.Label($"• {reason}", 11.5, null, Theme.Muted, wrap: true));
            results.Children.Add(Widgets.Card(body, brush));
        }
        results.Children.Add(Widgets.Fine("No AI detector is reliable enough to prove who wrote something. Treat this as a signal for editing, never as evidence."));
    }

    void Humanize(TextScope checkedScope, string passage)
    {
        var p = PassageScope(checkedScope, passage);
        if (p == null)
        {
            Host.Notice("Couldn't find that passage in the text. It may have been reworded by the check.");
            return;
        }
        Host.HumanizePassage(p);
    }

    /// <summary>
    /// The flagged passage as a scope of its own within the text that was checked, or null if it isn't there word for
    /// word. The engine re-checks the field still holds that text before rewriting.
    /// </summary>
    internal static TextScope? PassageScope(TextScope checkedScope, string passage)
    {
        string p = passage.Trim();
        if (p.Length == 0) return null;
        int at = checkedScope.FullText.IndexOf(p, StringComparison.Ordinal);
        return at < 0 ? null : new TextScope(at, p.Length, p, checkedScope.FullText, false);
    }

    public override void OnNewField()
    {
        runner.Reset();
        results.Children.Clear();
    }
}
