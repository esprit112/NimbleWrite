using NimbleWrite.Desktop.Core;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

/// <summary>
/// The checks every panel tool runs on the text before it goes to the AI (one place, so a new tool can't forget the
/// privacy guard): there must be text, not too much of it, and nothing that looks like a password or key.
/// </summary>
public class ScopeGuardTests
{
    static TextScope Whole(string t) => new(0, t.Length, t, t, true);

    static TextScope Selection(string full, string part) => new(full.IndexOf(part, System.StringComparison.Ordinal), part.Length, part, full, false);

    [Fact]
    public void NoFieldMeansClickIntoATextBoxFirst() =>
        Assert.Equal("Click into a text box first.", Engine.ScopeProblem(null, 12_000, "rewrite"));

    [Fact]
    public void EmptyOrBlankTextIsRefusedWithTheToolsVerb()
    {
        Assert.Equal("There's no text to rewrite yet.", Engine.ScopeProblem(Whole(""), 12_000, "rewrite"));
        Assert.Equal("There's no text to grade yet.", Engine.ScopeProblem(Whole("   \n "), 60_000, "grade"));
    }

    [Fact]
    public void TooMuchTextAsksForASelection()
    {
        var big = Whole(new string('a', 12_001));
        Assert.Equal("That's a lot of text to rewrite at once. Select a part of it first.", Engine.ScopeProblem(big, 12_000, "rewrite"));
        Assert.Null(Engine.ScopeProblem(big, 60_000, "check"));
    }

    [Fact]
    public void SecretsAnywhereInTheFieldBlockEvenASelection()
    {
        // The selection itself is harmless, but the field holds a key: never send any of it.
        var s = Selection("Notes for today.\nNVIDIA_API_KEY=nvapi-abcdefghijklmnopqrstuvwxyz0123", "Notes for today.");
        Assert.Equal("This looks like it contains passwords or keys, so NimbleWrite won't send it to the AI.", Engine.ScopeProblem(s, 12_000, "rewrite"));
    }

    [Fact]
    public void OrdinaryTextPasses() =>
        Assert.Null(Engine.ScopeProblem(Selection("Their going to the libary tomorow.", "libary"), 12_000, "rewrite"));
}
