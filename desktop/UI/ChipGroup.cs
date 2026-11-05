using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NimbleWrite.Desktop.UI;

/// <summary>
/// Pick one of a few options, shown as a row of chips that wraps. Used instead of a ComboBox, whose drop-down popup
/// misbehaves in our windows that never take focus. The selected chip is filled; screen readers hear "Selected".
/// </summary>
internal sealed class ChipGroup : WrapPanel
{
    readonly (string Id, string Label, string Tip)[] options;

    public ChipGroup(IEnumerable<(string Id, string Label, string Tip)> options, string selected)
    {
        this.options = options.ToArray();
        Selected = this.options.Any(o => o.Id == selected) ? selected : this.options.FirstOrDefault().Id ?? "";
        Render();
    }

    public string Selected { get; private set; }

    /// <summary>The user (or <see cref="Select"/>) picked a different chip.</summary>
    public event Action<string>? SelectedChanged;

    public IEnumerable<Border> Chips => Children.OfType<Border>();

    /// <summary>Select <paramref name="id"/>. Unknown ids and the current selection are ignored.</summary>
    public void Select(string id, bool notify = true)
    {
        if (id == Selected || options.All(o => o.Id != id)) return;
        Selected = id;
        Render(); // rebuilt rather than restyled: a chip's normal and hover colours are fixed when it's made
        if (notify) SelectedChanged?.Invoke(id);
    }

    void Render()
    {
        Children.Clear();
        foreach (var (id, label, tip) in options)
        {
            bool on = id == Selected;
            var chip = UiKit.Spaced(UiKit.Button(label, () => Select(id), on ? Kind.Primary : Kind.Secondary, string.IsNullOrEmpty(tip) ? null : tip));
            AutomationProperties.SetItemStatus(chip, on ? "Selected" : "");
            Children.Add(chip);
        }
    }
}
