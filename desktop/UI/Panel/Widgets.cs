using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

internal enum Level { Good, Warn, Bad }

/// <summary>The web editor's green / amber / red thresholds for each tool's numbers.</summary>
internal static class Scales
{
    /// <summary>AI Grader score (and criteria): higher is better.</summary>
    public static Level Grade(double score) => score >= 75 ? Level.Good : score >= 55 ? Level.Warn : Level.Bad;

    /// <summary>AI Detector: how AI-like the text reads; higher is worse.</summary>
    public static Level AiLikely(double score) => score >= 60 ? Level.Bad : score >= 35 ? Level.Warn : Level.Good;

    /// <summary>Plagiarism: share of the text matching a source; higher is worse.</summary>
    public static Level Matches(double pct) => pct >= 30 ? Level.Bad : pct >= 10 ? Level.Warn : Level.Good;

    /// <summary>The web's colours, a little lighter in dark mode so they stay readable.</summary>
    public static Brush Brush(Level l) => (l, Theme.Dark) switch
    {
        (Level.Good, false) => Theme.B("#18A058"),
        (Level.Warn, false) => Theme.B("#B86304"),
        (Level.Bad, false) => Theme.B("#D63B40"),
        (Level.Good, true) => Theme.B("#3CC27E"),
        (Level.Warn, true) => Theme.B("#F0A33A"),
        _ => Theme.B("#FF6B6F"),
    };
}

/// <summary>Result building blocks shared by the tool tabs.</summary>
internal static class Widgets
{
    /// <summary>A titled bullet list, or null when there's nothing to list.</summary>
    public static FrameworkElement? Bullets(string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return null;
        var box = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        box.Children.Add(UiKit.Label(title, 13, FontWeights.SemiBold));
        foreach (var item in items)
        {
            var row = new DockPanel { Margin = new Thickness(2, 3, 0, 0) };
            var dot = UiKit.Label("•", 13, null, Theme.Muted);
            dot.Margin = new Thickness(0, 0, 7, 0);
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(UiKit.Label(item, 12.5, null, Theme.Text, wrap: true));
            box.Children.Add(row);
        }
        return box;
    }

    /// <summary>A rounded box around a result.</summary>
    public static Border Card(UIElement child, Brush? border = null, Brush? background = null, double bottom = 8) => new()
    {
        BorderBrush = border ?? Theme.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Background = background ?? Theme.Surface,
        Padding = new Thickness(10, 8, 10, 9),
        Margin = new Thickness(0, 0, 0, bottom),
        Child = child,
    };

    /// <summary>A thin bar filled to <paramref name="percent"/>.</summary>
    public static FrameworkElement Bar(double percent, Brush fill)
    {
        var track = new Grid { Height = 6, Margin = new Thickness(0, 3, 0, 3) };
        track.Children.Add(new Border { Background = Theme.Subtle, CornerRadius = new CornerRadius(3) });
        double p = Math.Clamp(percent, 0, 100) / 100;
        var cols = new Grid();
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(p, GridUnitType.Star) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - p, GridUnitType.Star) });
        cols.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(3) });
        track.Children.Add(cols);
        AutomationProperties.SetName(track, $"{Math.Round(percent)} percent");
        return track;
    }

    /// <summary>A big number with a caption under it, centred in a tinted box (detector %, plagiarism %).</summary>
    public static FrameworkElement Headline(string big, Brush brush, string caption, string? detail = null)
    {
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var n = UiKit.Label(big, 30, FontWeights.Bold, brush);
        n.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(n);
        var c = UiKit.Label(caption, 12.5, null, Theme.Muted, wrap: true);
        c.TextAlignment = TextAlignment.Center;
        s.Children.Add(c);
        if (detail != null)
        {
            var d = UiKit.Label(detail, 13, FontWeights.SemiBold, Theme.Text, wrap: true);
            d.TextAlignment = TextAlignment.Center;
            d.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(d);
        }
        return Card(s, Theme.Line, Theme.Raised, 10);
    }

    /// <summary>"Label ........ value" on one line, the value right-aligned and allowed to wrap.</summary>
    public static FrameworkElement Pair(string label, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(UiKit.Label(label, 12, null, Theme.Muted, wrap: true));
        var v = UiKit.Label(value, 12, null, Theme.Text, wrap: true);
        v.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        return g;
    }

    /// <summary>A small print line (disclaimers).</summary>
    public static TextBlock Fine(string text)
    {
        var t = UiKit.Label(text, 11, null, Theme.Muted, wrap: true);
        t.Margin = new Thickness(0, 10, 0, 0);
        return t;
    }

    public static void AddIf(this Panel panel, UIElement? child)
    {
        if (child != null) panel.Children.Add(child);
    }
}

/// <summary>An on/off row that works without taking focus (a WPF CheckBox's template ignores the dark theme).</summary>
internal sealed class ToggleRow : Border
{
    readonly TextBlock box;
    readonly TextBlock label;
    bool on;
    bool enabled = true;

    public ToggleRow(string text, bool on, Action<bool>? changed = null)
    {
        this.on = on;
        box = UiKit.Glyph("", 15);
        box.Margin = new Thickness(0, 0, 8, 0);
        label = UiKit.Label(text, 12.5, null, Theme.Text, wrap: true);
        var row = new DockPanel();
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(box);
        row.Children.Add(label);
        var click = UiKit.Clickable(row, () =>
        {
            if (!enabled) return;
            On = !On;
            changed?.Invoke(On);
        }, Kind.Ghost, text);
        click.HorizontalAlignment = HorizontalAlignment.Left;
        click.Padding = new Thickness(2, 3, 6, 3);
        Child = click;
        Render();
    }

    public bool On
    {
        get => on;
        set
        {
            on = value;
            Render();
        }
    }

    public bool Enabled
    {
        get => enabled;
        set
        {
            enabled = value;
            Render();
        }
    }

    void Render()
    {
        box.Text = on ? "\uE73A" : "\uE739"; // CheckboxComposite / Checkbox
        box.Foreground = !enabled ? Theme.Line : on ? Theme.BrandText : Theme.Muted;
        label.Foreground = enabled ? Theme.Text : Theme.Muted;
        AutomationProperties.SetItemStatus(Child, on ? "On" : "Off");
    }
}
