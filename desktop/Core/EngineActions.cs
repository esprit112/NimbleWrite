using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Interop.UIAutomationClient;

namespace NimbleWrite.Desktop.Core;

/// <summary>Things the user asks for: apply fixes, dismiss, add words, AI rewrites, tone, open in the editor.</summary>
public sealed partial class Engine
{
    CancellationTokenSource? aiCts;
    int aiSeq;
    string? lastAppliedId;
    DateTime lastAppliedAt;

    public void Apply(string issueId, int index) => Post(() => DoApply(issueId, index));

    public void ApplyAll(IReadOnlyList<string> ids) => Post(() => DoApplyAll(ids));

    public void Dismiss(string issueId) => Post(() => DoDismiss(issueId));

    public void AddToDictionary(string issueId) => Post(() => DoAddWord(issueId));

    public void Rewrite(string task, Action<AiUpdate> onUpdate) => Post(() => DoRewrite(task, onUpdate));

    public void CancelRewrite() => Post(() => aiCts?.Cancel());

    public void ApplyRewrite(TextScope scope, string replacement) => Post(() => DoApplyRewrite(scope, replacement));

    public void DetectTone(Action<ToneResult?, string?> done) => Post(() => DoTone(done));

    public void OpenInEditor() => Post(DoOpenInEditor);

    /// <summary>Forget cached results for the current text and check it again.</summary>
    public void Recheck() => Post(() =>
    {
        foreach (var p in TextModel.SplitParagraphs(text))
        {
            final.Remove(p.Text);
            provisional.Remove(p.Text);
        }
        changedAt = DateTime.MinValue;
        retryAt = DateTime.MinValue;
        issues = Compose(Array.Empty<PlacedIssue>());
        issuesVersion++;
        PublishState();
    });

    /// <summary>Forget every cached result, e.g. after the NVIDIA key changed: everything gets checked again.</summary>
    public void ResetChecks() => Post(() =>
    {
        final.Clear();
        provisional.Clear();
        aiOff = false;
        changedAt = DateTime.MinValue;
        retryAt = DateTime.MinValue;
        if (state is CheckState.Error or CheckState.Offline) SetState(CheckState.Idle, null);
        issues = Compose(Array.Empty<PlacedIssue>());
        issuesVersion++;
        PublishState();
    });

    void DoApply(string id, int index)
    {
        var f = field;
        var issue = issues.FirstOrDefault(i => i.Id == id);
        if (f == null || issue == null)
        {
            // A second click on a fix that has just gone in (double-click, or the card's last frame) is not news.
            if (id == lastAppliedId && DateTime.UtcNow - lastAppliedAt < TimeSpan.FromSeconds(5)) return;
            onNotice("That suggestion has already gone.");
            return;
        }
        if (issue.Issue.Replacements.Count == 0)
        {
            onNotice("This one needs a human touch: there's no automatic fix.");
            return;
        }
        string replacement = issue.Issue.Replacements[Math.Clamp(index, 0, issue.Issue.Replacements.Count - 1)];
        bool wasFinal = ParagraphAt(issue.Start) is { } para && final.ContainsKey(para.Text);
        var (ok, error) = Replace(f, issue.Start, issue.Issue.Original, replacement);
        lastApply = ok ? "ok" : "failed";
        Log.Info($"apply {issue.Issue.Type} in {f.Info.ProcessName}: {(ok ? "ok" : "failed")}");
        if (!ok)
        {
            onNotice(error!);
            return;
        }
        Applied(id);
        RememberParagraphAt(issue.Start, wasFinal);
        PublishState();
    }

    /// <summary>
    /// A fix is in, so it must never be offered again. The text-change bookkeeping alone can't be trusted with that:
    /// a fix that only adds ("Also" -> "Also,") edits next to its own range, which an edit while typing also does.
    /// </summary>
    void Applied(string id)
    {
        lastAppliedId = id;
        lastAppliedAt = DateTime.UtcNow;
        if (issues.All(i => i.Id != id)) return;
        issues = issues.Where(i => i.Id != id).ToList();
        issuesVersion++;
    }

