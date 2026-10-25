using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI;

/// <summary>The assistant panel beside the field: all suggestions, fix-all, AI rewrites and tone.</summary>
internal sealed class PanelWindow : NoActivateWindow
{
    const double PanelWidth = 360;
    readonly UiController ui;
    readonly TextBlock subtitle;
    readonly TextBlock status;
    readonly TextBlock notice;
    readonly StackPanel list = new();
    readonly ScrollViewer listScroll;
    readonly ContentControl fixAllHost = new();
    readonly TextBlock aiStatus;
    readonly Border aiBox;
    readonly TextBlock aiText;
    readonly StackPanel aiActions;
    readonly TextBlock toneText;
    readonly Border turnOff;
    readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    string listKey = "\0";
    AiUpdate? ai;
    int aiSeq = -1;
    Rect anchorField;

    public PanelWindow(UiController ui) : base(clickThrough: false)
    {
        this.ui = ui;
        SizeToContent = SizeToContent.Height;
        Width = PanelWidth + 2 * UiKit.ShadowMargin;
        var root = new StackPanel { Margin = new Thickness(16, 14, 14, 12) };

        var header = new DockPanel { LastChildFill = true };
        var close = UiKit.Button("✕", ui.ClosePanel, Kind.Ghost, "Close");
        close.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var logo = UiKit.Logo(28);
        logo.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(logo, Dock.Left);
        header.Children.Add(logo);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(UiKit.Label("NimbleWrite", 14.5, FontWeights.SemiBold));
        subtitle = UiKit.Label("", 12, null, Theme.Muted);
        titles.Children.Add(subtitle);
        header.Children.Add(titles);
        root.Children.Add(header);

        status = UiKit.Label("", 12.5, null, Theme.Muted, wrap: true);
        status.Margin = new Thickness(0, 12, 0, 8);
        root.Children.Add(status);
        root.Children.Add(fixAllHost);
        listScroll = new ScrollViewer { Content = list, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Padding = new Thickness(0, 0, 4, 0) };
        root.Children.Add(listScroll);

        root.Children.Add(UiKit.Divider(6, 10));
        root.Children.Add(UiKit.Label("✨ Rewrite with AI", 13, FontWeights.SemiBold));
        var hint = UiKit.Label("Works on your selection, or on all the text if nothing is selected.", 12, null, Theme.Muted, wrap: true);
        hint.Margin = new Thickness(0, 2, 0, 8);
        root.Children.Add(hint);
        var chips = new WrapPanel();
        foreach (var (label, task) in new[] { ("Improve", "improve"), ("Fix grammar", "fix_grammar"), ("Shorten", "shorten"), ("More formal", "professional"), ("Friendlier", "friendly"), ("Simplify", "simplify") })
        {
            chips.Children.Add(UiKit.Spaced(UiKit.Button(label, () => StartRewrite(task))));
        }
        root.Children.Add(chips);
        aiStatus = UiKit.Label("", 12, null, Theme.Muted, wrap: true);
        aiStatus.Visibility = Visibility.Collapsed;
        aiStatus.Margin = new Thickness(0, 2, 0, 4);
        root.Children.Add(aiStatus);
        aiText = UiKit.Label("", 13, null, Theme.Text, wrap: true);
        aiBox = new Border
        {
            Background = Theme.Raised,
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 2, 0, 8),
            Child = new ScrollViewer { Content = aiText, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false },
            Visibility = Visibility.Collapsed,
        };
        root.Children.Add(aiBox);
        aiActions = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
        aiActions.Children.Add(UiKit.Spaced(UiKit.Button("Replace", ApplyAi, Kind.Primary)));
        aiActions.Children.Add(UiKit.Spaced(UiKit.Button("Copy", CopyAi)));
        aiActions.Children.Add(UiKit.Spaced(UiKit.Button("Discard", DiscardAi, Kind.Ghost)));
        root.Children.Add(aiActions);

        root.Children.Add(UiKit.Divider(6, 10));
        var toneRow = new DockPanel { LastChildFill = true };
        var detect = UiKit.Button("Detect tone", DetectTone);
        detect.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(detect, Dock.Right);
        toneRow.Children.Add(detect);
        toneText = UiKit.Label("How does this sound to a reader?", 12.5, null, Theme.Muted, wrap: true);
        toneText.VerticalAlignment = VerticalAlignment.Center;
        toneText.Margin = new Thickness(0, 0, 8, 0);
        toneRow.Children.Add(toneText);
        root.Children.Add(toneRow);

        root.Children.Add(UiKit.Divider(10, 6));
        notice = UiKit.Label("", 12.5, FontWeights.SemiBold, Theme.BrandText, wrap: true);
        notice.Visibility = Visibility.Collapsed;
        notice.Margin = new Thickness(0, 0, 0, 6);
        root.Children.Add(notice);
        var footer = new WrapPanel();
        footer.Children.Add(UiKit.Button("Open in NimbleWrite", ui.OpenEditor, Kind.Ghost));
        footer.Children.Add(UiKit.Button("Pause 1 hour", ui.PauseHour, Kind.Ghost));
        turnOff = UiKit.Button("Turn off here", ui.TurnOffInCurrentApp, Kind.Ghost);
        footer.Children.Add(turnOff);
        root.Children.Add(footer);

