using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>Your NimbleWrite documents: find one, put its text in place of the selection, copy it, or open it in the editor.</summary>
internal sealed class DocumentsTab : PanelTab
{
    InputBox search = null!;
    AgentRunner runner = null!;
    StackPanel list = null!;
    List<DocSummary> docs = new();

    public DocumentsTab(IPanelActions host) : base(host)
    {
    }

    public override string Id => "documents";
    public override string Label => "Documents";
    public override string Glyph => Icons.Documents;

    protected override FrameworkElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Hint("Your documents in NimbleWrite. Insert replaces the text you've selected."));
        var save = UiKit.Button("Save this text as a document", Host.OpenEditor, Kind.Secondary, "Saves the text box's text as a new document and opens it in the editor");
        save.HorizontalAlignment = HorizontalAlignment.Left;
        save.Margin = new Thickness(0, 0, 0, 8);
        root.Children.Add(save);
        search = new InputBox("Search documents", 100, placeholder: "Search by title or text") { Margin = new Thickness(0, 0, 0, 4) };
        search.TextChanged += Render;
        root.Children.Add(search);
        runner = new AgentRunner(Host);
        root.Children.Add(runner);
        list = new StackPanel();
        root.Children.Add(list);
        return root;
    }

    public override void OnShown(EngineSnapshot? s) =>
        _ = runner.Run("Loading your documents…", ct => Host.Api.ListDocsAsync(ct), Show, "");

    internal void Show(List<DocSummary> all)
    {
        _ = View;
        docs = all;
        runner.Reset(); // nothing to say once the list is there
        Render();
    }

    void Render()
    {
        list.Children.Clear();
        var shown = Filter(docs, search.Text).Take(50).ToList();
        if (shown.Count == 0)
        {
            list.Children.Add(UiKit.Label(docs.Count == 0 ? "No documents yet." : "No documents match that.", 12.5, null, Theme.Muted, wrap: true));
            return;
        }
        var now = DateTimeOffset.Now;
        foreach (var d in shown)
        {
            var s = new StackPanel();
            s.Children.Add(UiKit.Label(d.Title.Length > 0 ? d.Title : "Untitled", 13, FontWeights.SemiBold));
            if (d.Snippet.Length > 0)
            {
                var snip = UiKit.Label(d.Snippet, 12, null, Theme.Muted, wrap: true);
                snip.MaxHeight = 36;
                snip.TextTrimming = TextTrimming.WordEllipsis;
                s.Children.Add(snip);
            }
            s.Children.Add(UiKit.Label($"{d.WordCount:N0} words · {Ago(d.UpdatedAt, now)}", 11.5, null, Theme.Muted));
            var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            actions.Children.Add(UiKit.Spaced(UiKit.Button("Insert", () => Insert(d), Kind.Primary, "Replace the selected text with this document's text"), 6, 0));
            actions.Children.Add(UiKit.Spaced(UiKit.Button("Copy text", () => Copy(d), Kind.Secondary), 6, 0));
            actions.Children.Add(UiKit.Button("Open", () => Host.OpenUrl($"{Host.EditorUrl}/#/doc/{Uri.EscapeDataString(d.Id)}"), Kind.Ghost, "Open in the NimbleWrite editor"));
            s.Children.Add(actions);
            list.Children.Add(Widgets.Card(s));
        }
    }

    async void Insert(DocSummary d)
    {
        try
        {
            var (scope, problem) = await Host.CaptureAsync(TextModel.MaxChars, "replace");
            if (scope == null)
            {
                Host.Notice(problem ?? "Click into a text box first.");
                return;
            }
            if (scope.Whole)
            {
                Host.Notice("Select the text to replace first, or use Copy text and paste it yourself.");
                return;
            }
            var doc = await Host.Api.GetDocAsync(d.Id, default);
            Host.ApplyRewrite(scope, doc.Text, $"Inserted \u201C{doc.Title}\u201D.");
        }
        catch (Exception e)
        {
            Host.Notice(AgentRunner.Describe(e));
        }
    }

    async void Copy(DocSummary d)
    {
        try
        {
            var doc = await Host.Api.GetDocAsync(d.Id, default);
            OnUi(() =>
            {
                try
                {
                    Clipboard.SetText(doc.Text);
                    Host.Notice($"Copied \u201C{doc.Title}\u201D.");
                }
                catch (Exception)
                {
                    Host.Notice("Couldn't use the clipboard just now.");
                }
            });
        }
        catch (Exception e)
        {
            Host.Notice(AgentRunner.Describe(e));
        }
    }

    /// <summary>Documents not in the bin whose title or snippet contains <paramref name="query"/>, newest first.</summary>
    internal static IEnumerable<DocSummary> Filter(IEnumerable<DocSummary> docs, string query)
    {
        string q = query.Trim();
        return docs.Where(d => d.DeletedAt == null)
            .Where(d => q.Length == 0 || d.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || d.Snippet.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.UpdatedAt);
    }

    /// <summary>"just now", "5 min ago", "3 h ago", "2 days ago", then the date.</summary>
    internal static string Ago(long updatedAtMs, DateTimeOffset now)
    {
        var age = now - DateTimeOffset.FromUnixTimeMilliseconds(updatedAtMs);
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} h ago";
        if (age < TimeSpan.FromDays(7)) return $"{(int)age.TotalDays} day{((int)age.TotalDays == 1 ? "" : "s")} ago";
        return DateTimeOffset.FromUnixTimeMilliseconds(updatedAtMs).ToOffset(now.Offset).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
