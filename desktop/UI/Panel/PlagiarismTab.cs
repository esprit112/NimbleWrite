using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>Plagiarism Checker: passages that match your other documents, pasted sources, links or (if set up) the web.</summary>
internal sealed class PlagiarismTab : PanelTab
{
    ToggleRow docs = null!, web = null!;
    TextBlock webNote = null!;
    InputBox urls = null!, pasted = null!;
    AgentRunner runner = null!;
    StackPanel results = null!;
    bool searchConfigured;

    public PlagiarismTab(IPanelActions host) : base(host)
    {
    }

    public override string Id => "plagiarism";
    public override string Label => "Plagiarism Checker";
    public override string Glyph => Icons.Plagiarism;

    protected override FrameworkElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Hint("Find passages that match other sources, so you can quote and cite them properly."));
        docs = new ToggleRow("Compare with my other documents", true);
        root.Children.Add(docs);
        // Off by default even when available: web search sends parts of the text to a search provider.
        web = new ToggleRow("Search the web", false) { Enabled = false };
        root.Children.Add(web);
        webNote = Widgets.Fine("Web search isn't set up, so add sources below to compare against.");
        webNote.Margin = new Thickness(26, 0, 0, 6);
        root.Children.Add(webNote);
        root.Children.Add(Heading("Source links to compare against (optional)", 4));
        urls = new InputBox("Source links", 4000, multiline: true, "https://… one per line") { Margin = new Thickness(0, 4, 0, 6) };
        root.Children.Add(urls);
        root.Children.Add(Heading("Or paste source text (optional)"));
        pasted = new InputBox("Source text", 400_000, multiline: true, "Paste the text you want to compare with") { Margin = new Thickness(0, 4, 0, 8) };
        root.Children.Add(pasted);
        var go = UiKit.Button("Check originality", Go, Kind.Primary);
        go.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(go);
        runner = new AgentRunner(Host);
        root.Children.Add(runner);
        results = new StackPanel();
        root.Children.Add(results);
        return root;
    }

    public override async void OnShown(EngineSnapshot? s)
    {
        if (searchConfigured) return;
        try
        {
            var health = await Host.Api.HealthInfoAsync(default);
            searchConfigured = health?.SearchConfigured == true;
        }
        catch (Exception)
        {
            searchConfigured = false;
        }
        OnUi(() =>
        {
            web.Enabled = searchConfigured;
            webNote.Visibility = searchConfigured ? Visibility.Collapsed : Visibility.Visible;
        });
    }

    void Go()
    {
        results.Children.Clear();
        bool includeDocs = docs.On, useWeb = web.On;
        string paste = pasted.Text, links = urls.Text;
        _ = runner.Run(200_000, "check", "Comparing with your sources…", null,
            (scope, ct) => Host.Api.PlagiarismAsync(BuildRequest(scope.Text, includeDocs, useWeb, searchConfigured, paste, links), ct), Show);
    }

    /// <summary>The request, as the web panel builds it: pasted text as one source, then at most 8 links.</summary>
    internal static PlagiarismRequest BuildRequest(string text, bool includeDocs, bool web, bool searchConfigured, string pasted, string urls)
    {
        var r = new PlagiarismRequest { Text = text, IncludeDocs = includeDocs, Web = web && searchConfigured };
        if (pasted.Trim().Length > 0) r.Sources.Add(new PlagiarismSourceInput { Title = "Pasted source", Text = pasted });
        foreach (var u in Regex.Split(urls, @"\s+").Where(u => Regex.IsMatch(u, "^https?://", RegexOptions.IgnoreCase)).Take(8))
        {
            r.Sources.Add(new PlagiarismSourceInput { Url = u });
        }
        return r;
    }

    internal void Show(PlagiarismResult r, TextScope scope)
    {
        _ = View;
        results.Children.Clear();
        string sources = $"{r.CheckedSources} source{(r.CheckedSources == 1 ? "" : "s")}";
        // Asked with web off when search isn't set up, the server says "skipped"; say why, as the web panel does.
        string? webNote = r.WebSearch switch
        {
            "not_configured" => "Web search isn't set up, so only your documents and the sources you gave were checked.",
            "failed" => "Web search failed, so only your documents and the sources you gave were checked.",
            _ when !searchConfigured => "Web search isn't set up, so only your documents and the sources you gave were checked.",
            _ => null,
        };
        results.Children.Add(Widgets.Headline($"{Math.Round(r.Overall)}%", Scales.Brush(Scales.Matches(r.Overall)), $"of your text matches the {sources} checked", webNote));
        if (!string.IsNullOrWhiteSpace(r.Note)) results.Children.Add(UiKit.Label(r.Note!, 12, null, Scales.Brush(Level.Warn), wrap: true));
        if (r.Sources.Count == 0)
        {
            var none = UiKit.Label("No matching passages found. 🎉", 12.5, null, Theme.Muted, wrap: true);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            results.Children.Add(none);
        }
        foreach (var s in r.Sources)
        {
            var body = new StackPanel();
            var top = new DockPanel();
            var pct = UiKit.Label($"{Math.Round(s.Similarity)}%", 12, FontWeights.SemiBold, Scales.Brush(Scales.Matches(s.Similarity)));
            DockPanel.SetDock(pct, Dock.Right);
            top.Children.Add(pct);
            string icon = s.Kind == "doc" ? "📄 " : s.Kind == "web" ? "🌐 " : "📎 ";
            top.Children.Add(UiKit.Label(icon + s.Title, 12.5, FontWeights.SemiBold));
            body.Children.Add(top);
            if (!string.IsNullOrEmpty(s.Url))
            {
                var link = UiKit.Button(s.Url!, () => Host.OpenUrl(s.Url!), Kind.Ghost, "Open in your browser");
                link.HorizontalAlignment = HorizontalAlignment.Left;
                link.Padding = new Thickness(0, 2, 0, 2);
                body.Children.Add(link);
            }
            foreach (var p in s.Passages.Take(4))
            {
                string t = p.Text.Length > 160 ? p.Text[..160] + "…" : p.Text;
                var quote = UiKit.Label($"\u201C{t}\u201D", 11.5, null, Theme.Text, wrap: true);
                body.Children.Add(new Border { Background = Theme.Raised, BorderBrush = Scales.Brush(Level.Bad), BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(6, 3, 4, 3), Margin = new Thickness(0, 4, 0, 0), Child = quote });
            }
            results.Children.Add(Widgets.Card(body));
        }
    }

    public override void OnNewField()
    {
        runner.Reset();
        results.Children.Clear();
    }
}
