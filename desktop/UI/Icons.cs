using System.Windows.Media;

namespace NimbleWrite.Desktop.UI;

/// <summary>
/// Icon glyphs. Segoe Fluent Icons ships with Windows 11; Segoe MDL2 Assets (Windows 10) has the same code points for
/// everything used here, so the rail looks the same on both.
/// </summary>
internal static class Icons
{
    public static readonly FontFamily Glyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public const string Review = "\uE73E"; // CheckMark
    public const string Assistant = "\uE945"; // LightningBolt
    public const string Paraphrase = "\uE8AC"; // Rename
    public const string Humanize = "\uE77B"; // Contact
    public const string Translate = "\uE8C1"; // Characters
    public const string Reader = "\uE890"; // View
    public const string Grader = "\uE7BE"; // Education
    public const string Detector = "\uE721"; // Search
    public const string Synonyms = "\uE82D"; // Dictionary
    public const string Citations = "\uE8F1"; // Library
    public const string Plagiarism = "\uEA18"; // Shield
    public const string ImagePrompt = "\uE91B"; // Photo
    public const string Documents = "\uE8A5"; // Document
    public const string Performance = "\uE9D9"; // Diagnostic
    public const string Settings = "\uE713"; // Setting
    public const string Close = "\uE711"; // Cancel
    public const string Paste = "\uE77F"; // Paste
    public const string Send = "\uE724"; // Send
}
