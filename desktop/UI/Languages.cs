using System;
using System.Collections.Generic;
using System.Linq;

namespace NimbleWrite.Desktop.UI;

/// <summary>The languages the Translate tool offers. Copied from LANGUAGES in shared/meta.ts; a test keeps them in step.</summary>
internal static class Languages
{
    public static readonly string[] All =
    {
        "Arabic", "Chinese (Simplified)", "Chinese (Traditional)", "Czech", "Danish", "Dutch", "English (British)", "English (American)",
        "Finnish", "French", "German", "Greek", "Hindi", "Hungarian", "Indonesian", "Italian", "Japanese", "Korean", "Norwegian",
        "Polish", "Portuguese (Brazil)", "Portuguese (Portugal)", "Romanian", "Russian", "Spanish", "Swedish", "Thai", "Turkish",
        "Ukrainian", "Vietnamese", "Welsh",
    };

    /// <summary>What text can be translated from: plain English, then every other language (as the web editor's From list).</summary>
    public static readonly string[] Sources = new[] { "English" }.Concat(All.Where(l => !l.StartsWith("English", StringComparison.Ordinal))).ToArray();

    /// <summary><paramref name="language"/> first, then the others, at most <paramref name="max"/>. Unknown languages are ignored.</summary>
    public static List<string> PushRecent(IEnumerable<string> recent, string language, int max = 3)
    {
        var list = recent.Where(All.Contains).ToList();
        if (!All.Contains(language)) return list.Take(max).ToList();
        list.Remove(language);
        list.Insert(0, language);
        return list.Take(max).ToList();
    }
}
