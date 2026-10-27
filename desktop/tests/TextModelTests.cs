using System.Collections.Generic;
using System.Linq;
using NimbleWrite.Desktop.Core;
using NimbleWrite.Desktop.UI;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

public class TextModelTests
{
    static Issue I(string paragraph, string original, string replacement, string type = "spelling", string category = "correctness") => new()
    {
        Offset = paragraph.IndexOf(original, System.StringComparison.Ordinal),
        Length = original.Length,
        Original = original,
        Replacements = new List<string> { replacement },
        Type = type,
        Category = category,
    };

    static PlacedIssue P(string text, string original, string replacement = "x")
    {
        var p = TextModel.SplitParagraphs(text).First(p => p.Text.Contains(original));
        return TextModel.Place(p, I(p.Text, original, replacement))!;
    }

    [Fact]
    public void SplitsParagraphsOnEveryKindOfLineBreak()
    {
        var paras = TextModel.SplitParagraphs("First one.\r\nSecond one.\n\n   \rThird\vFourth\u2029...");
        Assert.Equal(new[] { "First one.", "Second one.", "Third", "Fourth" }, paras.Select(p => p.Text));
        Assert.Equal(new[] { 0, 12, 29, 35 }, paras.Select(p => p.Start));
    }

    [Fact]
    public void PlacesIssuesAtAbsoluteOffsetsAndRejectsMismatches()
    {
        const string text = "Hello there.\nWe saw the libary.";
        var para = TextModel.SplitParagraphs(text)[1];
        var placed = TextModel.Place(para, I(para.Text, "libary", "library"));
        Assert.NotNull(placed);
        Assert.Equal(text.IndexOf("libary", System.StringComparison.Ordinal), placed!.Start);
        var wrong = I(para.Text, "libary", "library");
        wrong.Offset = 2;
        Assert.Null(TextModel.Place(para, wrong));
    }

    [Fact]
    public void CarryOverKeepsSuggestionsAroundAnEditAndShiftsLaterOnes()
    {
        const string before = "Teh cat sat on the mat. It was hapy.";
        var issues = new[] { P(before, "Teh"), P(before, "hapy") };
        const string after = "Teh cat sat on the big mat. It was hapy.";
        var kept = TextModel.CarryOver(before, after, issues);
        Assert.Equal(2, kept.Count);
        Assert.Equal(0, kept[0].Start);
        Assert.Equal(after.IndexOf("hapy", System.StringComparison.Ordinal), kept[1].Start);
    }

    [Fact]
    public void CarryOverDropsASuggestionWhoseWordIsBeingTyped()
    {
        const string before = "I saw the libary";
        var issues = new[] { P(before, "libary") };
        Assert.Empty(TextModel.CarryOver(before, "I saw the libaryx", issues));
        Assert.Empty(TextModel.CarryOver(before, "I saw the libry", issues));
        Assert.Single(TextModel.CarryOver(before, "I saw the libary.", issues));
    }

    [Fact]
    public void CarryOverDropsAFixOnceItIsInSoItCanNeverBeAppliedTwice()
    {
        // Seen live: "Also" -> "Also," stayed on offer after it was accepted, and each click added a comma ("Also,,,,").
        const string before = "Also if I open a document in Notepad, will it be checked?";
        var comma = P(before, "Also", "Also,");
        Assert.Empty(TextModel.CarryOver(before, "Also, if I open a document in Notepad, will it be checked?", new[] { comma }));
        // A word added in front of the original, accepted or typed by hand, counts too.
        const string b2 = "The issue was the folder I chose.";
        Assert.Empty(TextModel.CarryOver(b2, "The big issue was the folder I chose.", new[] { P(b2, "issue", "big issue") }));
        // Typing something else next to it doesn't: "libary." still needs "library".
        Assert.Single(TextModel.CarryOver(before, "Also if I open a document in Notepad, will it be checked??", new[] { comma }));
    }

    [Fact]
    public void AFixTheTextAlreadyContainsIsNeverPlaced()
    {
        // An older server can still send "create" -> "create." for text that already ends "create.".
        const string text = "The only problem was the folder it could not create.";
        var para = TextModel.SplitParagraphs(text).Single();
        Assert.Null(TextModel.Place(para, I(text, "create", "create.", "punctuation")));
        Assert.NotNull(TextModel.Place(para, I(text, "create", "make", "word_choice")));
    }

    [Theory]
    [InlineData("It could not create.", 13, "create", "create.", true)]
    [InlineData("Also, if I open it", 0, "Also", "Also,", true)]
    [InlineData("Also if I open it", 0, "Also", "Also,", false)]
    [InlineData("The big issue was it", 8, "issue", "big issue", true)]
    [InlineData("The issue was it", 4, "issue", "big issue", false)]
    [InlineData("the the cat", 0, "the the", "the", false)]
    [InlineData("the cat", 0, "the", "The", false)]
    [InlineData("its here and it's here", 0, "its", "it's", false)]
    [InlineData("end.", 0, "end", "end.", true)]
    [InlineData("end", 0, "end", "end.", false)]
    public void AlreadyAppliedMatchesTheServer(string text, int start, string original, string replacement, bool expected)
        => Assert.Equal(expected, TextModel.AlreadyApplied(text, start, original, replacement));

