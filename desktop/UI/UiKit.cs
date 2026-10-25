using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace NimbleWrite.Desktop.UI;

internal enum Kind { Primary, Secondary, Ghost, Accept }

/// <summary>Small building blocks for the code-built WPF UI (no XAML, so everything is checked at compile time).</summary>
internal static class UiKit
{
    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");

    /// <summary>Space around cards for their drop shadow, in DIPs. Window placement compensates for it.</summary>
    public const double ShadowMargin = 14;

    public static TextBlock Label(string text, double size = 13, FontWeight? weight = null, Brush? brush = null, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = Font,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = brush ?? Theme.Text,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    public static Border Button(string text, Action onClick, Kind kind = Kind.Secondary, string? tip = null)
    {
        var fg = kind is Kind.Primary or Kind.Accept ? Theme.OnBrand : kind == Kind.Ghost ? Theme.Muted : Theme.Text;
        var label = Label(text, 12.5, FontWeights.SemiBold, fg);
        return Clickable(label, onClick, kind, tip);
    }

    /// <summary>A border that behaves like a button. Works in our non-activating windows without taking focus.</summary>
    public static Border Clickable(UIElement content, Action onClick, Kind kind, string? tip = null)
    {
        Brush normal = kind switch { Kind.Primary or Kind.Accept => Theme.Brand, Kind.Ghost => Brushes.Transparent, _ => Theme.Subtle };
        Brush hover = kind is Kind.Primary or Kind.Accept ? Theme.BrandHover : Theme.Hover;
        var b = new ClickBorder(onClick)
        {
            Background = normal,
            CornerRadius = new CornerRadius(6),
            Padding = kind == Kind.Ghost ? new Thickness(7, 4, 7, 5) : new Thickness(11, 5, 11, 6),
            Child = content,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            SnapsToDevicePixels = true,
        };
        bool pressed = false;
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) =>
        {
            b.Background = normal;
            pressed = false;
        };
        b.MouseLeftButtonDown += (_, e) =>
        {
            pressed = true;
            e.Handled = true;
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            e.Handled = true;
            onClick();
        };
        return b;
    }

    /// <summary>The NimbleWrite mark: green rounded square, white N and the NVIDIA-green dot (same as the web favicon).</summary>
    public static FrameworkElement Logo(double size)
    {
        var canvas = new Canvas { Width = 64, Height = 64 };
        canvas.Children.Add(new Rectangle { Width = 64, Height = 64, RadiusX = 16, RadiusY = 16, Fill = Theme.B("#0B6B3A") });
        canvas.Children.Add(new Path { Data = Geometry.Parse("M18 44V20h6l16 16V20h6v24h-6L24 28v16z"), Fill = Brushes.White });
        var dot = new Ellipse { Width = 10, Height = 10, Fill = Theme.B("#76B900") };
        Canvas.SetLeft(dot, 45);
        Canvas.SetTop(dot, 41);
        canvas.Children.Add(dot);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    public static Border Surface(UIElement child, double radius = 10) => new()
    {
        Background = Theme.Surface,
        BorderBrush = Theme.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius),
        Child = child,
        Margin = new Thickness(ShadowMargin),
        Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = Theme.Dark ? 0.55 : 0.2, Color = Colors.Black },
    };

    public static Border Divider(double top = 10, double bottom = 10) => new() { Height = 1, Background = Theme.Line, Margin = new Thickness(0, top, 0, bottom) };

    public static Ellipse Dot(Brush b, double size = 8) => new() { Width = size, Height = size, Fill = b, VerticalAlignment = VerticalAlignment.Center };

    public static T Spaced<T>(T e, double right = 6, double bottom = 6) where T : FrameworkElement
    {
        e.Margin = new Thickness(0, 0, right, bottom);
        return e;
    }

    /// <summary>Shorten user text for display; show a deletion's word without the space it takes along.</summary>
    public static string Display(string s, int max = 140)
    {
        string t = s.Trim();
        if (t.Length == 0) t = s.Length == 0 ? "" : "(space)";
        return t.Length > max ? t[..max] + "…" : t;
    }
}

/// <summary>
/// What UiKit buttons are made of. Screen readers and other UI Automation clients see a button they can invoke, named
/// after its label (or tooltip). It still never takes keyboard focus, so the non-activating overlays stay that way.
/// </summary>
internal sealed class ClickBorder : Border
{
    readonly Action onClick;

    public ClickBorder(Action onClick) => this.onClick = onClick;

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    sealed class Peer : FrameworkElementAutomationPeer, IInvokeProvider
    {
        public Peer(ClickBorder owner)
            : base(owner)
        {
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => "Button";

        protected override bool IsKeyboardFocusableCore() => false;

        protected override string GetNameCore()
        {
            string name = base.GetNameCore();
            if (!string.IsNullOrEmpty(name)) return name;
            var owner = (ClickBorder)Owner;
            return owner.Child is TextBlock { Text.Length: > 0 } t ? t.Text : owner.ToolTip as string ?? "";
        }

        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPattern(pattern);

        void IInvokeProvider.Invoke()
        {
            if (!IsEnabled()) throw new ElementNotEnabledException();
            var owner = (ClickBorder)Owner;
            // Run it after the automation call returns: the action may close the window it lives in.
            owner.Dispatcher.BeginInvoke(owner.onClick);
        }
    }
}
