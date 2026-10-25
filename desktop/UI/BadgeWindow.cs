using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI;

/// <summary>The little N button in the corner of the field: suggestion count, a spinner while checking. Click for the assistant.</summary>
internal sealed class BadgeWindow : NoActivateWindow
{
    const double Box = 30;
    readonly Border bubble;
    readonly TextBlock count;
    readonly FrameworkElement logo;
    readonly Path ring;
    readonly RotateTransform spin = new(0, Box / 2, Box / 2);
    bool spinning;

    public event Action? Clicked;

    public BadgeWindow() : base(clickThrough: false)
    {
        Width = Box;
        Height = Box;
        logo = UiKit.Logo(22);
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        logo.VerticalAlignment = VerticalAlignment.Center;
        ring = new Path
        {
            Data = Geometry.Parse("M 15,1.5 A 13.5,13.5 0 0 1 28.5,15"),
            Stroke = Theme.B("#76B900"),
            StrokeThickness = 2.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = spin,
            Visibility = Visibility.Collapsed,
        };
        count = UiKit.Label("", 9.5, FontWeights.Bold, Brushes.White);
        count.TextAlignment = TextAlignment.Center;
        count.HorizontalAlignment = HorizontalAlignment.Center;
        count.VerticalAlignment = VerticalAlignment.Center;
        bubble = new Border
        {
            MinWidth = 15,
            Height = 15,
            CornerRadius = new CornerRadius(7.5),
            Padding = new Thickness(3, 0, 3, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.2),
            Child = count,
            Visibility = Visibility.Collapsed,
        };
        var grid = new Grid
        {
            Width = Box,
            Height = Box,
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), // alpha 1: still clickable in a layered window
            Cursor = Cursors.Hand,
        };
        grid.Children.Add(logo);
        grid.Children.Add(ring);
        grid.Children.Add(bubble);
        grid.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Clicked?.Invoke();
        };
        grid.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            Clicked?.Invoke();
        };
        Content = grid;
    }

    public void Update(EngineSnapshot s)
    {
        int n = s.Issues.Count;
        bubble.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        count.Text = n > 99 ? "99+" : n.ToString();
        string worst = s.Issues.Select(i => i.Issue.Category).OrderBy(Theme.CategoryRank).FirstOrDefault() ?? "correctness";
        bubble.Background = Theme.CategoryBrush(worst);
        logo.Opacity = s.State is CheckState.Offline or CheckState.TooLong or CheckState.Private ? 0.45 : 1;
        Spin(s.State == CheckState.Checking);
        ToolTip = s.State switch
        {
            CheckState.Checking => "NimbleWrite is checking…",
            CheckState.Offline => "NimbleWrite can't reach its server",
            CheckState.TooLong => "Too long for live checking",
            CheckState.Private => "Not checked: this looks like passwords or keys",
            _ => n == 0 ? "NimbleWrite: no issues found" : $"NimbleWrite: {n} suggestion{(n == 1 ? "" : "s")}",
        };
    }

    void Spin(bool on)
    {
        if (on == spinning) return;
        spinning = on;
        ring.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        spin.BeginAnimation(RotateTransform.AngleProperty, on ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever } : null);
    }

    /// <summary>Sit inside the field's bottom-right corner (vertically centred in short, single-line fields).</summary>
    public void PlaceInField(Rect field)
    {
        double s = Native.ScaleAt(field.Right - 1, field.Bottom - 1);
        double size = Box * s, inset = 3 * s;
        double x = field.Right - size - inset;
        double y = field.Height >= size + 2 * inset ? field.Bottom - size - inset : field.Top + (field.Height - size) / 2;
        if (!IsVisible) Show();
        MoveTo(x, y);
    }
}
