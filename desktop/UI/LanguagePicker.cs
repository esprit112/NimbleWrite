using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NimbleWrite.Desktop.UI;

/// <summary>
/// Pick a language without a drop-down (drop-downs misbehave in windows that never take focus): a few quick choices
/// (recent languages, plus "Detect" when there's an automatic option), and "All languages", which unfolds the full
/// list with a filter box. Picking from the full list folds it away again and adds the language to the quick choices.
/// </summary>
internal sealed class LanguagePicker : StackPanel
{
    readonly string[] all;
    readonly List<string> quick;
    readonly string? autoLabel;
    readonly WrapPanel quickRow = new();
    readonly StackPanel more = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 4) };
    readonly InputBox filter;
    readonly WrapPanel matchRow = new() { Margin = new Thickness(0, 6, 0, 0) };
    readonly string name;
    bool clearing;

    public LanguagePicker(string name, IReadOnlyList<string> all, IEnumerable<string> quick, string? selected, string? autoLabel)
    {
        this.name = name;
        this.all = all.ToArray();
        this.autoLabel = autoLabel;
        this.quick = quick.Where(this.all.Contains).Distinct().ToList();
        Selected = selected != null && this.all.Contains(selected) ? selected : autoLabel != null ? null : this.quick.FirstOrDefault() ?? this.all[0];
        if (Selected != null && !this.quick.Contains(Selected)) this.quick.Add(Selected);
        AutomationProperties.SetName(this, name);

        filter = new InputBox($"Find a language ({name.ToLowerInvariant()})", 40, placeholder: "Type to find a language");
        filter.TextChanged += () =>
        {
            if (!clearing) Filter(filter.Text);
        };
        filter.Submitted += () =>
        {
            if (Matches.Count > 0) Choose(Matches[0]);
        };
        more.Children.Add(filter);
        // Its own short scroll area, so the tool's button and result stay in view while the list is open.
        more.Children.Add(new ScrollViewer { Content = matchRow, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        Children.Add(quickRow);
        Children.Add(more);
        Matches = this.all;
        RenderQuick();
        RenderMatches();
    }

    /// <summary>The chosen language, or null for automatic (only when there's an automatic option).</summary>
    public string? Selected { get; private set; }

    public event Action<string?>? SelectedChanged;

    public IReadOnlyList<string> QuickChoices => quick;

    public bool Expanded => more.Visibility == Visibility.Visible;

    /// <summary>Languages in the full list that match the filter.</summary>
    public IReadOnlyList<string> Matches { get; private set; } = Array.Empty<string>();

    /// <summary>Select a language (or null for automatic). Unknown languages, and the current choice, are ignored.</summary>
    public void Choose(string? language)
    {
        if (language == Selected) return;
        if (language == null ? autoLabel == null : !all.Contains(language)) return;
        Selected = language;
        if (language != null && !quick.Contains(language)) quick.Add(language);
        SetExpanded(false);
        RenderQuick();
        RenderMatches();
        SelectedChanged?.Invoke(language);
    }

    /// <summary>Show the full list, narrowed to languages containing <paramref name="text"/>.</summary>
    public void Filter(string text)
    {
        string t = text.Trim();
        Matches = t.Length == 0 ? all : all.Where(l => l.Contains(t, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (filter.Text != text) filter.Text = text;
        SetExpanded(true);
        RenderMatches();
    }

    void SetExpanded(bool on)
    {
        more.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on && filter.Text.Length > 0)
        {
            clearing = true; // emptying the box must not unfold the list again
            filter.Text = "";
            clearing = false;
            Matches = all;
        }
        RenderQuick();
    }

    void RenderQuick()
    {
        quickRow.Children.Clear();
        if (autoLabel != null) quickRow.Children.Add(Chip(autoLabel, null, $"Let the AI work out the language"));
        foreach (var l in quick) quickRow.Children.Add(Chip(l, l, null));
        var toggle = UiKit.Button(Expanded ? "Fewer" : "All languages…", () => SetExpanded(!Expanded), Kind.Ghost, Expanded ? "Hide the full list" : $"Choose from all {all.Length} languages");
        quickRow.Children.Add(UiKit.Spaced(toggle));
    }

    void RenderMatches()
    {
        matchRow.Children.Clear();
        if (Matches.Count == 0)
        {
            matchRow.Children.Add(UiKit.Label("No language matches that.", 12, null, Theme.Muted));
            return;
        }
        foreach (var l in Matches) matchRow.Children.Add(Chip(l, l, null));
    }

    Border Chip(string label, string? value, string? tip)
    {
        bool on = value == Selected;
        var chip = UiKit.Spaced(UiKit.Button(label, () => Choose(value), on ? Kind.Primary : Kind.Secondary, tip));
        AutomationProperties.SetItemStatus(chip, on ? "Selected" : "");
        AutomationProperties.SetHelpText(chip, name);
        return chip;
    }
}