    void DoApplyAll(IReadOnlyList<string> ids)
    {
        var f = field;
        if (f == null) return;
        var want = ids.ToHashSet(StringComparer.Ordinal);
        // Last first, so earlier offsets stay valid as the text changes.
        var todo = issues.Where(i => want.Contains(i.Id) && i.Issue.Replacements.Count > 0).OrderByDescending(i => i.Start).ToList();
        int done = 0;
        foreach (var i in todo)
        {
            if (field != f) break;
            bool wasFinal = ParagraphAt(i.Start) is { } para && final.ContainsKey(para.Text);
            var (ok, error) = Replace(f, i.Start, i.Issue.Original, i.Replacement);
            if (!ok)
            {
                lastApply = $"all:{done}:failed";
                onNotice(done > 0 ? $"Fixed {done}, then stopped: {error}" : error!);
                PublishState();
                return;
            }
            done++;
            Applied(i.Id);
            RememberParagraphAt(i.Start, wasFinal);
        }
        lastApply = $"all:{done}";
        Log.Info($"apply all in {f.Info.ProcessName}: {done} fixed");
        if (done > 0) onNotice($"Fixed {done} {(done == 1 ? "issue" : "issues")}.");
        PublishState();
    }

    Paragraph? ParagraphAt(int pos) => TextModel.SplitParagraphs(text).FirstOrDefault(p => pos >= p.Start && pos <= p.End);

    /// <summary>
    /// After accepting a fix, the paragraph's remaining suggestions are still right, so cache them for its new text
    /// instead of paying for another AI check.
    /// </summary>
    void RememberParagraphAt(int pos, bool asFinal)
    {
        if (ParagraphAt(pos) is not { } p) return;
        var list = issues.Where(i => i.Start >= p.Start && i.End <= p.End).Select(i => i.Issue.At(i.Start - p.Start)).ToList();
        (asFinal ? final : provisional)[p.Text] = list;
    }

    void DoDismiss(string id)
    {
        var i = issues.FirstOrDefault(x => x.Id == id);
        if (i == null) return;
        dismissed.Add(i.Signature);
        issues = issues.Where(x => x.Signature != i.Signature).ToList();
        issuesVersion++;
        PublishState();
    }

