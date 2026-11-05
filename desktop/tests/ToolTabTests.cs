using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NimbleWrite.Desktop.Core;
using NimbleWrite.Desktop.UI.Panels;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

/// <summary>Citations, Plagiarism, Image prompt, Performance, Tune and Documents: pure helpers and the lists copied from the web.</summary>
public sealed class ToolTabTests
{
    [Fact]
    public void CitationStylesAreTheWebsInOrder()
    {
        Assert.Equal(new[] { "apa", "mla", "chicago", "harvard" }, CitationsTab.Styles.Select(s => s.Id));
        Assert.Equal(new[] { "APA 7", "MLA 9", "Chicago", "Harvard" }, CitationsTab.Styles.Select(s => s.Label));
        Assert.Equal("https://scholar.google.com/scholar?q=screen%20time%20%26%20anxiety", CitationsTab.ScholarUrl("screen time & anxiety"));
    }

    [Fact]
    public void PlagiarismSendsPastedTextAndUpToEightLinks()
    {
        string urls = "https://a.example/x  not-a-link\nhttp://b.example " + string.Join(' ', Enumerable.Range(0, 10).Select(i => $"https://c{i}.example"));
        var r = PlagiarismTab.BuildRequest("My essay.", includeDocs: false, web: true, searchConfigured: false, "  Source text  ", urls);
        Assert.Equal("My essay.", r.Text);
        Assert.False(r.IncludeDocs);
        Assert.False(r.Web); // asked for, but web search isn't set up
        Assert.Equal("Pasted source", r.Sources[0].Title);
        Assert.Equal("  Source text  ", r.Sources[0].Text);
        Assert.Equal(new[] { "https://a.example/x", "http://b.example" }, r.Sources.Skip(1).Take(2).Select(s => s.Url));
        Assert.Equal(9, r.Sources.Count); // pasted + 8 links

        var plain = PlagiarismTab.BuildRequest("Text", includeDocs: true, web: true, searchConfigured: true, " ", "");
        Assert.True(plain.Web);
        Assert.Empty(plain.Sources);
    }

    [Fact]
    public void ImagePromptDefaultsToRestructuringForFlux3Klein()
    {
        Assert.Equal("flux3-klein", ImagePromptTab.Targets[0].Id);
        Assert.False(ImagePromptTab.EnhanceByDefault);
    }

    [Theory]
    [InlineData(0, "0 sec")]
    [InlineData(45, "45 sec")]
    [InlineData(60, "1 min")]
    [InlineData(200, "3 min 20 sec")]
    public void DurationsReadNaturally(int seconds, string want) => Assert.Equal(want, PerformanceTab.Duration(seconds));

    [Fact]
    public void ScoreCountsComeFromTheOpenSuggestions()
    {
        var issues = new[] { "correctness", "correctness", "clarity", "delivery" }
            .Select((c, i) => new PlacedIssue($"i{i}", i, new Issue { Category = c, Length = 1, Original = "x" }, ""))
            .ToList();
        var counts = PerformanceTab.CountsByCategory(issues);
        Assert.Equal(new Dictionary<string, int> { ["correctness"] = 2, ["clarity"] = 1, ["engagement"] = 0, ["delivery"] = 1 }, counts);
    }

    [Theory]
    [InlineData("2026-11-05", "Thu")]
    [InlineData("2026-11-08", "Sun")]
    [InlineData("nonsense", "")]
    public void InsightDaysAreShortNames(string date, string want) => Assert.Equal(want, PerformanceTab.DayName(date));

    [Fact]
    public void TuneChangesAreDescribedInPlainWords()
    {
        Assert.Equal("Stop flagging: Passive voice", ReviewTab.Describe(new TuneChange { Kind = "mute", Type = "passive_voice", Title = "Passive voice" }));
        Assert.Equal("Flag again: passive_voice", ReviewTab.Describe(new TuneChange { Kind = "unmute", Type = "passive_voice" }));
        Assert.Equal("Add to dictionary: Nemotron", ReviewTab.Describe(new TuneChange { Kind = "dictionary", Word = "Nemotron" }));
        Assert.Equal("Style rule: utilise → use", ReviewTab.Describe(new TuneChange { Kind = "style_rule", Find = "utilise", Replace = "use" }));
        Assert.Equal("Remember: I write for engineers", ReviewTab.Describe(new TuneChange { Kind = "instruction", Text = "I write for engineers" }));
    }

    [Fact]
    public void DocumentsFilterByTitleOrSnippetAndHideTheBin()
    {
        var docs = new List<DocSummary>
        {
            new() { Id = "1", Title = "Cover letter", Snippet = "Dear hiring manager", UpdatedAt = 3 },
            new() { Id = "2", Title = "Notes", Snippet = "Letter to the council", UpdatedAt = 5 },
            new() { Id = "3", Title = "Old letter", Snippet = "", UpdatedAt = 9, DeletedAt = 10 },
        };
        Assert.Equal(new[] { "2", "1" }, DocumentsTab.Filter(docs, "  LETTER ").Select(d => d.Id));
        Assert.Equal(new[] { "2", "1" }, DocumentsTab.Filter(docs, "").Select(d => d.Id));
        Assert.Empty(DocumentsTab.Filter(docs, "zzz"));
    }

    [Fact]
    public void DocumentTimesAreRelative()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        long ms(TimeSpan ago) => (now - ago).ToUnixTimeMilliseconds();
        Assert.Equal("just now", DocumentsTab.Ago(ms(TimeSpan.FromSeconds(20)), now));
        Assert.Equal("5 min ago", DocumentsTab.Ago(ms(TimeSpan.FromMinutes(5)), now));
        Assert.Equal("3 h ago", DocumentsTab.Ago(ms(TimeSpan.FromHours(3)), now));
        Assert.Equal("2 days ago", DocumentsTab.Ago(ms(TimeSpan.FromDays(2)), now));
        Assert.Equal("12 Oct 2026", DocumentsTab.Ago(ms(TimeSpan.FromDays(24)), now));
    }

    [Fact]
    public void ListsCopiedFromTheWebEditorStillMatch()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "editor", "panels", "AgentPanels.tsx"));
        string[] Strings(string name) => Regex.Matches(Regex.Match(src, $@"const {name} = \[(.*?)\];", RegexOptions.Singleline).Groups[1].Value, "'([^']+)'").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(Strings("READERS"), ReaderTab.Readers);
        Assert.Equal(Strings("LEVELS"), GraderTab.Levels);

        string targets = Regex.Match(src, @"const IMAGE_TARGETS[^=]*= \[(.*?)\];", RegexOptions.Singleline).Groups[1].Value;
        var web = Regex.Matches(targets, @"\{ id: '([^']+)', label: '([^']+)', hint: ([""'])(.*?)\3 \}").Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[4].Value)).ToArray();
        Assert.Equal(5, web.Length);
        Assert.Equal(web, ImagePromptTab.Targets);

        var quick = Regex.Matches(src, @"\{ task: '([^']+)', label: '([^']+)', kind:").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToArray();
        Assert.Equal(quick, AssistantTab.QuickActions);
    }

    static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "shared", "meta.ts"))) return d.FullName;
        }
        throw new InvalidOperationException("Couldn't find the repository root above the test binaries.");
    }
}
