using System;
using Interop.UIAutomationClient;

namespace NimbleWrite.Desktop.Core;

internal static class UiaIds
{
    public const int ValuePattern = 10002, TextPattern = 10014;
    public const int IsReadOnlyAttribute = 40015;
    public const int Edit = 50004, Document = 50030, Text = 50020, Custom = 50025, Group = 50026, Pane = 50033, ComboBox = 50003;
    public const int Menu = 50009, MenuBar = 50010, MenuItem = 50011, TabItem = 50019;

    public static string ControlName(int id) => id switch
    {
        Edit => "Edit", Document => "Document", Text => "Text", Custom => "Custom", Group => "Group", Pane => "Pane", ComboBox => "ComboBox",
        Menu => "Menu", MenuBar => "MenuBar", MenuItem => "MenuItem", TabItem => "TabItem",
        _ => id.ToString(),
    };
}

/// <summary>The focused, editable field the engine is attached to. Only touched on the engine thread.</summary>
internal sealed class Field
{
    public Field(IUIAutomationElement element, IUIAutomationTextPattern? text, IUIAutomationValuePattern? value, TargetInfo info, string kind)
    {
        Element = element;
        Text = text;
        Value = value;
        Info = info;
        Kind = kind;
    }

    public IUIAutomationElement Element { get; }
    public IUIAutomationTextPattern? Text { get; }
    public IUIAutomationValuePattern? Value { get; }
    public TargetInfo Info { get; }

    /// <summary>Structure only (control type, framework, pattern) — safe to log.</summary>
    public string Kind { get; }

    /// <summary>True once we learn this app counts UI Automation characters as grapheme clusters.</summary>
    public bool Graphemes { get; set; }

    /// <summary>
    /// System-DPI-aware WPF (.NET Framework) reports text-range rectangles in unscaled units around the window's
    /// client origin, though element rectangles are right. When set, the engine scales text rectangles back.
    /// </summary>
    public bool UnscaledTextRects { get; init; }

    public string? Read(int max)
    {
        if (Text != null) return Text.DocumentRange.GetText(max);
        if (Value != null)
        {
            string v = Value.CurrentValue ?? "";
            return v.Length > max ? v[..max] : v;
        }
        return null;
    }
}

/// <summary>A collapsed range that walks forward through the document, so placing N sorted suggestions is one pass.</summary>
internal sealed class RangeCursor
{
    const TextPatternRangeEndpoint Start = TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start;
    const TextPatternRangeEndpoint End = TextPatternRangeEndpoint.TextPatternRangeEndpoint_End;
    readonly IUIAutomationTextRange doc;
    IUIAutomationTextRange? at;
    int units;

    public RangeCursor(IUIAutomationTextRange doc) => this.doc = doc;

    public void Reset() => at = null;

    /// <summary>A range covering [startUnits, startUnits + lengthUnits) in UI Automation character units, or null past the end.</summary>
    public IUIAutomationTextRange? RangeAt(int startUnits, int lengthUnits)
    {
        if (at == null || startUnits < units)
        {
            at = doc.Clone();
            at.MoveEndpointByRange(End, doc, Start);
            units = 0;
        }
        int delta = startUnits - units;
        if (delta > 0)
        {
            int moved = at.MoveEndpointByUnit(Start, TextUnit.TextUnit_Character, delta);
            var collapsed = at.Clone();
            at.MoveEndpointByRange(End, collapsed, Start);
            units += moved;
            if (moved != delta)
            {
                at = null;
                return null;
            }
        }
        var r = at.Clone();
        if (lengthUnits > 0) r.MoveEndpointByUnit(End, TextUnit.TextUnit_Character, lengthUnits);
        return r;
    }
}
