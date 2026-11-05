using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>
/// What the assistant panel and its tabs can ask the app to do. <see cref="UiController"/> implements it; tests use a
/// fake, so every tab can be built and rendered without an engine. Callbacks always arrive on the UI thread.
/// </summary>
internal interface IPanelActions
{
    string Hotkey { get; }

    void Accept(string id, int index);
    void Dismiss(string id);
    void AddWord(string id);
    void AcceptAll(IReadOnlyList<string> ids);
    void SetHighlight(string? id);

    /// <summary>Stream a write task on the selection (or all the text, or <paramref name="on"/>) into <paramref name="sink"/>. Starting one stops any other.</summary>
    void Rewrite(WriteRequest request, RewriteResult sink, TextScope? on = null);
    void CancelRewrite(RewriteResult sink);

    /// <summary>Put <paramref name="text"/> in place of <paramref name="scope"/>, if the field still holds what it did.</summary>
    void ApplyRewrite(TextScope scope, string text, string? doneNotice = null);

    /// <summary>The server, for tools that call it directly. Calls return on the UI thread when awaited from it.</summary>
    ApiClient Api { get; }

    /// <summary>The web editor's address, for links into it.</summary>
    string EditorUrl { get; }

    /// <summary>The selection (or all the text) after the shared checks, or why it can't be used.</summary>
    Task<(TextScope? Scope, string? Problem)> CaptureAsync(int maxChars, string verb);

    /// <summary>Humanize just this passage (from the AI Detector): opens the Humanizer on it.</summary>
    void HumanizePassage(TextScope scope);

    /// <summary>Writing settings changed (Tune): check the text again with them.</summary>
    void Recheck();

    void OpenUrl(string url);

    void DetectTone(Action<ToneResult?, string?> done);

    /// <summary>Languages last translated into, most recent first.</summary>
    IReadOnlyList<string> RecentLanguages { get; }

    /// <summary>The user translated into <paramref name="language"/>; offer it first next time.</summary>
    void UsedLanguage(string language);

    /// <summary>A short message in the panel's notice line.</summary>
    void Notice(string text);

    void OpenEditor();
    void OpenWritingSettings();
    void PauseHour();
    void TurnOffInCurrentApp();
    void ClosePanel();

    /// <summary>The user switched tabs; remember it for next time.</summary>
    void TabChanged(string id);
}
