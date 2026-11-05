using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>Reader Reactions: how a particular reader will take the text (takeaways, questions, confusion).</summary>
internal sealed class ReaderTab : PanelTab
{
    /// <summary>Same as READERS in src/editor/panels/AgentPanels.tsx.</summary>
    internal static readonly string[] Readers = { "My manager", "A university professor", "A potential client", "A hiring manager", "A busy executive", "A general reader" };

    ChipGroup readers = null!;
    InputBox custom = null!;
    AgentRunner runner = null!;
    StackPanel results = null!;

    public ReaderTab(IPanelActions host) : base(host)
    {
    }

    public override string Id => "reader";
    public override string Label => "Reader Reactions";
    public override string Glyph => Icons.Reader;

    protected override FrameworkElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Hint("Predict how a specific reader will react: what they'll take away, what they'll ask, and where they might get confused."));
        root.Children.Add(Heading("Who's reading?"));
        readers = new ChipGroup(Readers.Select(r => (r, r, "")), Readers[0]) { Margin = new Thickness(0, 4, 0, 2) };
        root.Children.Add(readers);
        custom = new InputBox("Describe your reader", 300, placeholder: "Or describe them, e.g. a sceptical head teacher") { Margin = new Thickness(0, 0, 0, 8) };
        custom.Submitted += Go;
        root.Children.Add(custom);
        var go = UiKit.Button("Predict reactions", Go, Kind.Primary);
        go.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(go);
        runner = new AgentRunner(Host);
        root.Children.Add(runner);
        results = new StackPanel();
        root.Children.Add(results);
        return root;
    }

    string Reader => custom.Text.Trim().Length > 0 ? custom.Text.Trim() : readers.Selected;

    void Go()
    {
        results.Children.Clear();
        string reader = Reader;
        _ = runner.Run(40_000, "read", $"Reading as {reader.ToLowerInvariant()}… (usually 10–20 s)", s => AgentRunner.TooFewWords(s, 15),
            (scope, ct) => Host.Api.ReaderAsync(scope.Text, reader, ct), Show);
    }

    internal void Show(ReaderReactions r, TextScope scope)
    {
        _ = View;
        results.Children.Clear();
        if (r.Overall.Length > 0) results.Children.Add(Widgets.Card(UiKit.Label(r.Overall, 12.5, null, Theme.Text, wrap: true), Theme.Line, Theme.Raised));
        results.AddIf(Widgets.Bullets("Key takeaways", r.Takeaways));
        results.AddIf(Widgets.Bullets("Questions they'll have", r.Questions));
        if (r.Confusions.Count > 0)
        {
            var head = UiKit.Label("Possible confusion", 13, FontWeights.SemiBold);
            head.Margin = new Thickness(0, 10, 0, 4);
            results.Children.Add(head);
            foreach (var c in r.Confusions)
            {
                var s = new StackPanel();
                if (c.Quote.Length > 0)
                {
                    var q = UiKit.Label($"\u201C{c.Quote}\u201D", 12.5, null, Theme.Text, wrap: true);
                    q.FontStyle = FontStyles.Italic;
                    s.Children.Add(q);
                }
                s.Children.Add(UiKit.Label(c.Issue, 12.5, null, Theme.Text, wrap: true));
                if (c.Suggestion.Length > 0) s.Children.Add(UiKit.Label($"→ {c.Suggestion}", 12.5, null, Theme.BrandText, wrap: true));
                results.Children.Add(Widgets.Card(s, Scales.Brush(Level.Warn)));
            }
        }
        results.AddIf(Widgets.Bullets("How to land it better", r.Suggestions));
    }

    public override void OnNewField()
    {
        runner.Reset();
        results.Children.Clear();
    }
}