    [Fact]
    public void LocatePrefersTheExactSpotThenTheNearestOccurrence()
    {
        const string text = "the cat and the dog and the bird";
        Assert.Equal(12, TextModel.Locate(text, 12, "the"));
        Assert.Equal(24, TextModel.Locate(text, 22, "the"));
        Assert.Equal(-1, TextModel.Locate(text, 5, "fish"));
    }

    [Fact]
    public void CountsOccurrencesBeforeAnOffset()
    {
        Assert.Equal(2, TextModel.OccurrencesBefore("the cat and the dog and the bird", "the", 24));
        Assert.Equal(0, TextModel.OccurrencesBefore("the cat", "the", 0));
    }

    [Fact]
    public void GraphemeIndexCountsEmojiAndCrLfAsOneCharacter()
    {
        const string text = "Hi 👋🏽 there\r\nyou";
        Assert.Equal(3, TextModel.GraphemeIndex(text, 3));
        Assert.Equal(5, TextModel.GraphemeIndex(text, text.IndexOf("there", System.StringComparison.Ordinal)));
        Assert.Equal(11, TextModel.GraphemeIndex(text, text.IndexOf("you", System.StringComparison.Ordinal)));
        Assert.True(TextModel.IsSimple("plain text", 10));
        Assert.False(TextModel.IsSimple(text, text.Length));
    }

    [Fact]
    public void CountsWordsLikeTheWebApp()
    {
        Assert.Equal(0, TextModel.CountWords("  ...  "));
        Assert.Equal(4, TextModel.CountWords("Don't stop me now!"));
    }

    [Fact]
    public void KeepEdgesPreservesSurroundingWhitespaceAndStripsAddedQuotes()
    {
        Assert.Equal(" Better words. ", TextModel.KeepEdges(" bad words ", "Better words."));
        Assert.Equal("Hello there.", TextModel.KeepEdges("hello there", "\"Hello there.\""));
        Assert.Equal("\"Quoted\" text", TextModel.KeepEdges("\"quoted\" text", "\"Quoted\" text"));
    }

    [Fact]
    public void HashIsStable()
    {
        Assert.Equal(TextModel.Hash("abc"), TextModel.Hash("abc"));
        Assert.NotEqual(TextModel.Hash("abc"), TextModel.Hash("abd"));
        Assert.Equal("e71fa2190541574b", TextModel.Hash("abc"));
    }
}

