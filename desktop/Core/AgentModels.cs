using System.Collections.Generic;

namespace NimbleWrite.Desktop.Core;

// Shapes returned by the server's tool endpoints, mirroring shared/types.ts (JSON is camelCase; System.Text.Json's
// web defaults map it onto these PascalCase properties). Only what the companion shows or sends back is included.

/// <summary>What a write task asks for: POST /api/ai/write. Null fields aren't sent.</summary>
public sealed record WriteRequest(string Task, string Text, string? Context = null, string? Instruction = null, string? Mode = null, string? TargetLanguage = null);

public sealed class ModelSettings
{
    public string Check { get; set; } = "";
    public string Write { get; set; } = "";
    public string Translate { get; set; } = "";
}

public sealed class Goals
{
    public string Audience { get; set; } = "knowledgeable";
    public string Formality { get; set; } = "neutral";
    public string Domain { get; set; } = "general";
    public List<string> Intent { get; set; } = new();
}

/// <summary>GET /api/settings, reduced to what the desktop Settings window shows. Never PUT back (that replaces everything).</summary>
public sealed class WritingSettings
{
    public string Dialect { get; set; } = "en-GB";
    public Goals DefaultGoals { get; set; } = new();
    public bool ShowGoalsOnNewDoc { get; set; }
    public Dictionary<string, bool> Categories { get; set; } = new();
    public bool LocalRules { get; set; } = true;
    public string Theme { get; set; } = "system";
    public ModelSettings Models { get; set; } = new();
    public List<string> MutedTypes { get; set; } = new();
}

/// <summary>PATCH /api/settings: only the non-null fields are sent, and the server merges them.</summary>
public sealed class SettingsPatch
{
    public string? Dialect { get; set; }
    /// <summary>Only the categories being changed, e.g. { "engagement": false }.</summary>
    public Dictionary<string, bool>? Categories { get; set; }
    public bool? LocalRules { get; set; }
    public Goals? DefaultGoals { get; set; }
    public bool? ShowGoalsOnNewDoc { get; set; }
    public string? Theme { get; set; }
}

public sealed class SynonymSense
{
    public string Meaning { get; set; } = "";
    public List<string> Synonyms { get; set; } = new();
}

public sealed class SynonymsResponse
{
    public List<SynonymSense> Senses { get; set; } = new();
}

public sealed class ReaderConfusion
{
    public string Quote { get; set; } = "";
    public string Issue { get; set; } = "";
    public string Suggestion { get; set; } = "";
}

