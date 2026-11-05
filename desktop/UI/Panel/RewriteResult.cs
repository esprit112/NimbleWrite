using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>
/// A streamed AI result with Replace (or Insert after), Copy and Discard. Every tab that rewrites text uses one, so
/// results always go back through the engine's verified replace: if the text changed meanwhile, nothing is typed.
/// </summary>
internal sealed class RewriteResult : StackPanel
{
    readonly IPanelActions host;
    readonly TextBlock status;
    readonly Border box;
    readonly TextBlock text;
    readonly StackPanel actions;
    readonly TextBlock applyLabel;
    AiUpdate? ai;
    int seq = -1;
    bool live;
    string task = "";

    public RewriteResult(IPanelActions host)
    {
        this.host = host;
        status = UiKit.Label("", 12, null, Theme.Muted, wrap: true);
        status.Margin = new Thickness(0, 8, 0, 4);
        status.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        Children.Add(status);

        text = UiKit.Label("", 13, null, Theme.Text, wrap: true);
        box = new Border
        {
            Background = Theme.Raised,
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 2, 0, 8),
            Child = new ScrollViewer { Content = text, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false },
            Visibility = Visibility.Collapsed,
        };
        Children.Add(box);

        actions = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
        var apply = UiKit.Button("Replace", Apply, Kind.Primary);
        applyLabel = (TextBlock)apply.Child;
        actions.Children.Add(UiKit.Spaced(apply));
        actions.Children.Add(UiKit.Spaced(UiKit.Button("Copy", Copy)));
        actions.Children.Add(UiKit.Spaced(UiKit.Button("Discard", Discard, Kind.Ghost)));
        Children.Add(actions);
    }

    public string Status => status.Text;

    public bool CanApply => live && ai is { Done: true, Error: null, Scope: not null } && ai.Text.Trim().Length > 0;

    public bool Running => live && ai is not { Done: true };

    public void Start(WriteRequest request)
    {
        task = request.Task;
        live = true;
        seq = -1;
        ai = null;
        applyLabel.Text = Inserts(task) ? "Insert after" : "Replace";
        SetStatus("Thinking…");
        box.Visibility = Visibility.Collapsed;
        actions.Visibility = Visibility.Collapsed;
        host.Rewrite(request, this);
    }

    public void OnUpdate(AiUpdate u)
    {
        if (!live || u.Seq < seq) return;
        seq = u.Seq;
        ai = u;
        if (u.Error != null)
        {
            SetStatus(u.Error);
            box.Visibility = Visibility.Collapsed;
            actions.Visibility = Visibility.Collapsed;
            return;
        }
        string what = u.Scope?.Whole == false ? "your selection" : "all the text";
        bool insert = Inserts(task);
        SetStatus(u.Done
            ? insert ? $"From {what}:" : $"Rewrite of {what}:"
            : u.Text.Length == 0 ? "Thinking…" : insert ? "Writing…" : $"Rewriting {what}…");
        text.Text = u.Text;
        box.Visibility = u.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        actions.Visibility = CanApply ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Another tool started a rewrite, which stopped this one.</summary>
    public void Interrupted()
    {
        if (!live) return;
        live = false;
        ai = null;
        SetStatus("Stopped: another tool started a rewrite.");
        box.Visibility = Visibility.Collapsed;
        actions.Visibility = Visibility.Collapsed;
    }

    public void Apply()
    {
        if (!CanApply) return;
        var u = ai!;
        host.ApplyRewrite(u.Scope!, Compose(u.Scope!, u.Text, task));
        Discard();
    }

    void Copy()
    {
        if (ai?.Text is not { Length: > 0 } t) return;
        try
        {
            Clipboard.SetText(t.Trim());
            host.Notice("Copied to the clipboard.");
        }
        catch (Exception)
        {
            host.Notice("Couldn't use the clipboard just now.");
        }
    }

    public void Discard()
    {
        if (Running) host.CancelRewrite(this);
        live = false;
        ai = null;
        status.Visibility = Visibility.Collapsed;
        box.Visibility = Visibility.Collapsed;
        actions.Visibility = Visibility.Collapsed;
    }

    void SetStatus(string s)
    {
        status.Text = s;
        status.Visibility = Visibility.Visible;
    }

    /// <summary>Tasks whose result goes after the text (a summary, a reply…) instead of replacing it.</summary>
    public static bool Inserts(string task) => task is "summarize" or "continue" or "reply" or "outline" or "brainstorm";

    /// <summary>
    /// What replaces <paramref name="scope"/>. A rewrite keeps the spaces and line breaks around the original (models
    /// often add or drop a trailing newline); an inserted result goes after the original, on its own paragraph, or
    /// straight after it for "continue".
    /// </summary>
    public static string Compose(TextScope scope, string result, string task)
    {
        string body = result.Trim();
        string original = scope.Text;
        if (!Inserts(task))
        {
            string lead = original[..(original.Length - original.TrimStart().Length)];
            string trail = original[original.TrimEnd().Length..];
            return lead + body + trail;
        }
        if (task == "continue") return original + (original.Length == 0 || char.IsWhiteSpace(original[^1]) ? "" : " ") + body;
        string nl = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string sep = original.EndsWith(nl + nl, StringComparison.Ordinal) ? "" : original.EndsWith(nl, StringComparison.Ordinal) ? nl : nl + nl;
        return original + sep + body;
    }
}
