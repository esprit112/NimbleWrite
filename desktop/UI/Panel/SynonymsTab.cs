using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>Synonyms for the selected word (or short phrase), grouped by meaning; click one to put it in place.</summary>
internal sealed class SynonymsTab : PanelTab
{
    AgentRunner runner = null!;
    StackPanel results = null!;

    public SynonymsTab(IPanelActions host) : base(host)
    {
    }

    public override string Id => "synonyms";
    public override string Label => "Synonyms";
    public override string Glyph => Icons.Synonyms;

    protected override FrameworkElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Hint("Select a word (or a short phrase) in your text, then find other ways to say it. The sentence around it helps pick the right meaning."));
        var go = UiKit.Button("Find synonyms", Go, Kind.Primary);
        go.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(go);
        runner = new AgentRunner(Host);
        root.Children.Add(runner);
        results = new StackPanel();
        root.Children.Add(results);
        return root;
    }

    void Go()
    {
        results.Children.Clear();
        _ = runner.Run(TextModel.MaxChars, "look up", "Finding synonyms…", SelectionProblem, async (scope, ct) =>
        {
            string dialect = (await Host.Api.GetWritingSettingsAsync(ct)).Dialect;
            return await Host.Api.SynonymsAsync(scope.Text.Trim(), SentenceAround(scope.FullText, scope.Start, scope.Length), dialect, ct);
        }, Show);
    }

    internal void Show(List<SynonymSense> senses, TextScope scope)
    {
        _ = View;
        results.Children.Clear();
        if (senses.All(s => s.Synonyms.Count == 0))
        {
            results.Children.Add(UiKit.Label($"No synonyms found for \u201C{scope.Text.Trim()}\u201D.", 12.5, null, Theme.Muted, wrap: true));
            return;
        }
        string original = scope.Text.Trim();
        foreach (var sense in senses.Where(s => s.Synonyms.Count > 0))
        {
            var box = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
            box.Children.Add(UiKit.Label(sense.Meaning, 12, FontWeights.SemiBold, Theme.Muted, wrap: true));
            var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var syn in sense.Synonyms.Distinct())
            {
                string word = MatchCase(original, syn);
                chips.Children.Add(UiKit.Spaced(UiKit.Button(word, () => Use(scope, word), Kind.Secondary, $"Replace \u201C{original}\u201D with \u201C{word}\u201D")));
            }
            box.Children.Add(chips);
            results.Children.Add(box);
        }
    }

    void Use(TextScope scope, string word)
    {
        string lead = scope.Text[..(scope.Text.Length - scope.Text.TrimStart().Length)];
        string trail = scope.Text[scope.Text.TrimEnd().Length..];
        Host.ApplyRewrite(scope, lead + word + trail, $"Replaced with \u201C{word}\u201D.");
        results.Children.Clear(); // the selection is gone now; these were for the old word
        runner.Reset();
    }

    /// <summary>Why the scope isn't a word to look up: nothing selected, or more than a short phrase.</summary>
    internal static string? SelectionProblem(TextScope scope)
    {
        if (scope.Whole) return "Select a word first (double-click it).";
        string t = scope.Text.Trim();
        int words = TextModel.CountWords(t);
        return words is < 1 or > 3 || t.Length > 60 ? "Select just a word or a short phrase (up to 3 words)." : null;
    }

    /// <summary>The sentence containing the selection (at most 1,200 characters), to tell the AI which meaning is meant.</summary>
    internal static string SentenceAround(string full, int start, int length)
    {
        int from = start;
        while (from > 0 && ".!?\n".IndexOf(full[from - 1]) < 0) from--;
        int to = Math.Min(full.Length, start + length);
        while (to < full.Length && full[to] != '\n')
        {
            to++;
            if (".!?".IndexOf(full[to - 1]) >= 0) break;
        }
        from = Math.Max(from, start - 600);
        to = Math.Min(Math.Min(to, start + length + 600), from + 1200); // the server takes at most 1,200
        return full[from..to].Trim();
    }

    /// <summary>Give <paramref name="synonym"/> the original's capitals ("Quick" → "Fast", "QUICK" → "FAST").</summary>
    internal static string MatchCase(string original, string synonym)
    {
        if (synonym.Length == 0 || original.Length == 0) return synonym;
        if (original.Length > 1 && original.Any(char.IsLetter) && original.Where(char.IsLetter).All(char.IsUpper)) return synonym.ToUpperInvariant();
        if (char.IsUpper(original[0])) return char.ToUpperInvariant(synonym[0]) + synonym[1..];
        return synonym;
    }

    public override void OnNewField()
    {
        runner.Reset();
        results.Children.Clear();
    }
}