public sealed class ReaderReactions
{
    public string Overall { get; set; } = "";
    public List<string> Takeaways { get; set; } = new();
    public List<string> Questions { get; set; } = new();
    public List<ReaderConfusion> Confusions { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
}

public sealed class GraderCriterion
{
    public string Name { get; set; } = "";
    public double Score { get; set; }
    public string Feedback { get; set; } = "";
}

public sealed class GraderResult
{
    public string Grade { get; set; } = "";
    public double Score { get; set; }
    public string Summary { get; set; } = "";
    public List<GraderCriterion> Criteria { get; set; } = new();
    public List<string> Strengths { get; set; } = new();
    public List<string> Improvements { get; set; } = new();
}

public sealed class ClaimToCite
{
    public string Claim { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Query { get; set; } = "";
    /// <summary>needs_citation or recommended.</summary>
    public string Strength { get; set; } = "recommended";
}

public sealed class ClaimsResponse
{
    public List<ClaimToCite> Claims { get; set; } = new();
}

public sealed class AiDetectPassage
{
    public string Text { get; set; } = "";
    public double Score { get; set; }
    public List<string> Reasons { get; set; } = new();
}

public sealed class DetectSignal
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class AiDetectResult
{
    public double Score { get; set; }
    public string Verdict { get; set; } = "";
    public List<AiDetectPassage> Passages { get; set; } = new();
    public List<DetectSignal> Signals { get; set; } = new();
}

public sealed class PlagiarismSourceInput
{
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? Url { get; set; }
}

public sealed class PlagiarismRequest
{
    public string Text { get; set; } = "";
    public string? DocId { get; set; }
    public bool IncludeDocs { get; set; } = true;
    public bool Web { get; set; }
    public List<PlagiarismSourceInput> Sources { get; set; } = new();
}

public sealed class PlagiarismPassage
{
    public int Offset { get; set; }
    public int Length { get; set; }
    public string Text { get; set; } = "";
}

public sealed class PlagiarismSource
{
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    /// <summary>doc, pasted or web.</summary>
    public string Kind { get; set; } = "";
    public string? DocId { get; set; }
    public double Similarity { get; set; }
    public List<PlagiarismPassage> Passages { get; set; } = new();
}

public sealed class PlagiarismResult
{
    public double Overall { get; set; }
    public List<PlagiarismSource> Sources { get; set; } = new();
    /// <summary>used, not_configured, failed or skipped.</summary>
    public string WebSearch { get; set; } = "skipped";
    public int CheckedSources { get; set; }
    public string? Note { get; set; }
}

public sealed class CitationAuthor
{
    public string? Given { get; set; }
    public string? Family { get; set; }
    public string? Literal { get; set; }
}

public sealed class CitationMeta
{
    public string Type { get; set; } = "webpage";
    public string Title { get; set; } = "";
    public List<CitationAuthor> Authors { get; set; } = new();
    public string? SiteName { get; set; }
    public string? ContainerTitle { get; set; }
    public string? Publisher { get; set; }
    public string? Published { get; set; }
    public string? Accessed { get; set; }
    public string? Volume { get; set; }
    public string? Issue { get; set; }
    public string? Pages { get; set; }
    public string? Doi { get; set; }
    public string? Url { get; set; }
}

public sealed class FormattedCitation
{
    /// <summary>apa, mla, chicago or harvard.</summary>
    public string Style { get; set; } = "";
    public string Text { get; set; } = "";
    public string Html { get; set; } = "";
}

public sealed class CitationLookupResult
{
    public CitationMeta Meta { get; set; } = new();
    public List<FormattedCitation> Formatted { get; set; } = new();
}

public sealed class ImagePromptResult
{
    public string Prompt { get; set; } = "";
    public string Model { get; set; } = "";
    public int Ms { get; set; }
}

/// <summary>
/// One settings change proposed by the Tune chat (shared/types.ts TuneChange, a union on <see cref="Kind"/>). Fields
/// that don't apply to a kind stay null and aren't sent, so a change goes back to the server exactly as it came.
/// </summary>
public sealed class TuneChange
{
    /// <summary>instruction, mute, unmute, dictionary or style_rule.</summary>
    public string Kind { get; set; } = "";
    public string? Text { get; set; }
    public string? Type { get; set; }
    public string? Title { get; set; }
    public string? Word { get; set; }
    public string? Find { get; set; }
    public string? Replace { get; set; }
    public string? Note { get; set; }
}

public sealed class TuneResult
{
    public string Reply { get; set; } = "";
    public List<TuneChange> Changes { get; set; } = new();
}

public sealed class TuneApplyResult
{
    public WritingSettings Settings { get; set; } = new();
    public int Applied { get; set; }
    public int Skipped { get; set; }
}

public sealed class TextStats
{
    public int Characters { get; set; }
    public int CharactersNoSpaces { get; set; }
    public int Words { get; set; }
    public int Sentences { get; set; }
    public int Paragraphs { get; set; }
    public int ReadingTimeSec { get; set; }
    public int SpeakingTimeSec { get; set; }
    public double AvgWordLength { get; set; }
    public double AvgSentenceLength { get; set; }
    public int Readability { get; set; }
    public int GradeLevel { get; set; }
    public int UniqueWordsPct { get; set; }
    public int RareWordsPct { get; set; }
    public int LongSentences { get; set; }
}

public sealed class ReadabilityLabel
{
    public string Label { get; set; } = "";
    public string Audience { get; set; } = "";
}

public sealed class TextStatsResponse
{
    public TextStats Stats { get; set; } = new();
    public ReadabilityLabel Readability { get; set; } = new();
    /// <summary>0–100, or null when there's too little text or no suggestion counts were sent.</summary>
    public int? Score { get; set; }
}

public sealed class DayStats
{
    public Dictionary<string, int> Accepted { get; set; } = new();
    public int Checks { get; set; }
    public int WordsChecked { get; set; }
}

public sealed class InsightsDay
{
    public string Date { get; set; } = "";
    public DayStats Stats { get; set; } = new();
}

public sealed class InsightsTotals
{
    public int Docs { get; set; }
    public int Words { get; set; }
    public int Accepted { get; set; }
}

public sealed class InsightsResponse
{
    public List<InsightsDay> Days { get; set; } = new();
    public InsightsTotals Totals { get; set; } = new();
}

public sealed class DocSummary
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Snippet { get; set; } = "";
    public int WordCount { get; set; }
    public double? Score { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
}

public sealed class DocsResponse
{
    public List<DocSummary> Docs { get; set; } = new();
}

/// <summary>GET /api/docs/:id, reduced to what the companion needs (the plain text, not the HTML).</summary>
public sealed class DocDetail
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public int WordCount { get; set; }
    public long UpdatedAt { get; set; }
}
