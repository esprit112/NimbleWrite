using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>Every suggestion for the field, with Fix all. Cards are kept by issue, so a change rebuilds only what changed.</summary>
internal sealed class ReviewTab : PanelTab
{
    const int MaxCards = 60;
    readonly Dictionary<string, FrameworkElement> cards = new();
    TextBlock status = null!;
    ContentControl fixAllHost = null!;
    StackPanel list = null!;
    ScrollViewer listScroll = null!;
    string listKey = "\0";

    public ReviewTab(IPanelActions host) : base(host)
    {
    }

    public override string Id => "review";
    public override string Label => "Review suggestions";
    public override string Glyph => Icons.Review;
    public override bool ScrollsItself => true;

    /// <summary>The cards on show, in order.</summary>
    internal IEnumerable<FrameworkElement> Cards => list.Children.OfType<FrameworkElement>();

    protected override FrameworkElement Build()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        status = UiKit.Label("", 12.5, null, Theme.Muted, wrap: true);
        status.Margin = new Thickness(0, 0, 0, 8);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        grid.Children.Add(status);
        fixAllHost = new ContentControl { Focusable = false };
        Grid.SetRow(fixAllHost, 1);
        grid.Children.Add(fixAllHost);
        list = new StackPanel();
        listScroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Padding = new Thickness(0, 0, 4, 0) };
        Grid.SetRow(listScroll, 2);
        grid.Children.Add(listScroll);
        return grid;
    }

    public override void OnShown(EngineSnapshot? s)
    {
        if (s != null) OnState(s);
    }

    public override void OnState(EngineSnapshot s)
    {
        if (s.Target == null) return;
        _ = View;
        int n = s.Issues.Count;
        status.Text = s.State switch
        {
            CheckState.Checking => n > 0 ? $"Checking… {Plural(n, "suggestion")} so far." : "Checking your writing…",
            CheckState.Offline => ServerLauncher.Packaged
                ? "Can't reach NimbleWrite's checker. It restarts by itself; if this keeps happening, quit and reopen NimbleWrite."
                : "Can't reach the NimbleWrite server. Run npm start in the NimbleWrite folder.",
            CheckState.TooLong => s.Message ?? "This text is too long for live checking.",
            CheckState.Private => s.Message ?? "This looks like it contains passwords or keys, so it isn't checked.",
            CheckState.Error => (s.Message ?? "Something went wrong.") + (n > 0 ? $" {Plural(n, "suggestion")}." : ""),
            _ => s.WordCount < TextModel.MinWords ? "Start writing: suggestions appear as you type." : n == 0 ? "✓ Looks good! No issues found." : $"{Plural(n, "suggestion")}",
        };
        if (s.AiOff && s.State == CheckState.Idle)
        {
            status.Text += "\nBuilt-in checks only. For AI suggestions, choose Connect to NVIDIA in the NimbleWrite tray menu.";
        }

        var shown = s.Issues.Take(MaxCards).ToList();
        string key = string.Join("|", shown.Select(Key));
        if (key == listKey) return;
        listKey = key;
        var keep = new HashSet<string>(shown.Select(Key));
        foreach (var stale in cards.Keys.Where(k => !keep.Contains(k)).ToList()) cards.Remove(stale);
        list.Children.Clear();
        foreach (var p in shown)
        {
            string k = Key(p);
            if (!cards.TryGetValue(k, out var card)) cards[k] = card = IssueCard(p);
            list.Children.Add(card);
        }
        listScroll.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        var fixable = s.Issues.Where(i => i.Issue.Category == "correctness" && i.Issue.Replacements.Count > 0).Select(i => i.Id).ToList();
        fixAllHost.Content = fixable.Count >= 2
            ? UiKit.Spaced(UiKit.Button($"Fix all spelling & grammar ({fixable.Count})", () => Host.AcceptAll(fixable), Kind.Primary), 0, 10)
            : null;
    }

    /// <summary>The same issue with different fixes (say, the AI's after the built-in check's) needs a new card.</summary>
    static string Key(PlacedIssue p) => p.Id + "\n" + p.Signature;

    static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    FrameworkElement IssueCard(PlacedIssue p)
    {
        var i = p.Issue;
        var body = new StackPanel { Margin = new Thickness(11, 9, 10, 9) };
        var head = new DockPanel { LastChildFill = true };
        var cat = UiKit.Label(Theme.CategoryLabel(i.Category), 11, FontWeights.SemiBold, Theme.CategoryBrush(i.Category));
        cat.Margin = new Thickness(8, 1, 0, 0);
        DockPanel.SetDock(cat, Dock.Right);
        head.Children.Add(cat);
        head.Children.Add(UiKit.Label(string.IsNullOrWhiteSpace(i.Title) ? "Suggestion" : i.Title, 13, FontWeights.SemiBold));
        body.Children.Add(head);

        var change = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, FontFamily = UiKit.Font, Margin = new Thickness(0, 5, 0, 0) };
        change.Inlines.Add(new Run(UiKit.Display(i.Original)) { Foreground = Theme.Muted, TextDecorations = TextDecorations.Strikethrough });
        if (i.Replacements.Count > 0)
        {
            change.Inlines.Add(new Run("  →  ") { Foreground = Theme.Muted });
            string r = i.Replacements[0];
            change.Inlines.Add(new Run(r.Trim().Length == 0 ? "(remove)" : UiKit.Display(r)) { Foreground = Theme.BrandText, FontWeight = FontWeights.SemiBold });
        }
        body.Children.Add(change);
        if (!string.IsNullOrWhiteSpace(i.Explanation))
        {
            var ex = UiKit.Label(i.Explanation, 12, null, Theme.Muted, wrap: true);
            ex.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(ex);
        }
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (i.Replacements.Count > 0) actions.Children.Add(UiKit.Spaced(UiKit.Button("Accept", () => Host.Accept(p.Id, 0), Kind.Primary), 6, 0));
        actions.Children.Add(UiKit.Spaced(UiKit.Button("Dismiss", () => Host.Dismiss(p.Id)), 6, 0));
        if (i.Type is "spelling" or "dialect") actions.Children.Add(UiKit.Button("Add to dictionary", () => Host.AddWord(p.Id), Kind.Ghost));
        body.Children.Add(actions);

        var bar = new Border { Width = 3, Background = Theme.CategoryBrush(i.Category), CornerRadius = new CornerRadius(8, 0, 0, 8) };
        DockPanel.SetDock(bar, Dock.Left);
        var dock = new DockPanel { LastChildFill = true };
        dock.Children.Add(bar);
        dock.Children.Add(body);
        var card = new Border
        {
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = Theme.Surface,
            Margin = new Thickness(0, 0, 0, 8),
            Child = dock,
        };
        card.MouseEnter += (_, _) => Host.SetHighlight(p.Id);
        card.MouseLeave += (_, _) => Host.SetHighlight(null);
        return card;
    }
}
