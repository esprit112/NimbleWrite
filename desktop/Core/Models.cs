using System;
using System.Collections.Generic;
using System.Windows;

namespace NimbleWrite.Desktop.Core;

/// <summary>A suggestion relative to one paragraph's text (same shape as the server's Issue).</summary>
public sealed class Issue
{
    public int Offset { get; set; }
    public int Length { get; set; }
    public string Original { get; set; } = "";
    public List<string> Replacements { get; set; } = new();
    public string Category { get; set; } = "correctness";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string Source { get; set; } = "ai";

    public Issue At(int offset) => new()
    {
        Offset = offset, Length = Length, Original = Original, Replacements = Replacements, Category = Category,
        Type = Type, Title = Title, Explanation = Explanation, Source = Source,
    };
}

public sealed class AssistResult
{
    public string Id { get; set; } = "";
    public List<Issue> Issues { get; set; } = new();
}

public sealed class AssistResponse
{
    public List<AssistResult> Results { get; set; } = new();
    public string? Model { get; set; }
    public int Ms { get; set; }
    public bool Ai { get; set; }
    public string? AiError { get; set; }
    /// <summary>False when the server has no NVIDIA key: local rules only, which isn't an error. Null from older servers.</summary>
    public bool? AiConfigured { get; set; }
    public string Fingerprint { get; set; } = "";
}

/// <summary>GET /api/health.</summary>
public sealed class HealthInfo
{
    public bool Ok { get; set; }
    public bool NimConfigured { get; set; }
    public bool Packaged { get; set; }
    public string? Version { get; set; }
}

/// <summary>POST /api/setup/test-key: whether NVIDIA accepts a key. The key is never sent back.</summary>
public sealed class KeyTestResult
{
    public bool Ok { get; set; }
    /// <summary>invalid, rejected, model, rate, network or error.</summary>
    public string? Reason { get; set; }
    public string? Message { get; set; }
}

public sealed class ToneScore
{
    public string Name { get; set; } = "";
    public string Emoji { get; set; } = "";
    public int Score { get; set; }
}

public sealed class ToneResult
{
    public List<ToneScore> Tones { get; set; } = new();
    public string Summary { get; set; } = "";
    public int Formality { get; set; }
    public string Sentiment { get; set; } = "neutral";
}

/// <summary>A suggestion placed in the focused field's text (absolute offsets).</summary>
public sealed record PlacedIssue(string Id, int Start, Issue Issue, string Signature)
{
    public int End => Start + Issue.Length;
    public string Replacement => Issue.Replacements.Count > 0 ? Issue.Replacements[0] : "";
}

/// <summary>Screen rectangles (physical pixels) of one suggestion's text.</summary>
public sealed record Underline(string IssueId, string Category, Rect[] Rects);

public enum CheckState { Idle, Checking, Error, Offline, TooLong, Private }

public sealed record TargetInfo(int Pid, string ProcessName, string AppName, IntPtr Hwnd, bool CanUnderline);

public sealed record EngineSnapshot(TargetInfo? Target, IReadOnlyList<PlacedIssue> Issues, CheckState State, string? Message, string? Model, int Ms, int WordCount, bool AiOff = false);

/// <summary>Where the field is on screen and where each suggestion's underline goes (physical pixels).</summary>
public sealed record GeometrySnapshot(Rect Field, IReadOnlyList<Underline> Underlines);

/// <summary>The text an AI action works on: the selection, or the whole field.</summary>
public sealed record TextScope(int Start, int Length, string Text, string FullText, bool Whole);

/// <summary>Progress of an AI rewrite. <see cref="Seq"/> lets the panel ignore updates from a superseded request.</summary>
public sealed record AiUpdate(int Seq, string Text, bool Done, string? Error, TextScope? Scope);
