using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI;

/// <summary>The suggestion card that appears when you hover an underline (also used for short notices).</summary>
internal sealed class CardWindow : NoActivateWindow
{
    readonly UiController ui;
    readonly Border surface;
    readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };

    public string? IssueId { get; private set; }
    public bool IsToast { get; private set; }

    public CardWindow(UiController ui) : base(clickThrough: false)
    {
        this.ui = ui;
        SizeToContent = SizeToContent.WidthAndHeight;
        surface = UiKit.Surface(new Grid(), 10);
        Content = surface;
        toastTimer.Tick += (_, _) =>
        {
            toastTimer.Stop();
            if (IsToast) Close2();
        };
    }

    public void ShowIssue(PlacedIssue p, Rect anchor)
    {
        IssueId = p.Id;
        IsToast = false;
        toastTimer.Stop();
        var i = p.Issue;
        var root = new StackPanel { Width = 304, Margin = new Thickness(14, 12, 14, 10) };

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(UiKit.Dot(Theme.CategoryBrush(i.Category)));
        var cat = UiKit.Label(Theme.CategoryLabel(i.Category).ToUpperInvariant(), 10.5, FontWeights.SemiBold, Theme.Muted);
        cat.Margin = new Thickness(7, 0, 0, 0);
        head.Children.Add(cat);
        root.Children.Add(head);

        var title = UiKit.Label(string.IsNullOrWhiteSpace(i.Title) ? "Suggestion" : i.Title, 14, FontWeights.SemiBold, wrap: true);
        title.Margin = new Thickness(0, 5, 0, 9);
        root.Children.Add(title);

        var row = new WrapPanel();
        var orig = UiKit.Label(UiKit.Display(i.Original), 13, null, Theme.Muted, wrap: true);
        orig.TextDecorations = TextDecorations.Strikethrough;
        orig.VerticalAlignment = VerticalAlignment.Center;
        orig.MaxWidth = 290;
        orig.Margin = new Thickness(0, 0, 9, 6);
        row.Children.Add(orig);
        for (int k = 0; k < Math.Min(3, i.Replacements.Count); k++)
        {
            int idx = k;
            string r = i.Replacements[k];
            var b = UiKit.Button(r.Trim().Length == 0 ? "Remove it" : UiKit.Display(r), () => ui.Accept(p.Id, idx), Kind.Accept, "Accept");
            b.MaxWidth = 290;
            row.Children.Add(UiKit.Spaced(b));
        }
        root.Children.Add(row);

        if (!string.IsNullOrWhiteSpace(i.Explanation))
        {
            var ex = UiKit.Label(i.Explanation, 12.5, null, Theme.Muted, wrap: true);
            ex.Margin = new Thickness(0, 2, 0, 0);
            root.Children.Add(ex);
        }
        root.Children.Add(UiKit.Divider(10, 6));
        var actions = new WrapPanel();
        actions.Children.Add(UiKit.Button("Dismiss", () => ui.Dismiss(p.Id), Kind.Ghost));
        if (i.Type is "spelling" or "dialect") actions.Children.Add(UiKit.Button("Add to dictionary", () => ui.AddWord(p.Id), Kind.Ghost));
        actions.Children.Add(UiKit.Button("Open assistant", ui.OpenPanelFromCard, Kind.Ghost));
        root.Children.Add(actions);

        surface.Child = root;
        ShowHidden();
        PlaceNear(anchor);
    }

    public void ShowToast(string text, Rect anchor)
    {
        IssueId = null;
        IsToast = true;
        var root = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 9, 14, 9) };
        var logo = UiKit.Logo(18);
        logo.Margin = new Thickness(0, 0, 9, 0);
        logo.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(logo);
        var label = UiKit.Label(text, 12.5, null, Theme.Text, wrap: true);
        label.MaxWidth = 290;
        label.VerticalAlignment = VerticalAlignment.Center;
        root.Children.Add(label);
        surface.Child = root;
        ShowHidden();
        PlaceNear(anchor, alignRight: true);
        toastTimer.Stop();
        toastTimer.Start();
    }

    public void Close2()
    {
        IssueId = null;
        IsToast = false;
        toastTimer.Stop();
        Hide();
    }
}
