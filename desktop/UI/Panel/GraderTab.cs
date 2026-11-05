using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>AI Grader: an estimated grade with feedback per criterion, against an optional brief or rubric.</summary>
internal sealed class GraderTab : PanelTab
{
    /// <summary>Same as LEVELS in src/editor/panels/AgentPanels.tsx.</summary>
    internal static readonly string[] Levels = { "Secondary school (GCSE)", "Sixth form (A-level)", "Undergraduate", "Postgraduate", "Professional" };

    ChipGroup level = null!;
    InputBox brief = null!;
    AgentRunner runner = null!;
    StackPanel results = null!;

    public GraderTab(IPanelActions host) : base(host)
    {
    }

    public override string Id => "grader";
    public override string Label => "AI Grader";
    public override string Glyph => Icons.Grader;

    protected override FrameworkElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Hint("Get rubric-aligned feedback and an estimated grade before you submit."));
        root.Children.Add(Heading("Level"));
        level = new ChipGroup(Levels.Select(l => (l, l, "")), "Undergraduate") { Margin = new Thickness(0, 4, 0, 4) };
        root.Children.Add(level);
        root.Children.Add(Heading("Assignment brief or rubric (optional)", 2));
        brief = new InputBox("Assignment brief or rubric", 20_000, multiline: true, "Paste the task, marking criteria or rubric…") { Margin = new Thickness(0, 4, 0, 8) };
        brief.Submitted += Go;
        root.Children.Add(brief);
        var go = UiKit.Button("Grade my writing", Go, Kind.Primary);
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
        string rubric = brief.Text, lvl = level.Selected;
        _ = runner.Run(60_000, "grade", "Marking against the rubric… (usually 15–25 s)", s => AgentRunner.TooFewWords(s, 30),
            (scope, ct) => Host.Api.GraderAsync(scope.Text, rubric, lvl, ct), Show);
    }

    internal static string ScoreLine(double score) => $"Estimated {Math.Round(score)}/100";

    internal void Show(GraderResult g, TextScope scope)
    {
        _ = View;
        results.Children.Clear();
        var brush = Scales.Brush(Scales.Grade(g.Score));
        var head = new DockPanel();
        var badge = new Border
        {
            Width = 54,
            Height = 54,
            CornerRadius = new CornerRadius(12),
            Background = brush,
            Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock { Text = g.Grade, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Theme.OnBrand, FontFamily = UiKit.Font, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(UiKit.Label(ScoreLine(g.Score), 13, FontWeights.SemiBold));
        words.Children.Add(UiKit.Label(g.Summary, 12, null, Theme.Muted, wrap: true));
        head.Children.Add(words);
        results.Children.Add(Widgets.Card(head, Theme.Line, Theme.Raised, 10));

        foreach (var c in g.Criteria)
        {
            var row = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
            var top = new DockPanel();
            var n = UiKit.Label($"{Math.Round(c.Score)}", 12.5, FontWeights.SemiBold);
            DockPanel.SetDock(n, Dock.Right);
            top.Children.Add(n);
            top.Children.Add(UiKit.Label(c.Name, 12.5, FontWeights.SemiBold, Theme.Text, wrap: true));
            row.Children.Add(top);
            row.Children.Add(Widgets.Bar(c.Score, Scales.Brush(Scales.Grade(c.Score))));
            row.Children.Add(UiKit.Label(c.Feedback, 12, null, Theme.Muted, wrap: true));
            results.Children.Add(row);
        }
        results.AddIf(Widgets.Bullets("Strengths", g.Strengths));
        results.AddIf(Widgets.Bullets("Improve before submitting", g.Improvements));
        results.Children.Add(Widgets.Fine("An AI estimate to guide revision. Your marker's judgement may differ."));
    }

    public override void OnNewField()
    {
        runner.Reset();
        results.Children.Clear();
    }
}