        Content = UiKit.Surface(root, 12);
        SizeChanged += (_, _) =>
        {
            if (IsVisible) Reposition(anchorField);
        };
        noticeTimer.Tick += (_, _) =>
        {
            noticeTimer.Stop();
            notice.Visibility = Visibility.Collapsed;
        };
    }

    public void Open(EngineSnapshot s, Rect field)
    {
        listKey = "\0";
        Update(s);
        ShowHidden();
        Reposition(field);
    }

    public void ResetForNewField()
    {
        DiscardAi();
        toneText.Text = "How does this sound to a reader?";
        notice.Visibility = Visibility.Collapsed;
    }

    public void Update(EngineSnapshot s)
    {
        if (s.Target == null) return;
        int n = s.Issues.Count;
        subtitle.Text = $"in {s.Target.AppName}";
        ((TextBlock)turnOff.Child).Text = $"Turn off in {Trim(s.Target.AppName, 22)}";
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
        string key = string.Join("|", s.Issues.Select(i => i.Id));
        if (key == listKey) return;
        listKey = key;
        list.Children.Clear();
        foreach (var i in s.Issues.Take(60)) list.Children.Add(IssueCard(i));
        listScroll.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        var fixable = s.Issues.Where(i => i.Issue.Category == "correctness" && i.Issue.Replacements.Count > 0).Select(i => i.Id).ToList();
        fixAllHost.Content = fixable.Count >= 2
            ? UiKit.Spaced(UiKit.Button($"Fix all spelling & grammar ({fixable.Count})", () => ui.AcceptAll(fixable), Kind.Primary), 0, 10)
            : null;
    }

    static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    static string Trim(string s, int max) => s.Length > max ? s[..max] + "…" : s;

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
        if (i.Replacements.Count > 0) actions.Children.Add(UiKit.Spaced(UiKit.Button("Accept", () => ui.Accept(p.Id, 0), Kind.Primary), 6, 0));
        actions.Children.Add(UiKit.Spaced(UiKit.Button("Dismiss", () => ui.Dismiss(p.Id)), 6, 0));
        if (i.Type is "spelling" or "dialect") actions.Children.Add(UiKit.Button("Add to dictionary", () => ui.AddWord(p.Id), Kind.Ghost));
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
        card.MouseEnter += (_, _) => ui.SetHighlight(p.Id);
        card.MouseLeave += (_, _) => ui.SetHighlight(null);
        return card;
    }

    void StartRewrite(string task)
    {
        aiStatus.Text = "Thinking…";
        aiStatus.Visibility = Visibility.Visible;
        aiBox.Visibility = Visibility.Collapsed;
        aiActions.Visibility = Visibility.Collapsed;
        ui.Rewrite(task);
    }

    public void OnRewrite(AiUpdate u)
    {
        if (u.Seq < aiSeq) return;
        aiSeq = u.Seq;
        ai = u;
        aiStatus.Visibility = Visibility.Visible;
        if (u.Error != null)
        {
            aiStatus.Text = u.Error;
            aiBox.Visibility = Visibility.Collapsed;
            aiActions.Visibility = Visibility.Collapsed;
            return;
        }
        string what = u.Scope?.Whole == false ? "your selection" : "all the text";
        aiStatus.Text = u.Done ? $"Rewrite of {what}:" : u.Text.Length == 0 ? "Thinking…" : $"Rewriting {what}…";
        aiText.Text = u.Text;
        aiBox.Visibility = u.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        aiActions.Visibility = u.Done && u.Text.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void ApplyAi()
    {
        if (ai is not { Done: true, Error: null, Scope: { } scope } || ai.Text.Trim().Length == 0) return;
        ui.ApplyRewrite(scope, ai.Text);
        DiscardAi();
    }

    void CopyAi()
    {
        if (ai?.Text is not { Length: > 0 } t) return;
        try
        {
            Clipboard.SetText(t);
            ShowNotice("Copied to the clipboard.");
        }
        catch (Exception)
        {
            ShowNotice("Couldn't use the clipboard just now.");
        }
    }

    void DiscardAi()
    {
        if (ai is { Done: false }) ui.CancelRewrite();
        ai = null;
        aiStatus.Visibility = Visibility.Collapsed;
        aiBox.Visibility = Visibility.Collapsed;
        aiActions.Visibility = Visibility.Collapsed;
    }

    void DetectTone()
    {
        toneText.Text = "Listening to your text…";
        ui.DetectTone();
    }

    public void OnTone(ToneResult? t, string? error)
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

    public void ShowNotice(string text)
    {
        notice.Text = text;
        notice.Visibility = Visibility.Visible;
        noticeTimer.Stop();
        noticeTimer.Start();
    }

    /// <summary>Beside the field (right, else left, else overlapping), bottom-aligned with it, inside the work area.</summary>
    public void Reposition(Rect field)
    {
        anchorField = field;
        // Visibility, not IsVisible: IsVisible can lag right after Show(), which would leave the panel at opacity 0.
        if (Visibility != Visibility.Visible) return;
        if (field.IsEmpty || field.Width < 1 || field.Height < 1)
        {
            // Some fields report no rectangle: open by the mouse pointer rather than staying invisible.
            Native.GetCursorPos(out var p);
            field = new Rect(p.X, p.Y, 1, 1);
        }
        UpdateLayout();
        double s = Native.ScaleAt(field.Right - 1, field.Bottom - 1);
        double m = UiKit.ShadowMargin * s, w = ActualWidth * s, h = ActualHeight * s, gap = 8 * s;
        var wa = Native.WorkAreaAt(field.Right - 1, field.Bottom - 1);
        double x = field.Right + gap - m;
        if (x + w - m > wa.Right) x = field.Left - gap - w + m;
        if (x + m < wa.Left) x = wa.Right - w + m - gap;
        double y = field.Bottom - h + m;
        y = Math.Min(y, wa.Bottom - h + m);
        y = Math.Max(y, wa.Top - m);
        MoveTo(x, y);
        Opacity = 1;
    }
}
