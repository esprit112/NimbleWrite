using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NimbleWrite.Desktop.Core;

/// <summary>One paragraph of the focused field's text.</summary>
public sealed record Paragraph(int Start, string Text)
{
    public int End => Start + Text.Length;
}

/// <summary>Pure text logic shared by the engine and the tests: paragraphs, carrying suggestions over edits, offsets.</summary>
public static class TextModel
{
    public const int MaxChars = 30_000;
    public const int MaxParagraphChars = 6_000;
    public const int MinWords = 3;

    static bool IsBreak(char c) => c is '\n' or '\r' or '\v' or '\f' or '\a' or '\u2028' or '\u2029';

    /// <summary>Split at any line break (Word uses \r, \v and \a too). Paragraphs without letters are skipped.</summary>
    public static List<Paragraph> SplitParagraphs(string text)
    {
        var list = new List<Paragraph>();
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && !IsBreak(text[i])) continue;
            if (i > start)
            {
                string p = text.Substring(start, i - start);
                if (p.Any(char.IsLetter)) list.Add(new Paragraph(start, p));
            }
            start = i + 1;
        }
        return list;
    }

    public static int CountWords(string s)
    {
        int n = 0;
        bool inWord = false;
        foreach (char c in s)
        {
            bool w = char.IsLetterOrDigit(c) || c == '\'' || c == '’';
            if (w && !inWord) n++;
            inWord = w;
        }
        return n;
    }

    /// <summary>64-bit FNV-1a, as hex. Stable across runs (string.GetHashCode is not).</summary>
    public static string Hash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h.ToString("x16");
    }

    /// <summary>Identity used for "Dismiss": the same suggestion is hidden wherever it reappears this session.</summary>
    public static string Signature(Issue i) => $"{i.Type}\u001f{i.Original}\u001f{(i.Replacements.Count > 0 ? i.Replacements[0] : "")}";

    /// <summary>Place a paragraph-relative issue in the field, or null if it doesn't match the paragraph text.</summary>
    public static PlacedIssue? Place(Paragraph p, Issue i)
    {
        if (i.Length <= 0 || i.Offset < 0 || i.Offset + i.Length > p.Text.Length) return null;
        if (string.CompareOrdinal(p.Text, i.Offset, i.Original, 0, i.Length) != 0 || i.Original.Length != i.Length) return null;
        // A fix the text already contains is not a suggestion: accepting it would duplicate text ("create..").
        if (i.Replacements.Any(r => AlreadyApplied(p.Text, i.Offset, i.Original, r))) return null;
        int start = p.Start + i.Offset;
        return new PlacedIssue(Hash($"{start}|{i.Length}|{i.Type}|{i.Original}"), start, i, Signature(i));
    }

    /// <summary>
    /// True when the text around <paramref name="start"/> already reads like <paramref name="replacement"/>: a fix that
    /// only adds to the original ("Also" -> "Also,", "issue" -> "big issue") whose additions are already there.
    /// Applying it again would duplicate them ("Also,,"). Mirrors alreadyApplied in shared/text.ts.
    /// </summary>
    public static bool AlreadyApplied(string text, int start, string original, string replacement)
    {
        if (original.Length == 0 || replacement == original) return false;
        int k = replacement.IndexOf(original, StringComparison.Ordinal);
        if (k < 0) return false;
        int at = start - k;
        return at >= 0 && at + replacement.Length <= text.Length && string.CompareOrdinal(text, at, replacement, 0, replacement.Length) == 0;
    }

    /// <summary>
    /// After an edit, keep the suggestions outside the edited region (shifting those after it) so underlines don't
    /// flicker while typing. Suggestions touching the edit, or whose word got longer, are dropped, and so are fixes the
    /// edit has just made (the comma after "Also" is in, so "Also" -> "Also," is done).
    /// </summary>
    public static List<PlacedIssue> CarryOver(string before, string after, IEnumerable<PlacedIssue> issues)
    {
        int max = Math.Min(before.Length, after.Length);
        int p = 0;
        while (p < max && before[p] == after[p]) p++;
        int s = 0;
        while (s < max - p && before[before.Length - 1 - s] == after[after.Length - 1 - s]) s++;
        int oldEditEnd = before.Length - s;
        int delta = after.Length - before.Length;
        var kept = new List<PlacedIssue>();
        foreach (var i in issues)
        {
            int start;
            if (i.End <= p) start = i.Start;
            else if (i.Start >= oldEditEnd) start = i.Start + delta;
            else continue;
            int len = i.Issue.Length;
            if (start < 0 || start + len > after.Length || string.CompareOrdinal(after, start, i.Issue.Original, 0, len) != 0) continue;
            // "libary" followed by a newly typed letter is no longer the word that was flagged.
            if (len > 0 && IsWordChar(i.Issue.Original[len - 1]) && start + len < after.Length && IsWordChar(after[start + len])) continue;
            if (len > 0 && IsWordChar(i.Issue.Original[0]) && start > 0 && IsWordChar(after[start - 1])) continue;
            // The edit was this fix (accepted, or typed by hand): offering it again would add the comma twice.
            if (i.Issue.Replacements.Any(r => AlreadyApplied(after, start, i.Issue.Original, r))) continue;
            kept.Add(i with { Start = start });
        }
        return kept;
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '\'' || c == '’';

    /// <summary>Find <paramref name="expected"/> at <paramref name="start"/>, or its nearest occurrence (within 2,000 chars).</summary>
    public static int Locate(string text, int start, string expected)
    {
        if (expected.Length == 0) return -1;
        if (start >= 0 && start + expected.Length <= text.Length && string.CompareOrdinal(text, start, expected, 0, expected.Length) == 0) return start;
        int best = -1;
        for (int at = text.IndexOf(expected, StringComparison.Ordinal); at >= 0; at = text.IndexOf(expected, at + 1, StringComparison.Ordinal))
        {
            if (best < 0 || Math.Abs(at - start) < Math.Abs(best - start)) best = at;
        }
        return best >= 0 && Math.Abs(best - start) <= 2000 ? best : -1;
    }

    /// <summary>How many (non-overlapping) times <paramref name="needle"/> occurs before <paramref name="start"/>.</summary>
    public static int OccurrencesBefore(string text, string needle, int start)
    {
        int n = 0;
        for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0 && at < start; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>Number of grapheme clusters in text[0..offset) — some apps count UI Automation characters this way.</summary>
    public static int GraphemeIndex(string text, int offset)
    {
        if (offset <= 0) return 0;
        int count = 0;
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext() && e.ElementIndex < offset) count++;
        return count;
    }

    /// <summary>True when every char before <paramref name="end"/> is its own grapheme (so both offset modes agree).</summary>
    public static bool IsSimple(string text, int end)
    {
        int n = Math.Min(end, text.Length);
        for (int i = 0; i < n; i++)
        {
            char c = text[i];
            if (char.IsSurrogate(c) || c == '\r') return false;
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return false;
        }
        return true;
    }

    /// <summary>Keep the leading/trailing whitespace of the text being replaced around an AI rewrite.</summary>
    public static string KeepEdges(string original, string rewrite)
    {
        string core = rewrite.Trim();
        if (core.Length >= 2 && core[0] == '"' && core[^1] == '"' && !original.TrimStart().StartsWith('"')) core = core[1..^1].Trim();
        int lead = original.Length - original.TrimStart().Length;
        int trail = original.Length - original.TrimEnd().Length;
        if (lead + trail >= original.Length) return core;
        return original[..lead] + core + original[^trail..];
    }

    /// <summary>
    /// After typing a replacement at <paramref name="at"/> (over <paramref name="removed"/> characters), the text that
    /// actually landed there — but only if everything before and after the edit is untouched. Null otherwise, so a
    /// repair can never spill outside the edit.
    /// </summary>
    public static string? Landed(string before, string now, int at, int removed, int maxLength)
    {
        string b = before.Replace('\u00A0', ' '), n = now.Replace('\u00A0', ' ');
        int suffix = b.Length - at - removed;
        int length = n.Length - at - suffix;
        if (at < 0 || suffix < 0 || length <= 0 || length > maxLength) return null;
        if (string.CompareOrdinal(b, 0, n, 0, at) != 0) return null;
        if (string.CompareOrdinal(b, at + removed, n, at + length, suffix) != 0) return null;
        return now.Substring(at, length);
    }

    static readonly Regex SecretRe = new(
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----" +
        @"|\b(?:nvapi|sk|rk)-(?=[A-Za-z0-9_\-]*\d)[A-Za-z0-9_\-]{20,}|\b(?:sk|pk|rk)_(?:live_|test_)?[A-Za-z0-9]{24,}" +
        @"|\bgh[pousr]_[A-Za-z0-9]{30,}|\bgithub_pat_[A-Za-z0-9_]{40,}" +
        @"|\bxox[abprs]-[A-Za-z0-9-]{10,}" +
        @"|\bAKIA[0-9A-Z]{16}\b" +
        @"|\bAIza[0-9A-Za-z_\-]{35}" +
        @"|\bhf_[A-Za-z0-9]{30,}|\br8_[A-Za-z0-9]{30,}" +
        @"|\b\d{8,10}:[A-Za-z0-9_\-]{35}\b" +
        @"|\beyJ[A-Za-z0-9_\-]{10,}\.eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}" +
        // Any long random-looking token (upper, lower and digits, 32+ characters) that isn't part of a link.
        @"|(?<![\w/=?&#.:@%+\-])(?=[A-Za-z0-9_\-]*[A-Z])(?=[A-Za-z0-9_\-]*[a-z])(?=[A-Za-z0-9_\-]*\d)[A-Za-z0-9_\-]{32,}(?![\w/?=&#\-])" +
        @"|^[ \t]*(?:export[ \t]+|set[ \t]+|\$env:)?(?:[A-Za-z_][A-Za-z0-9_]*)?(?i:key|token|secret|password|passwd|pwd)[A-Za-z0-9_]*[ \t]*=[ \t]*[""']?[^\s""']{8,}" +
        @"|^[ \t]*(?i:password|passcode|pin)[ \t]*:[ \t]*\S{4,}",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Text that looks like it holds credentials: API keys, private keys, <c>NAME_KEY=value</c> lines, "Password: …".
    /// The desktop companion sees every text box on the PC, so text like this is never sent to the checker or the AI.
    /// </summary>
    public static bool LooksSecret(string text) => text.Length > 0 && SecretRe.IsMatch(text);

    static readonly Regex UrlRe = new(@"\b(?:https?://|www\.)\S+|\S+@\S+\.\w+", RegexOptions.CultureInvariant);

    static readonly Regex CodeLineRe = new(
        @"^(?:#!|//|/\*|<\?|<[A-Za-z][\w:-]*(?:\s[^>]*)?>\s*$|""[^""\n]*""\s*:|\$[A-Za-z_][\w:]*\s*[=+\-.\[]" +
        @"|[A-Za-z_][\w.\-]*\s*(?:=|=>|:=|\+=|-=)\s*\S" +
        @"|(?:function|def|class|import|using|var|let|const|public|private|static|param|foreach|elif|return)\b[^.!?]*(?:\w\(|[{=])" +
        @"|(?:if|for|while|switch|catch)\s*\(.*[)};{]\s*$|[{}\[\]()]+[;,]?\s*$)",
        RegexOptions.CultureInvariant);

    // Characters that are rare in prose: braces, angle brackets, =, |, backslash, backtick, $variables, and semicolons
    // that end a line or touch the next character ("a;b"). A semicolon followed by a space is ordinary punctuation.
    static readonly Regex CodeTokenRe = new(@"[{}<>=|\\`]|\$[A-Za-z_{(]|;(?=\s*$|\S)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Code, config and command lines aren't prose: the desktop companion doesn't send them for checking. Prices,
    /// semicolons, citations like [1], emoticons like &lt;3, Markdown, hashtags, URLs and email addresses don't count
    /// as code, so notes and messages are still checked.
    /// </summary>
    public static bool LooksLikeCode(string paragraph)
    {
        string t = paragraph.Trim();
        if (t.Length == 0) return false;
        if (CodeLineRe.IsMatch(t)) return true;
        string bare = UrlRe.Replace(t, " ");
        int letters = 0;
        foreach (char c in bare)
        {
            if (char.IsLetter(c)) letters++;
        }
        if (letters == 0) return true;
        int symbols = CodeTokenRe.Matches(bare).Count;
        return symbols >= 3 && symbols * 100 >= (symbols + letters) * 6;
    }
}
