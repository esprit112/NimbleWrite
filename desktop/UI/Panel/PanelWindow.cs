using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>
/// The assistant panel beside the field: a rail of tools on the left (the web editor's tools, in the same order) and
/// the active tool on the right. Tabs are built the first time they're opened.
/// </summary>
internal sealed class PanelWindow : NoActivateWindow
{
    public const double PanelWidth = 420;
    public const double PanelMaxHeight = 620;
    const double RailWidth = 56;
    const double ContentLeft = 14, ContentRight = 12;

    /// <summary>The width a tab's view gets: the panel minus its border, the rail and the content padding.</summary>
    public const double ContentWidth = PanelWidth - 2 - RailWidth - 1 - ContentLeft - ContentRight;

    readonly IPanelActions host;
    readonly TextBlock title;
    readonly TextBlock subtitle;
    readonly TextBlock notice;
    readonly Border turnOff;
    readonly Dictionary<string, (Border Fill, Rectangle Bar, TextBlock Glyph)> rail = new();
    readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    EngineSnapshot? last;
    Rect anchorField;

    public PanelWindow(IPanelActions host, string? lastTab) : base(clickThrough: false)
    {
        this.host = host;
        Tabs = new TabSet(CreateTabs(host), lastTab);
        Width = PanelWidth + 2 * UiKit.ShadowMargin;
        Height = PanelMaxHeight + 2 * UiKit.ShadowMargin;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // line
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // rail + tab
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // notice
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // footer

        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(14, 11, 8, 10) };
        var close = UiKit.IconButton(Icons.Close, host.ClosePanel, "Close", 12);
        close.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var gear = UiKit.IconButton(Icons.Settings, host.OpenWritingSettings, "Writing settings", 14);
        gear.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(gear, Dock.Right);
        header.Children.Add(gear);
        var logo = UiKit.Logo(26);
        logo.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(logo, Dock.Left);
        header.Children.Add(logo);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title = UiKit.Label(Tabs.Active.Label, 14.5, FontWeights.SemiBold);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        titles.Children.Add(title);
        subtitle = UiKit.Label("NimbleWrite", 12, null, Theme.Muted);
        titles.Children.Add(subtitle);
        header.Children.Add(titles);
        root.Children.Add(header);