    void DoAddWord(string id)
    {
        var i = issues.FirstOrDefault(x => x.Id == id);
        if (i == null) return;
        string word = i.Issue.Original.Trim();
        var ct = life.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await api.AddToDictionaryAsync(word, ct).ConfigureAwait(false);
                Post(() => ForgetWord(word));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                onNotice($"Couldn't add that word to your dictionary: {e.Message}");
            }
        }, ct);
    }

    void ForgetWord(string word)
    {
        bool Match(Issue x) => x.Type is "spelling" or "dialect" && string.Equals(x.Original.Trim(), word, StringComparison.OrdinalIgnoreCase);
        foreach (var key in final.Keys.ToList()) final[key] = final[key].Where(x => !Match(x)).ToList();
        foreach (var key in provisional.Keys.ToList()) provisional[key] = provisional[key].Where(x => !Match(x)).ToList();
        issues = issues.Where(x => !Match(x.Issue)).ToList();
        issuesVersion++;
        PublishState();
        onNotice($"Added “{word}” to your dictionary.");
    }

    /// <summary>
    /// Replace text in the focused field: select the exact range through UI Automation, check the selection, then type
    /// (or paste) the replacement. Refuses if the field lost focus, so keystrokes can never land in another window.
    /// </summary>
    (bool Ok, string? Error) Replace(Field f, int start, string expected, string replacement)
    {
        if (!StillFocused(f)) return (false, $"Click back into {f.Info.AppName} first, then try again.");
        string? cur = f.Read(TextModel.MaxChars + 1);
        if (cur == null) return (false, "Couldn't read the text.");
        if (!string.Equals(cur, text, StringComparison.Ordinal)) OnTextChanged(cur);
        int at = TextModel.Locate(cur, start, expected);
        if (at < 0) return (false, "The text has changed. Checking it again…");
        string expectedAfter = string.Concat(cur.AsSpan(0, at), replacement, cur.AsSpan(at + expected.Length));
        object? saved = null;
        bool pasted = false;
        try
        {
            if (f.Text != null)
            {
                string? problem = SelectExact(f, at, expected);
                if (problem != null) return (false, problem);
                if (replacement.Length == 0) InputSim.Press(InputSim.VK_BACK);
                else if (NeedsPaste(replacement))
                {
                    saved = clipboard.Put(replacement);
                    pasted = true;
                    InputSim.Chord(InputSim.VK_CONTROL, InputSim.VK_V);
                }
                else InputSim.TypeText(replacement);
            }
            else if (f.Value != null)
            {
                f.Value.SetValue(expectedAfter);
            }
            else
            {
                return (false, "This field can't be edited.");
            }

            if (AwaitText(f, expectedAfter, out string? latest)) return (true, null);
            if (latest == null) return (false, $"{f.Info.AppName} didn't accept the change.");
            // Some apps reshape what was typed (autocorrect, smart quotes); accept if the replacement landed in place.
            if (replacement.Length > 0 && latest.Length >= at + replacement.Length && SameText(latest.Substring(at, replacement.Length), replacement)) return (true, null);
            // Apps that fall behind on synthesised keys can garble typing (Windows 11 Notepad repeats the last letter).
            // If everything around the edit is intact, select exactly what landed and paste the intended text over it.
            if (f.Text != null && !pasted && replacement.Length > 0
                && TextModel.Landed(cur, latest, at, expected.Length, replacement.Length * 2 + 16) is { } landed
                && SelectExact(f, at, landed) == null)
            {
                saved = clipboard.Put(replacement);
                pasted = true;
                InputSim.Chord(InputSim.VK_CONTROL, InputSim.VK_V);
                bool repaired = AwaitText(f, expectedAfter, out _);
                Log.Info($"typing was garbled in {f.Info.ProcessName}; pasted instead: {(repaired ? "ok" : "failed")}");
                if (repaired) return (true, null);
            }
            return (false, $"{f.Info.AppName} didn't accept the change.");
        }
        finally
        {
            if (pasted)
            {
                Thread.Sleep(150);
                clipboard.Restore(saved);
            }
        }
    }

    /// <summary>Select text[at..at+expected.Length] in the field and confirm the selection, ready to type over.</summary>
    string? SelectExact(Field f, int at, string expected)
    {
        var doc = f.Text!.DocumentRange;
        var range = RangeFor(f, doc, new RangeCursor(doc), at, expected);
        if (range == null) return "Couldn't find that text in the field.";
        range.Select();
        if (!WaitSelected(f, expected)) return $"{f.Info.AppName} didn't let NimbleWrite select the text.";
        if (!InputSim.WaitForModifiersReleased()) return "Let go of Ctrl, Alt and Shift, then try again.";
        if (!StillFocused(f)) return $"Click back into {f.Info.AppName} first, then try again.";
        return null;
    }

    /// <summary>Wait for the field to show <paramref name="want"/>. <paramref name="latest"/> is the last text read (null if unreadable).</summary>
    bool AwaitText(Field f, string want, out string? latest)
    {
        latest = null;
        for (int k = 0; k < 30; k++)
        {
            Thread.Sleep(k < 5 ? 20 : 50);
            latest = f.Read(TextModel.MaxChars + 1);
            if (latest == null) return false;
            if (SameText(latest, want)) break;
        }
        if (latest != null && !string.Equals(latest, text, StringComparison.Ordinal)) OnTextChanged(latest);
        return latest != null && SameText(latest, want);
    }

    static bool NeedsPaste(string s) => s.Length > 300 || s.IndexOfAny(new[] { '\r', '\n', '\v', '\u2028', '\u2029' }) >= 0;

    bool StillFocused(Field f)
    {
        if (Native.GetForegroundWindow() != f.Info.Hwnd) return false;
        try
        {
            var el = uia.GetFocusedElement();
            return el != null && Same(el, f.Element);
        }
        catch (COMException)
        {
            return false;
        }
    }

    static bool WaitSelected(Field f, string expected)
    {
        for (int k = 0; k < 12; k++)
        {
            try
            {
                var sel = f.Text!.GetSelection();
                if (sel != null && sel.Length > 0 && SameText(sel.GetElement(0).GetText(expected.Length + 8), expected)) return true;
            }
            catch (COMException)
            {
            }
            Thread.Sleep(30);
        }
        return false;
    }

    /// <summary>The selection if there is one, else the whole field.</summary>
    TextScope? ReadScope(Field f)
    {
        string? full = f.Read(TextModel.MaxChars + 1);
        if (full == null) return null;
        if (f.Text != null)
        {
            try
            {
                var sel = f.Text.GetSelection();
                if (sel != null && sel.Length > 0)
                {
                    var s = sel.GetElement(0);
                    string selected = s.GetText(TextModel.MaxChars) ?? "";
                    if (selected.Trim().Length > 0)
                    {
                        var pre = f.Text.DocumentRange.Clone();
                        pre.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, s, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
                        int start = (pre.GetText(TextModel.MaxChars + 1) ?? "").Length;
                        int at = TextModel.Locate(full, start, selected);
                        if (at >= 0) return new TextScope(at, selected.Length, selected, full, false);
                    }
                }
            }
            catch (COMException)
            {
            }
        }
        return new TextScope(0, full.Length, full, full, true);
    }

    void DoRewrite(string task, Action<AiUpdate> onUpdate)
    {
        int seq = ++aiSeq;
        var f = field;
        if (f == null)
        {
            onUpdate(new AiUpdate(seq, "", true, "Click into a text box first.", null));
            return;
        }
        var scope = ReadScope(f);
        if (scope == null || scope.Text.Trim().Length == 0)
        {
            onUpdate(new AiUpdate(seq, "", true, "There's no text to rewrite yet.", null));
            return;
        }
        if (scope.Text.Length > 12_000)
        {
            onUpdate(new AiUpdate(seq, "", true, "That's a lot of text to rewrite at once. Select a part of it first.", null));
            return;
        }
        if (TextModel.LooksSecret(scope.FullText))
        {
            onUpdate(new AiUpdate(seq, "", true, "This looks like it contains passwords or keys, so NimbleWrite won't send it to the AI.", null));
            return;
        }
        aiCts?.Cancel();
        var cts = aiCts = CancellationTokenSource.CreateLinkedTokenSource(life.Token);
        onUpdate(new AiUpdate(seq, "", false, null, scope));
        string? context = scope.Whole ? null : scope.FullText.Length > 6000 ? null : scope.FullText;
        _ = Task.Run(async () =>
        {
            try
            {
                string result = await api.WriteAsync(task, scope.Text, context, t => onUpdate(new AiUpdate(seq, t, false, null, scope)), cts.Token).ConfigureAwait(false);
                onUpdate(new AiUpdate(seq, TextModel.KeepEdges(scope.Text, result), true, null, scope));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                onUpdate(new AiUpdate(seq, "", true, e.Message, scope));
            }
        }, cts.Token);
    }

    void DoApplyRewrite(TextScope scope, string replacement)
    {
        var f = field;
        if (f == null)
        {
            onNotice("Click back into the text box first.");
            return;
        }
        var (ok, error) = Replace(f, scope.Start, scope.Text, replacement);
        lastApply = ok ? "rewrite:ok" : "rewrite:failed";
        Log.Info($"rewrite applied in {f.Info.ProcessName}: {(ok ? "ok" : "failed")}");
        onNotice(ok ? "Replaced with the rewrite." : error!);
        PublishState();
    }

    void DoTone(Action<ToneResult?, string?> done)
    {
        var f = field;
        if (f == null)
        {
            done(null, "Click into a text box first.");
            return;
        }
        string? full = f.Read(TextModel.MaxChars);
        if (full == null || TextModel.CountWords(full) < 5)
        {
            done(null, "Write a sentence or two first.");
            return;
        }
        if (TextModel.LooksSecret(full))
        {
            done(null, "This looks like it contains passwords or keys, so NimbleWrite won't send it to the AI.");
            return;
        }
        var ct = life.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                done(await api.ToneAsync(full, ct).ConfigureAwait(false), null);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                done(null, e.Message);
            }
        }, ct);
    }

    void DoOpenInEditor()
    {
        var f = field;
        if (f == null)
        {
            onNotice("Click into a text box first.");
            return;
        }
        string? full = f.Read(TextModel.MaxChars);
        if (string.IsNullOrWhiteSpace(full))
        {
            onNotice("There's no text to open yet.");
            return;
        }
        string title = $"From {f.Info.AppName}, {DateTime.Now:d MMM HH:mm}";
        var ct = life.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                string id = await api.CreateDocAsync(title, full, ct).ConfigureAwait(false);
                Process.Start(new ProcessStartInfo(api.DocUrl(id)) { UseShellExecute = true });
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                onNotice($"Couldn't open the editor: {e.Message}");
            }
        }, ct);
    }
}
