using System;
using System.Collections.Generic;
using System.Linq;
using NimbleWrite.Desktop.Core;
using NimbleWrite.Desktop.UI.Panels;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

/// <summary>Pure helpers behind the agent tabs (Synonyms, Reader, Grader, Detector, …): colours, guards, and text maths.</summary>
public sealed class AgentTabTests
{
    static TextScope Sel(string full, string part) => new(full.IndexOf(part, StringComparison.Ordinal), part.Length, part, full, false);

    static TextScope Whole(string full) => new(0, full.Length, full, full, true);

    [Theory]
    [InlineData(80, "Good")]
    [InlineData(75, "Good")]
    [InlineData(60, "Warn")]
    [InlineData(54.9, "Bad")]
    public void GradesAreGreenAmberRedLikeTheWeb(double score, string want) => Assert.Equal(want, Scales.Grade(score).ToString());

    [Theory]
    [InlineData(10, "Good")]
    [InlineData(35, "Warn")]
    [InlineData(60, "Bad")]
    public void MoreAiLikeIsWorse(double score, string want) => Assert.Equal(want, Scales.AiLikely(score).ToString());

    [Theory]
    [InlineData(5, "Good")]
    [InlineData(10, "Warn")]
    [InlineData(30, "Bad")]
    public void MoreMatchingIsWorse(double pct, string want) => Assert.Equal(want, Scales.Matches(pct).ToString());

    [Fact]
    public void SynonymsNeedASelectionOfUpToThreeWords()
    {
        Assert.Equal("Select a word first (double-click it).", SynonymsTab.SelectionProblem(Whole("The quick brown fox.")));
        Assert.Equal("Select just a word or a short phrase (up to 3 words).", SynonymsTab.SelectionProblem(Sel("The quick brown fox jumps.", "quick brown fox jumps")));
        Assert.Equal("Select just a word or a short phrase (up to 3 words).", SynonymsTab.SelectionProblem(Sel("The quick brown fox.", "  ")));
        Assert.Null(SynonymsTab.SelectionProblem(Sel("The quick brown fox.", "quick")));
        Assert.Null(SynonymsTab.SelectionProblem(Sel("The quick brown fox.", "quick brown fox")));
    }

    [Fact]
    public void SynonymsSendTheSentenceAroundTheWord()
    {
        const string text = "First one. Then the quick brown fox jumped! Last line\nNext paragraph.";
        var s = Sel(text, "quick");
        Assert.Equal("Then the quick brown fox jumped!", SynonymsTab.SentenceAround(s.FullText, s.Start, s.Length));
        var last = Sel(text, "Last");
        Assert.Equal("Last line", SynonymsTab.SentenceAround(last.FullText, last.Start, last.Length));
        Assert.True(SynonymsTab.SentenceAround(new string('a', 5000), 2500, 1).Length <= 1200);
    }

    [Theory]
    [InlineData("quick", "fast", "fast")]
    [InlineData("Quick", "fast", "Fast")]
    [InlineData("QUICK", "fast", "FAST")]
    [InlineData("quick", "London", "London")]
    [InlineData("A", "one", "One")]
    public void ASynonymTakesTheOriginalsCapitals(string original, string synonym, string want) => Assert.Equal(want, SynonymsTab.MatchCase(original, synonym));

    [Fact]
    public void LongerToolsNeedEnoughWords()
    {
        Assert.Equal("Write a bit more first (at least 15 words).", AgentRunner.TooFewWords(Whole("Just a few words here."), 15));
        Assert.Null(AgentRunner.TooFewWords(Whole(string.Join(' ', Enumerable.Repeat("word", 15))), 15));
    }

    [Fact]
    public void AFlaggedPassageBecomesItsOwnScopeIfTheTextIsUnchanged()
    {
        const string text = "I wrote this myself. In today's fast-paced world, synergy is key. The end.";
        var checkedScope = Whole(text);
        var p = DetectorTab.PassageScope(checkedScope, "In today's fast-paced world, synergy is key.");
        Assert.NotNull(p);
        Assert.Equal(text.IndexOf("In today", StringComparison.Ordinal), p!.Start);
        Assert.False(p.Whole);
        Assert.Equal(text, p.FullText);
        Assert.Null(DetectorTab.PassageScope(checkedScope, "A sentence that isn't there."));
        Assert.Null(DetectorTab.PassageScope(checkedScope, "  "));
    }

    [Fact]
    public void TheEngineOnlyRewritesAGivenScopeIfTheFieldStillHoldsThatText()
    {
        var s = Sel("Alpha beta gamma.", "beta");
        Assert.Null(Engine.FixedScopeProblem("Alpha beta gamma.", s));
        Assert.Equal("The text has changed since it was checked. Run the check again.", Engine.FixedScopeProblem("Alpha BETA gamma.", s));
        Assert.Equal("Click into a text box first.", Engine.FixedScopeProblem(null, s));
    }

    [Fact]
    public void GradeSummaryReadsLikeTheWeb() => Assert.Equal("Estimated 78/100", GraderTab.ScoreLine(78.4));
}
