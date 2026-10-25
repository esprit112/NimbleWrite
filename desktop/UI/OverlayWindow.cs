using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI;

/// <summary>A transparent, click-through layer over the focused field that draws the coloured underlines.</summary>
internal sealed class OverlayWindow : NoActivateWindow
{
    readonly UnderlineLayer layer = new();

    public OverlayWindow() : base(clickThrough: true) => Content = layer;

    public void Render(GeometrySnapshot g, string? highlight)
    {
        if (g.Underlines.Count == 0)
        {
            Hide();
            return;
        }
        var bounds = Rect.Inflate(g.Field, 3, 5);
        if (!IsVisible) Show();
        Place(bounds);
        layer.Set(g.Underlines, bounds.TopLeft, highlight);
    }

    public void SetHighlight(string? id) => layer.SetHighlight(id);
}

internal sealed class UnderlineLayer : FrameworkElement
{
    IReadOnlyList<Underline> lines = Array.Empty<Underline>();
    Point origin;
    string? highlight;

    public void Set(IReadOnlyList<Underline> underlines, Point windowOrigin, string? highlighted)
    {
        lines = underlines;
        origin = windowOrigin;
        highlight = highlighted;
        InvalidateVisual();
    }

    public void SetHighlight(string? id)
    {
        if (highlight == id) return;
        highlight = id;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        foreach (var u in lines)
        {
            var c = Theme.CategoryColor(u.Category);
            var pen = new Pen(new SolidColorBrush(c), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            Brush? glow = null;
            if (u.IssueId == highlight)
            {
                glow = new SolidColorBrush(Color.FromArgb(46, c.R, c.G, c.B));
                glow.Freeze();
            }
            foreach (var r in u.Rects)
            {
                double x = (r.X - origin.X) / s, y = (r.Y - origin.Y) / s, w = r.Width / s, h = r.Height / s;
                if (glow != null) dc.DrawRectangle(glow, null, new Rect(x, y, w, h));
                double uy = y + h - 0.5;
                dc.DrawLine(pen, new Point(x + 0.5, uy), new Point(x + Math.Max(1, w - 0.5), uy));
            }
        }
    }
}
