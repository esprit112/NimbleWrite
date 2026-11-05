using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>
/// One tool in the assistant panel's rail. Its view is built the first time the tab is shown, never at startup, so
/// adding tools doesn't add start-up time or memory for tools nobody opens.
/// </summary>
internal abstract class PanelTab
{
    FrameworkElement? view;
    FrameworkElement? hosted;

    protected PanelTab(IPanelActions host) => Host = host;

    protected IPanelActions Host { get; }

    public abstract string Id { get; }

    /// <summary>The name in the header and the rail button's tooltip.</summary>
    public abstract string Label { get; }

    public abstract string Glyph { get; }

    /// <summary>True when the view has its own scrolling (a list); otherwise the panel wraps it in a scroll viewer.</summary>
    public virtual bool ScrollsItself => false;

    public bool IsBuilt => view != null;

    public FrameworkElement View => view ??= Build();

    /// <summary>The view as the panel shows it (inside a scroll viewer unless it scrolls itself).</summary>
    internal FrameworkElement Hosted => hosted ??= ScrollsItself
        ? View
        : new ScrollViewer { Content = View, Padding = new Thickness(0, 0, 6, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };

    protected abstract FrameworkElement Build();

    /// <summary>The tab was brought to the front. <paramref name="s"/> is the latest snapshot, if any.</summary>
    public virtual void OnShown(EngineSnapshot? s)
    {
    }

    /// <summary>A new snapshot while this tab is in front.</summary>
    public virtual void OnState(EngineSnapshot s)
    {
    }

    /// <summary>The user moved to another text box: results for the old one no longer apply. Only called once built.</summary>
    public virtual void OnNewField()
    {
    }

    // ---- helpers for tab views ----

    protected static TextBlock Heading(string text, double top = 0)
    {
        var t = UiKit.Label(text, 13, FontWeights.SemiBold);
        t.Margin = new Thickness(0, top, 0, 2);
        return t;
    }

    protected static TextBlock Hint(string text, double bottom = 8)
    {
        var t = UiKit.Label(text, 12, null, Theme.Muted, wrap: true);
        t.Margin = new Thickness(0, 0, 0, bottom);
        return t;
    }
}

/// <summary>Which tab is in front. Unknown ids (say, a tab from a newer version in desktop.json) fall back to the first.</summary>
internal sealed class TabSet
{
    public TabSet(IReadOnlyList<PanelTab> tabs, string? initial)
    {
        if (tabs.Count == 0) throw new ArgumentException("The panel needs at least one tab.", nameof(tabs));
        All = tabs;
        Active = tabs.FirstOrDefault(t => t.Id == initial) ?? tabs[0];
    }

    public IReadOnlyList<PanelTab> All { get; }

    public PanelTab Active { get; private set; }

    /// <summary>Bring <paramref name="id"/> to the front. False if it's already there or doesn't exist.</summary>
    public bool Select(string id)
    {
        var t = All.FirstOrDefault(x => x.Id == id);
        if (t == null || ReferenceEquals(t, Active)) return false;
        Active = t;
        return true;
    }
}
