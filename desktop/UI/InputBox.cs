using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI;

/// <summary>
/// A text box that works inside our windows that never take focus. Clicking it borrows the keyboard
/// (<see cref="NoActivateWindow.BeginTyping"/>); Enter (Ctrl+Enter when it has several lines), Escape, or clicking any
/// button gives the keyboard back to the app you were writing in. The Paste button fills it from the clipboard without
/// borrowing the keyboard at all.
/// </summary>
internal sealed class InputBox : DockPanel
{
    readonly bool multiline;
    readonly TextBlock placeholder;

    public InputBox(string name, int maxLength, bool multiline = false, string? placeholder = null)
    {
        this.multiline = multiline;
        LastChildFill = true;
        var paste = UiKit.IconButton(Icons.Paste, Paste, "Paste from the clipboard");
        paste.VerticalAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center;
        paste.Margin = new Thickness(4, 0, 0, 0);
        SetDock(paste, Dock.Right);
        Children.Add(paste);

        Box = new TextBox
        {
            MaxLength = maxLength,
            FontFamily = UiKit.Font,
            FontSize = 13,
            Padding = new Thickness(6, 5, 6, 5),
            Background = Theme.Raised,
            Foreground = Theme.Text,
            BorderBrush = Theme.Line,
            CaretBrush = Theme.Text,
            SelectionBrush = Theme.Brand,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 64 : 0,
            MaxHeight = multiline ? 140 : double.PositiveInfinity,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        };
        AutomationProperties.SetName(Box, name);
        Box.PreviewMouseLeftButtonDown += (_, _) => Borrow();
        Box.PreviewKeyDown += (_, e) =>
        {
            if (HandleKey(e.Key, Keyboard.Modifiers)) e.Handled = true;
        };
        this.placeholder = UiKit.Label(placeholder ?? name, 13, null, Theme.Muted, wrap: multiline);
        this.placeholder.Margin = new Thickness(9, 6, 9, 0);
        this.placeholder.IsHitTestVisible = false;
        this.placeholder.VerticalAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center;
        Box.TextChanged += (_, _) =>
        {
            this.placeholder.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            TextChanged?.Invoke();
        };
        var stack = new Grid();
        stack.Children.Add(Box);
        stack.Children.Add(this.placeholder);
        Children.Add(stack);
    }

    public TextBox Box { get; }

    public string Text
    {
        get => Box.Text;
        set => Box.Text = value.Length > Box.MaxLength && Box.MaxLength > 0 ? ApiClient.Clip(value, Box.MaxLength) : value;
    }

    /// <summary>Enter (or Ctrl+Enter when the box has several lines).</summary>
    public event Action? Submitted;

    public event Action? TextChanged;

    /// <summary>Keys that end typing. Returns true when the key was used up here.</summary>
    internal bool HandleKey(Key key, ModifierKeys mods)
    {
        if (key == Key.Escape)
        {
            GiveBack();
            return true;
        }
        if (key != Key.Enter && key != Key.Return) return false;
        if (multiline && (mods & ModifierKeys.Control) == 0) return false; // a new line
        GiveBack();
        Submitted?.Invoke();
        return true;
    }

    void Borrow()
    {
        if (Window.GetWindow(this) is not NoActivateWindow w || w.Typing) return;
        if (w.BeginTyping()) Box.Focus();
    }

    void GiveBack()
    {
        if (Window.GetWindow(this) is NoActivateWindow { Typing: true } w) w.EndTyping(restore: true);
    }

    void Paste()
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            Text = Clipboard.GetText();
        }
        catch (Exception e)
        {
            Log.Info($"paste into {AutomationProperties.GetName(Box)}: {e.Message}");
        }
    }
}