        var line = new Border { Height = 1, Background = Theme.Line };
        Grid.SetRow(line, 1);
        root.Children.Add(line);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RailWidth) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var buttons = new StackPanel { Margin = new Thickness(8, 8, 8, 8) };
        foreach (var t in Tabs.All) buttons.Children.Add(RailButton(t));
        var railScroll = new ScrollViewer { Content = buttons, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
        AutomationProperties.SetName(railScroll, "Tools");
        body.Children.Add(railScroll);
        var railLine = new Border { Background = Theme.Line };
        Grid.SetColumn(railLine, 1);
        body.Children.Add(railLine);
        ContentHost = new ContentControl { Width = ContentWidth, Margin = new Thickness(ContentLeft, 12, ContentRight, 8), Focusable = false, HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetColumn(ContentHost, 2);
        body.Children.Add(ContentHost);
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        notice = UiKit.Label("", 12.5, FontWeights.SemiBold, Theme.BrandText, wrap: true);
        notice.Visibility = Visibility.Collapsed;
        notice.Margin = new Thickness(14, 6, 12, 2);
        AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);
        Grid.SetRow(notice, 3);
        root.Children.Add(notice);

        var footer = new WrapPanel { Margin = new Thickness(8, 6, 8, 8) };
        footer.Children.Add(UiKit.Button("Open in NimbleWrite", host.OpenEditor, Kind.Ghost));
        footer.Children.Add(UiKit.Button("Pause 1 hour", host.PauseHour, Kind.Ghost));
        turnOff = UiKit.Button("Turn off here", host.TurnOffInCurrentApp, Kind.Ghost);
        footer.Children.Add(turnOff);
        var footerBox = new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        Grid.SetRow(footerBox, 4);
        root.Children.Add(footerBox);

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

    /// <summary>The tools, in the web editor's order (AgentPanels.tsx PANELS, then the editor's extras).</summary>
    internal static IReadOnlyList<PanelTab> CreateTabs(IPanelActions host) => new PanelTab[]
    {
        new ReviewTab(host),
        new AssistantTab(host),
        new RewriteTab(host, RewriteKind.Paraphrase),
        new RewriteTab(host, RewriteKind.Humanize),
        new ReaderTab(host),
        new GraderTab(host),
        new CitationsTab(host),
        new DetectorTab(host),
        new PlagiarismTab(host),
        new RewriteTab(host, RewriteKind.Translate),
        new ImagePromptTab(host),
        new SynonymsTab(host),
        new PerformanceTab(host),
        new DocumentsTab(host),
    };

    /// <summary>Open the Humanizer on one passage (from the AI Detector) and start it.</summary>
    public void Humanize(TextScope passage)
    {
        ShowTab("humanize", last);
        if (Tabs.Active is RewriteTab t) t.StartOn(passage);
    }

    internal TabSet Tabs { get; }

    /// <summary>Where the active tab's view goes; exactly <see cref="ContentWidth"/> wide.</summary>
    internal ContentControl ContentHost { get; }

    public void Open(EngineSnapshot s, Rect field, string? tab = null)
    {
        Update(s);
        ShowTab(tab ?? Tabs.Active.Id, s, force: true);
        ShowHidden();
        Reposition(field);
    }

    /// <summary>Bring a tab to the front (building it the first time).</summary>
    public void ShowTab(string id, EngineSnapshot? s = null) => ShowTab(id, s, force: false);

    void ShowTab(string id, EngineSnapshot? s, bool force)
    {
        bool changed = Tabs.Select(id);
        if (!changed && !force && ContentHost.Content != null) return;
        var t = Tabs.Active;
        ContentHost.Content = t.Hosted;
        title.Text = t.Label;
        foreach (var (tabId, parts) in rail) SetRailState(parts, tabId == t.Id);
        t.OnShown(s ?? last);
        if (changed) host.TabChanged(t.Id);
    }

    public void ResetForNewField()
    {
        foreach (var t in Tabs.All)
        {
            if (t.IsBuilt) t.OnNewField();
        }
        notice.Visibility = Visibility.Collapsed;
    }

    public void Update(EngineSnapshot s)
    {
        if (s.Target == null) return;
        last = s;
        subtitle.Text = $"in {s.Target.AppName}";
        ((TextBlock)turnOff.Child).Text = $"Turn off in {Trim(s.Target.AppName, 22)}";
        if (Tabs.Active.IsBuilt) Tabs.Active.OnState(s);
    }

    static string Trim(string s, int max) => s.Length > max ? s[..max] + "…" : s;

    Border RailButton(PanelTab t)
    {
        var fill = new Border { CornerRadius = new CornerRadius(8) };
        var bar = new Rectangle { Width = 3, Height = 18, RadiusX = 1.5, RadiusY = 1.5, Fill = Theme.Brand, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-6, 0, 0, 0) };
        var glyph = UiKit.Glyph(t.Glyph, 17);
        var face = new Grid { Width = 40, Height = 34 };
        face.Children.Add(fill);
        face.Children.Add(bar);
        face.Children.Add(glyph);
        var b = UiKit.Clickable(face, () => ShowTab(t.Id), Kind.Ghost, t.Label);
        b.Padding = new Thickness(0);
        b.Margin = new Thickness(0, 0, 0, 2); // 14 tools fit the rail without scrolling at the full panel height
        rail[t.Id] = (fill, bar, glyph);
        SetRailState(rail[t.Id], t.Id == Tabs.Active.Id);
        return b;
    }

    static void SetRailState((Border Fill, Rectangle Bar, TextBlock Glyph) parts, bool active)
    {
        parts.Fill.Background = active ? Theme.Subtle : Brushes.Transparent;
        parts.Bar.Visibility = active ? Visibility.Visible : Visibility.Hidden;
        parts.Glyph.Foreground = active ? Theme.BrandText : Theme.Muted;
        AutomationProperties.SetItemStatus(parts.Fill, active ? "Selected" : "");
    }

    public void ShowNotice(string text)
    {
        notice.Text = text;
        notice.Visibility = Visibility.Visible;
        noticeTimer.Stop();
        noticeTimer.Start();
    }

    /// <summary>
    /// Beside the field (right, else left, else overlapping), bottom-aligned with it, inside the work area. The height is
    /// fixed (so switching tabs doesn't make it jump) but never taller than the screen allows.
    /// </summary>
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
        double s = Native.ScaleAt(field.Right - 1, field.Bottom - 1);
        var wa = Native.WorkAreaAt(field.Right - 1, field.Bottom - 1);
        double want = Math.Round(Math.Min(PanelMaxHeight, (wa.Bottom - wa.Top) / s - 24) + 2 * UiKit.ShadowMargin);
        if (Math.Abs(Height - want) > 0.5) Height = want;
        UpdateLayout();
        double m = UiKit.ShadowMargin * s, w = ActualWidth * s, h = ActualHeight * s, gap = 8 * s;
        double x = field.Right + gap - m;
        if (x + w - m > wa.Right) x = field.Left - gap - w + m;
        if (x + m < wa.Left) x = wa.Right - w + m - gap;
        double y = field.Bottom - h + m;
        y = Math.Min(y, wa.Bottom - h + m);
        y = Math.Max(y, wa.Top - m);
        MoveTo(x, y);
        Opacity = 1;
    }

    /// <summary>Lay the panel out off-screen and draw it, for tests and design checks.</summary>
    internal BitmapSource RenderForTest(EngineSnapshot s)
    {
        Update(s);
        var root = (FrameworkElement)Content;
        var size = new Size(Width, Height);
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        bmp.Freeze();
        return bmp;
    }
}
