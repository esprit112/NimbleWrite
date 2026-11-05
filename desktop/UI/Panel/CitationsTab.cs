using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>Citations: format a URL or DOI in four styles, or find claims in the text that need a source.</summary>
internal sealed class CitationsTab : PanelTab
{
    /// <summary>Same order and labels as STYLE_LABEL in src/editor/panels/AgentPanels.tsx.</summary>
    internal static readonly (string Id, string Label, string Tip)[] Styles = { ("apa", "APA 7", ""), ("mla", "MLA 9", ""), ("chicago", "Chicago", ""), ("harvard", "Harvard", "") };

    ChipGroup mode = null!;
    StackPanel citePane = null!, claimsPane = null!;
    InputBox source = null!;
    AgentRunner citeRunner = null!, claimsRunner = null!;
    StackPanel citeResults = null!, claimResults = null!;
    ChipGroup style = null!;
    TextBlock citation = null!;
    List<FormattedCitation> formatted = new();

    public CitationsTab(IPanelActions host) : base(host)
    {
    }

    public override string Id => "citations";
    public override string Label => "Citations";
    public override string Glyph => Icons.Citations;

    protected override FrameworkElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Hint("Generate APA, MLA, Chicago and Harvard citations, and find claims that need a source."));
        mode = new ChipGroup(new[] { ("cite", "Cite a source", ""), ("finder", "Citation Finder", "") }, "cite") { Margin = new Thickness(0, 0, 0, 6) };
        root.Children.Add(mode);

        citePane = new StackPanel();
        source = new InputBox("URL or DOI to cite", 2000, placeholder: "Paste a URL or DOI") { Margin = new Thickness(0, 0, 0, 6) };
        source.Submitted += Cite;
        citePane.Children.Add(source);
        var cite = UiKit.Button("Cite", Cite, Kind.Primary);
        cite.HorizontalAlignment = HorizontalAlignment.Left;
        citePane.Children.Add(cite);
        citeRunner = new AgentRunner(Host);
        citePane.Children.Add(citeRunner);
        citeResults = new StackPanel { Visibility = Visibility.Collapsed };
        style = new ChipGroup(Styles, "harvard") { Margin = new Thickness(0, 2, 0, 4) };
        style.SelectedChanged += _ => RenderCitation();
        citeResults.Children.Add(style);
        citation = UiKit.Label("", 12.5, null, Theme.Text, wrap: true);
        citeResults.Children.Add(Widgets.Card(citation, Theme.Line, Theme.Raised));
        var copy = UiKit.Button("Copy", CopyCitation, Kind.Secondary);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        citeResults.Children.Add(copy);
        citePane.Children.Add(citeResults);
        root.Children.Add(citePane);

        claimsPane = new StackPanel { Visibility = Visibility.Collapsed };
        var find = UiKit.Button("Find claims that need sources", FindClaims, Kind.Primary);
        find.HorizontalAlignment = HorizontalAlignment.Left;
        claimsPane.Children.Add(find);
        claimsPane.Children.Add(Widgets.Fine("“Search Scholar” opens Google Scholar in your browser with a search for that claim."));
        claimsRunner = new AgentRunner(Host);
        claimsPane.Children.Add(claimsRunner);
        claimResults = new StackPanel();
        claimsPane.Children.Add(claimResults);
        root.Children.Add(claimsPane);

        mode.SelectedChanged += m =>
        {
            citePane.Visibility = m == "cite" ? Visibility.Visible : Visibility.Collapsed;
            claimsPane.Visibility = m == "finder" ? Visibility.Visible : Visibility.Collapsed;
        };
        return root;
    }

    void Cite()
    {
        string input = source.Text.Trim();
        if (input.Length == 0)
        {
            Host.Notice("Paste a URL or DOI first.");
            return;
        }
        citeResults.Visibility = Visibility.Collapsed;
        _ = citeRunner.Run("Looking it up…", ct => Host.Api.CitationLookupAsync(input, ct), ShowCitation, "Formatted in every style. Pick one to copy.");
    }

    internal void ShowCitation(CitationLookupResult r)
    {
        _ = View;
        formatted = r.Formatted;
        citeResults.Visibility = Visibility.Visible;
        RenderCitation();
    }

    void RenderCitation() => citation.Text = formatted.FirstOrDefault(f => f.Style == style.Selected)?.Text ?? "This style isn't available for that source.";

    void CopyCitation()
    {
        var f = formatted.FirstOrDefault(x => x.Style == style.Selected);
        if (f == null) return;
        try
        {
            Clipboard.SetText(f.Text);
            Host.Notice("Citation copied. Paste it into your references.");
        }
        catch (Exception)
        {
            Host.Notice("Couldn't use the clipboard just now.");
        }
    }

    void FindClaims()
    {
        claimResults.Children.Clear();
        _ = claimsRunner.Run(40_000, "check", "Finding claims that need sources… (usually 5–15 s)", s => AgentRunner.TooFewWords(s, 15),
            (scope, ct) => Host.Api.ClaimsAsync(scope.Text, ct), ShowClaims);
    }

    internal void ShowClaims(List<ClaimToCite> claims, TextScope scope)
    {
        _ = View;
        mode.Select("finder");
        claimResults.Children.Clear();
        if (claims.Count == 0)
        {
            claimResults.Children.Add(UiKit.Label("No claims needing citations were found.", 12.5, null, Theme.Muted, wrap: true));
            return;
        }
        foreach (var c in claims)
        {
            bool needs = c.Strength == "needs_citation";
            var brush = Scales.Brush(needs ? Level.Bad : Level.Warn);
            var s = new StackPanel();
            s.Children.Add(UiKit.Label(needs ? "NEEDS A CITATION" : "CITATION RECOMMENDED", 10.5, FontWeights.SemiBold, brush));
            s.Children.Add(UiKit.Label($"\u201C{c.Claim}\u201D", 12.5, FontWeights.SemiBold, Theme.Text, wrap: true));
            s.Children.Add(UiKit.Label(c.Reason, 12, null, Theme.Muted, wrap: true));
            string query = c.Query.Length > 0 ? c.Query : c.Claim;
            var search = UiKit.Button("Search Scholar", () => Host.OpenUrl(ScholarUrl(query)), Kind.Secondary, $"Search Google Scholar for: {query}");
            search.HorizontalAlignment = HorizontalAlignment.Left;
            search.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(search);
            claimResults.Children.Add(Widgets.Card(s, brush));
        }
    }

    internal static string ScholarUrl(string query) => "https://scholar.google.com/scholar?q=" + Uri.EscapeDataString(query.Trim());

    public override void OnNewField()
    {
        claimsRunner.Reset();
        claimResults.Children.Clear(); // claims belong to the old text; a citation doesn't, so it stays
    }
}
