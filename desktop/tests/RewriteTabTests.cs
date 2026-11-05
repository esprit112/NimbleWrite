using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading;
using NimbleWrite.Desktop.Core;
using NimbleWrite.Desktop.UI;
using NimbleWrite.Desktop.UI.Panels;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

/// <summary>Paraphrase, Humanize and Translate: what each choice sends, and the lists copied from shared/meta.ts.</summary>
public sealed class RewriteTabTests
{
    [Fact]
    public void ParaphraseSendsItsModeAndOnlyACustomVoiceAsTheInstruction()
    {
        var (r, problem) = RewriteTab.BuildRequest(RewriteKind.Paraphrase, "formal", "ignored voice", "French", null);
        Assert.Null(problem);
        Assert.Equal(new WriteRequest("paraphrase", "", Mode: "formal"), r);

        (r, problem) = RewriteTab.BuildRequest(RewriteKind.Paraphrase, "custom", "  witty British tech journalist ", "French", null);
        Assert.Null(problem);
        Assert.Equal(new WriteRequest("paraphrase", "", Instruction: "witty British tech journalist", Mode: "custom"), r);
    }

    [Fact]
    public void ACustomVoiceHasToBeDescribed()
    {
        var (r, problem) = RewriteTab.BuildRequest(RewriteKind.Paraphrase, "custom", "   ", "French", null);
        Assert.Null(r);
        Assert.Equal("Describe the voice you want first.", problem);
    }

    [Fact]
    public void AnUnknownModeFallsBackToStandard() =>
        Assert.Equal("standard", RewriteTab.BuildRequest(RewriteKind.Paraphrase, "pirate", "", "French", null).Request!.Mode);

    [Fact]
    public void HumanizeSendsJustTheTask() =>
        Assert.Equal(new WriteRequest("humanize", ""), RewriteTab.BuildRequest(RewriteKind.Humanize, "formal", "voice", "French", "German").Request);

    [Fact]
    public void TranslateSendsTheTargetAndASourceOnlyWhenOneWasChosen()
    {
        Assert.Equal(new WriteRequest("translate", "", TargetLanguage: "Spanish"), RewriteTab.BuildRequest(RewriteKind.Translate, "", "", "Spanish", null).Request);
        Assert.Equal(new WriteRequest("translate", "", TargetLanguage: "Spanish", SourceLanguage: "English"), RewriteTab.BuildRequest(RewriteKind.Translate, "", "", "Spanish", "English").Request);
        var (r, problem) = RewriteTab.BuildRequest(RewriteKind.Translate, "", "", "German", "German");
        Assert.Null(r);
        Assert.Equal("Pick a different language to translate into.", problem);
    }

    [Fact]
    public void RecentLanguagesPutTheLatestFirstWithoutRepeats()
    {
        Assert.Equal(new[] { "Welsh", "French", "Spanish" }, Languages.PushRecent(new[] { "French", "Spanish", "German" }, "Welsh"));
        Assert.Equal(new[] { "German", "French", "Spanish" }, Languages.PushRecent(new[] { "French", "Spanish", "German" }, "German"));
        Assert.Equal(new[] { "Thai" }, Languages.PushRecent(Array.Empty<string>(), "Thai"));
        Assert.Equal(new[] { "French", "Spanish" }, Languages.PushRecent(new[] { "French", "Spanish" }, "Klingon")); // not a language we offer
    }

    [Fact]
    public void TheListsMatchTheWebEditors()
    {
        string meta = File.ReadAllText(Path.Combine(RepoRoot(), "shared", "meta.ts"));
        string langBlock = Regex.Match(meta, @"export const LANGUAGES = \[(.*?)\];", RegexOptions.Singleline).Groups[1].Value;
        var webLanguages = Regex.Matches(langBlock, "'([^']+)'").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(31, webLanguages.Length);
        Assert.Equal(webLanguages, Languages.All);

        string modeBlock = Regex.Match(meta, @"export const PARAPHRASE_MODES[^=]*= \[(.*?)\];", RegexOptions.Singleline).Groups[1].Value;
        var webModes = Regex.Matches(modeBlock, @"id: '([^']+)', label: '([^']+)', hint: '([^']+)'").Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)).ToArray();
        Assert.Equal(9, webModes.Length);
        Assert.Equal(webModes, RewriteTab.ParaphraseModes);
    }

    [Fact]
    public void SourceLanguagesAreEnglishThenTheOthers()
    {
        Assert.Equal("English", Languages.Sources[0]);
        Assert.DoesNotContain(Languages.Sources, l => l.StartsWith("English (", StringComparison.Ordinal));
        Assert.Equal(Languages.All.Length - 2 + 1, Languages.Sources.Length);
    }

    [Fact]
    public void ThePickerFiltersSelectsAndRemembersWhatWasPicked() => OnSta(() =>
    {
        var p = new LanguagePicker("Translate into", Languages.All, new[] { "French", "Spanish" }, "French", autoLabel: null);
        int changes = 0;
        p.SelectedChanged += _ => changes++;
        Assert.Equal(new[] { "French", "Spanish" }, p.QuickChoices);
        Assert.False(p.Expanded);

        p.Filter("chin");
        Assert.True(p.Expanded);
        Assert.Equal(new[] { "Chinese (Simplified)", "Chinese (Traditional)" }, p.Matches);
        p.Filter("  WELSH ");
        Assert.Equal(new[] { "Welsh" }, p.Matches);

        p.Choose("Welsh");
        Assert.Equal("Welsh", p.Selected);
        Assert.Equal(1, changes);
        Assert.False(p.Expanded); // the full list folds away once you've picked
        Assert.Equal(new[] { "French", "Spanish", "Welsh" }, p.QuickChoices);

        p.Choose("Welsh");
        p.Choose("Not a language");
        Assert.Equal(1, changes);
    });

    [Fact]
    public void TheFromPickerStartsOnDetect() => OnSta(() =>
    {
        var p = new LanguagePicker("Translate from", Languages.Sources, new[] { "English" }, null, autoLabel: "Detect");
        Assert.Null(p.Selected);
        p.Choose("English");
        Assert.Equal("English", p.Selected);
        p.Choose(null);
        Assert.Null(p.Selected);
    });

    [Fact]
    public void EachKindHasItsOwnTabIdAndGlyph() => OnSta(() =>
    {
        var host = new PanelTests.FakeHost();
        var tabs = new[] { RewriteKind.Paraphrase, RewriteKind.Humanize, RewriteKind.Translate }.Select(k => new RewriteTab(host, k)).ToList();
        Assert.Equal(new[] { "paraphrase", "humanize", "translate" }, tabs.Select(t => t.Id));
        Assert.Equal(3, tabs.Select(t => t.Glyph).Distinct().Count());
    });

    static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "shared", "meta.ts"))) return d.FullName;
        }
        throw new InvalidOperationException("Couldn't find the repository root (shared/meta.ts) above the test binaries.");
    }

    static void OnSta(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        failure?.Throw();
    }
}