public class SseParserTests
{
    [Fact]
    public void ParsesNamedEventsAndMultiLineData()
    {
        var p = new SseParser();
        var events = new List<SseEvent>();
        foreach (var line in new[] { "event: meta", "data: {\"model\":\"m\"}", "", ": comment", "event: delta", "data: {\"t\":", "data: \"Hi\"}", "", "", "event: done", "data: {}", "" })
        {
            if (p.Feed(line) is { } e) events.Add(e);
        }
        Assert.Equal(new[] { "meta", "delta", "done" }, events.Select(e => e.Event));
        Assert.Equal("{\"t\":\n\"Hi\"}", events[1].Data);
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+N", 0x3u, 0x4Eu)]
    [InlineData("ctrl + shift + 7", 0x6u, 0x37u)]
    [InlineData("Win+Alt+F2", 0x9u, 0x71u)]
    public void ParsesShortcuts(string spec, uint mods, uint vk)
    {
        Assert.True(HotkeyListener.TryParse(spec, out uint m, out uint v));
        Assert.Equal(mods, m);
        Assert.Equal(vk, v);
    }

    [Theory]
    [InlineData("N")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Banana")]
    public void RejectsShortcutsWithoutAModifierOrKey(string spec) => Assert.False(HotkeyListener.TryParse(spec, out _, out _));

    [Fact]
    public void TrayListsTheShortcutAsWorkingOnlyWhenWindowsAcceptedIt()
    {
        Assert.Equal("Assistant shortcut: Ctrl+Alt+N", Tray.ShortcutLabel("Ctrl+Alt+N", registered: true));
        Assert.Equal("Ctrl+Alt+N is used by another app: click the N badge instead", Tray.ShortcutLabel("Ctrl+Alt+N", registered: false));
        Assert.Equal("Shortcut \"Ctrl+Banana\" in desktop.json isn't valid: click the N badge instead", Tray.ShortcutLabel("Ctrl+Banana", registered: false));
    }
}

public class ApiClientTests
{
    [Theory]
    [InlineData("http://127.0.0.1:4317")]
    [InlineData("http://127.0.0.1:4317/")]
    public void OpensDocumentsOnTheEditorsHashRoute(string server)
    {
        // The web editor routes on the hash (src/router.ts): a plain /doc/<id> would show the documents list instead.
        using var api = new ApiClient(server);
        Assert.Equal("http://127.0.0.1:4317/#/doc/0f8fad5b-d9cb-469f-a165-70867728950e", api.DocUrl("0f8fad5b-d9cb-469f-a165-70867728950e"));
    }
}

public class DesktopConfigTests
{
    [Fact]
    public void TerminalsAndPasswordManagersAreOffByDefault()
    {
        var c = new DesktopConfig();
        Assert.True(c.IsAppDisabled("windowsterminal"));
        Assert.True(c.IsAppDisabled("KeePassXC"));
        Assert.False(c.IsAppDisabled("Hermes"));
        Assert.False(c.IsAppDisabled("chrome"));
    }
}

public class ApplyRepairTests
{
    [Fact]
    public void LandedIsWhatWasTypedOnlyWhenEverythingAroundItIsIntact()
    {
        const string before = "but it dont matter now.";
        int at = before.IndexOf("dont", System.StringComparison.Ordinal);
        Assert.Equal("does ttt", TextModel.Landed(before, "but it does ttt matter now.", at, 4, 40));
        Assert.Null(TextModel.Landed(before, "but at does ttt matter now.", at, 4, 40));
        Assert.Null(TextModel.Landed(before, "but it does ttt matters now.", at, 4, 40));
        Assert.Null(TextModel.Landed(before, "but it does ttt matter now.", at, 4, 5));
        Assert.Null(TextModel.Landed(before, before.Remove(at, 4), at, 4, 40));
        Assert.Equal("doesn\u2019t", TextModel.Landed("it\u00A0dont x", "it doesn\u2019t x", 3, 4, 40));
    }
}

public class PrivacyGuardTests
{
    [Theory]
    [InlineData("NVIDIA_API_KEY=nvapi-abcdefghijklmnopqrstuvwxyz0123")]
    [InlineData("export OPENAI_API_KEY=\"sk-proj-abcdefghij0123456789klmnop\"")]
    [InlineData("$env:HF_TOKEN = 'hf_abcdefghijklmnopqrstuvwxyz012345'")]
    [InlineData("db_password = hunter2hunter2")]
    [InlineData("Some notes\nPassword: hunter2\nMore notes")]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nabc")]
    [InlineData("my token is ghp_abcdefghijklmnopqrstuvwxyz0123456789 ok")]
    [InlineData("AKIAABCDEFGHIJKLMNOP")]
    [InlineData("PASSWORD=hunter2hunter2")]
    [InlineData("TOKEN=abc12345xyz")]
    [InlineData("my ElevenLabs key is sk_3f2a9c1d8e7b6a5f4e3d2c1b0a9f8e7d6c5b4a3f2e1d0c9b")]
    [InlineData("Bearer aB3dE5fG7hI9jK1lM3nO5pQ7rS9tU1vW and more")]
    [InlineData("bot 123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsawI")]
    public void SpotsSecrets(string text) => Assert.True(TextModel.LooksSecret(text));

    [Theory]
    [InlineData("I lost the key to the shed, so the password for the wifi is on the fridge.")]
    [InlineData("Their going to the libary tomorow to return the the books.")]
    [InlineData("The sk-8 park opens at nine, and the skateboarding-championship-finals start at ten.")]
    [InlineData("Key: bring snacks\nMonkey: banana")]
    [InlineData("Here's the doc: https://drive.google.com/file/d/1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgvE2upms/view?usp=sharing")]
    [InlineData("Commit 3f2a9c1d8e7b6a5f4e3d2c1b0a9f8e7d6c5b4a3f fixed it; doc 954bc22c-b5f2-4fa8-96da-82588efbda22.")]
    [InlineData("")]
    public void LeavesOrdinaryTextAlone(string text) => Assert.False(TextModel.LooksSecret(text));

    [Theory]
    [InlineData("$env:PATH = \"C:\\tools;\" + $env:PATH")]
    [InlineData("function Start-Server($port) {")]
    [InlineData("def check(text):")]
    [InlineData("const x = require('y');")]
    [InlineData("}")]
    [InlineData("\"name\": \"nimblewrite\",")]
    [InlineData("<div class=\"card\">Hello</div>")]
    [InlineData("if (x > 3) return;")]
    [InlineData("PORT=4317")]
    public void SpotsCode(string line) => Assert.True(TextModel.LooksLikeCode(line));

    [Theory]
    [InlineData("Their going to the libary tomorow, and we met in order to talk.")]
    [InlineData("**Important:** read the *whole* thing before Friday #goals")]
    [InlineData("See https://example.com/some/long/path_with_underscores?x=1&y=2 for the details.")]
    [InlineData("let me know what you think (no rush).")]
    [InlineData("Note: the meeting moved to 3pm.")]
    [InlineData("Email me at martin@example.co.uk when you're free.")]
    [InlineData("I came; I saw; I conquered.")]
    [InlineData("It cost $5 for the lot, which was a bargain.")]
    [InlineData("I love you <3")]
    [InlineData("As shown in [1] and [2], the results hold.")]
    [InlineData("London -> Paris -> Rome was quick.")]
    [InlineData("if (when) you get a chance, call me")]
    public void LeavesProseAlone(string line) => Assert.False(TextModel.LooksLikeCode(line));
}
