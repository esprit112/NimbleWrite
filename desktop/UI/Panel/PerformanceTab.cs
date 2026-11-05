using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>How the writing comes across: the editor's score and statistics, tone, and this week's insights.</summary>
internal sealed class PerformanceTab : PanelTab
{
    const string TonePrompt = "How does this sound to a reader?";
    static readonly string[] Categories = { "correctness", "clarity", "engagement", "delivery" };
    TextBlock toneText = null!;
    AgentRunner statsRunner = null!;
    StackPanel stats = null!;
    StackPanel insights = null!;
    EngineSnapshot? last;

    public PerformanceTab(IPanelActions host) : base(host)
    {
    }

    public override string Id => "performance";
    public override string Label => "Performance";
    public override string Glyph => Icons.Performance;

    protected override FrameworkElement Build()
    {
        var root = new StackPanel();
        var head = new DockPanel();
        var refresh = UiKit.Button("Refresh", Refresh, Kind.Ghost, "Measure the text again");
        DockPanel.SetDock(refresh, Dock.Right);
        head.Children.Add(refresh);
        head.Children.Add(Heading("Score and statistics"));
        root.Children.Add(head);
        statsRunner = new AgentRunner(Host);
        root.Children.Add(statsRunner);
        stats = new StackPanel();
        root.Children.Add(stats);

        root.Children.Add(Heading("Tone", 12));
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 0) };
        var detect = UiKit.Button("Detect tone", DetectTone);
        detect.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(detect, Dock.Right);
        row.Children.Add(detect);
        toneText = UiKit.Label(TonePrompt, 12.5, null, Theme.Muted, wrap: true);
        toneText.VerticalAlignment = VerticalAlignment.Center;
        toneText.Margin = new Thickness(0, 0, 8, 0);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(toneText, System.Windows.Automation.AutomationLiveSetting.Polite);
        row.Children.Add(toneText);
        root.Children.Add(row);

        root.Children.Add(Heading("This week", 14));
        insights = new StackPanel();
        root.Children.Add(insights);
        return root;
    }

    public override void OnShown(EngineSnapshot? s)
    {
        last = s ?? last;
        Refresh();
    }

    public override void OnState(EngineSnapshot s) => last = s; // measured on demand, not on every keystroke

    void Refresh()
    {
        var counts = CountsByCategory(last?.Issues ?? Array.Empty<PlacedIssue>());
        _ = statsRunner.Run(200_000, "measure", "Measuring…", null, (scope, ct) => Host.Api.TextStatsAsync(scope.Text, counts, ct), ShowStats);
        LoadInsights();
    }

    async void LoadInsights()
    {
        try
        {
            var r = await Host.Api.InsightsAsync(default);
            OnUi(() => ShowInsights(r));
        }
        catch (Exception e)
        {
            OnUi(() =>
            {
                insights.Children.Clear();
                insights.Children.Add(UiKit.Label(AgentRunner.Describe(e), 12, null, Theme.Muted, wrap: true));
            });
        }
    }

    internal void ShowStats(TextStatsResponse r, TextScope scope)
    {
        _ = View;
        stats.Children.Clear();
        var t = r.Stats;
        var top = new DockPanel { Margin = new Thickness(0, 2, 0, 8) };
        var ring = Ring(r.Score);
        ring.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(ring, Dock.Left);
        top.Children.Add(ring);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(UiKit.Label(r.Score == null ? "Not enough text for a score yet" : "Overall score", 13, FontWeights.SemiBold, Theme.Text, wrap: true));
        words.Children.Add(UiKit.Label($"{t.Words:N0} words · {Duration(t.ReadingTimeSec)} to read", 12, null, Theme.Muted, wrap: true));
        words.Children.Add(UiKit.Label(scope.Whole ? "All the text" : "Your selection", 11.5, null, Theme.Muted));
        top.Children.Add(words);
        stats.Children.Add(top);

        stats.Children.Add(Widgets.Pair("Readability", $"{t.Readability} · {r.Readability.Label}"));
        if (r.Readability.Audience.Length > 0) stats.Children.Add(Widgets.Pair("Suits", r.Readability.Audience));
        stats.Children.Add(Widgets.Pair("Grade level", t.GradeLevel.ToString(CultureInfo.InvariantCulture)));
        stats.Children.Add(Widgets.Pair("Characters", $"{t.Characters:N0} ({t.CharactersNoSpaces:N0} without spaces)"));
        stats.Children.Add(Widgets.Pair("Sentences / paragraphs", $"{t.Sentences:N0} / {t.Paragraphs:N0}"));
        stats.Children.Add(Widgets.Pair("Speaking time", Duration(t.SpeakingTimeSec)));
        stats.Children.Add(Widgets.Pair("Average sentence", $"{t.AvgSentenceLength:0.#} words"));
        stats.Children.Add(Widgets.Pair("Average word", $"{t.AvgWordLength:0.#} letters"));
        stats.Children.Add(Widgets.Pair("Unique words", $"{t.UniqueWordsPct}%"));
        stats.Children.Add(Widgets.Pair("Rare words", $"{t.RareWordsPct}%"));
        stats.Children.Add(Widgets.Pair("Long sentences", t.LongSentences.ToString(CultureInfo.InvariantCulture)));
    }

    internal void ShowInsights(InsightsResponse r)
    {
        _ = View;
        insights.Children.Clear();
        var days = r.Days.TakeLast(7).ToList();
        int max = Math.Max(1, days.Count == 0 ? 0 : days.Max(d => d.Stats.WordsChecked));
        var bars = new Grid { Height = 74, Margin = new Thickness(0, 4, 0, 4) };
        for (int i = 0; i < days.Count; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            var col = new DockPanel { Margin = new Thickness(3, 0, 3, 0) };
            var label = UiKit.Label(DayName(days[i].Date), 10.5, null, Theme.Muted);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            DockPanel.SetDock(label, Dock.Bottom);
            col.Children.Add(label);
            double h = Math.Max(2, 54.0 * days[i].Stats.WordsChecked / max);
            col.Children.Add(new Border
            {
                Height = h,
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(3),
                Background = days[i].Stats.WordsChecked > 0 ? Theme.Brand : Theme.Subtle,
                ToolTip = $"{days[i].Stats.WordsChecked:N0} words checked",
            });
            Grid.SetColumn(col, i);
            bars.Children.Add(col);
        }
        insights.Children.Add(UiKit.Label("Words checked per day", 11.5, null, Theme.Muted));
        insights.Children.Add(bars);
        int words = days.Sum(d => d.Stats.WordsChecked);
        int accepted = days.Sum(d => d.Stats.Accepted.Values.Sum());
        insights.Children.Add(Widgets.Pair("Words checked", words.ToString("N0", CultureInfo.CurrentCulture)));
        insights.Children.Add(Widgets.Pair("Suggestions accepted", accepted.ToString("N0", CultureInfo.CurrentCulture)));
        insights.Children.Add(Widgets.Pair("Documents", r.Totals.Docs.ToString("N0", CultureInfo.CurrentCulture)));
    }

    /// <summary>The score as a ring: a coloured arc for the score, the rest of the circle in the line colour.</summary>
    static FrameworkElement Ring(int? score)
    {
        const double size = 64, stroke = 6, r = (size - stroke) / 2;
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(new Ellipse { Stroke = Theme.Line, StrokeThickness = stroke, Margin = new Thickness(stroke / 2) });
        if (score is > 0)
        {
            double a = Math.Min(359.99, score.Value * 3.6) * Math.PI / 180;
            var c = new Point(size / 2, size / 2);
            var fig = new PathFigure { StartPoint = new Point(c.X, c.Y - r), IsClosed = false };
            fig.Segments.Add(new ArcSegment(new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            g.Children.Add(new Path { Data = new PathGeometry(new[] { fig }), Stroke = Scales.Brush(Scales.Grade(score.Value)), StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        var n = UiKit.Label(score?.ToString(CultureInfo.InvariantCulture) ?? "–", 18, FontWeights.Bold);
        n.HorizontalAlignment = HorizontalAlignment.Center;
        n.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(n);
        System.Windows.Automation.AutomationProperties.SetName(g, score == null ? "No score yet" : $"Score {score} out of 100");
        return g;
    }

    /// <summary>"45 sec", "1 min", "3 min 20 sec".</summary>
    internal static string Duration(int seconds)
    {
        if (seconds < 60) return $"{Math.Max(0, seconds)} sec";
        int m = seconds / 60, s = seconds % 60;
        return s == 0 ? $"{m} min" : $"{m} min {s} sec";
    }

    /// <summary>Open suggestions per category (all four, zero when none): the score's input, as in the editor.</summary>
    internal static Dictionary<string, int> CountsByCategory(IReadOnlyList<PlacedIssue> issues) =>
        Categories.ToDictionary(c => c, c => issues.Count(i => i.Issue.Category == c));

    /// <summary>"Thu" for "2026-11-05"; empty if the date can't be read.</summary>
    internal static string DayName(string date) =>
        DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("ddd", CultureInfo.InvariantCulture) : "";

    void DetectTone()
    {
        toneText.Text = "Listening to your text…";
        Host.DetectTone(OnTone);
    }

    void OnTone(ToneResult? t, string? error)
    {
        if (error != null || t == null)
        {
            toneText.Text = error ?? "Couldn't detect the tone.";
            return;
        }
        var top = t.Tones.OrderByDescending(x => x.Score).Take(3).Select(x => $"{x.Emoji} {x.Name}");
        string sentiment = t.Sentiment.Length > 0 ? char.ToUpperInvariant(t.Sentiment[0]) + t.Sentiment[1..] : "Neutral";
        toneText.Text = $"{string.Join("  ·  ", top)}\nFormality {t.Formality}/100 · {sentiment}";
    }

    public override void OnNewField()
    {
        toneText.Text = TonePrompt;
        statsRunner.Reset();
        stats.Children.Clear();
    }
}
