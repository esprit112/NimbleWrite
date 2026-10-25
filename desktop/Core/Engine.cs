using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Interop.UIAutomationClient;

namespace NimbleWrite.Desktop.Core;

/// <summary>Clipboard access lives on the UI (STA) thread; the engine reaches it through this bridge.</summary>
public interface IClipboardBridge
{
    object? Put(string text);

    void Restore(object? saved);
}

/// <summary>
/// Watches the focused text field in whichever app is in front, checks its text with the NimbleWrite server and works
/// out where each suggestion sits on screen. Every UI Automation call happens on this one MTA background thread.
/// </summary>
public sealed partial class Engine : IDisposable
{
    static readonly TimeSpan TickEvery = TimeSpan.FromMilliseconds(120);
    static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(650);

    /// <summary>Fields we never check: browser address bars.</summary>
    static readonly HashSet<string> DeniedFields = new(StringComparer.OrdinalIgnoreCase) { "OmniboxViewViews", "urlbar-input" };

    readonly DesktopConfig config;
    readonly ApiClient api;
    readonly Action<EngineSnapshot> onState;
    readonly Action<GeometrySnapshot?> onGeometry;
    readonly Action<string> onNotice;
    readonly IClipboardBridge clipboard;
    readonly bool diag;
    readonly BlockingCollection<Action> queue = new();
    readonly CancellationTokenSource life = new();
    readonly Thread thread;
    readonly int ownPid = Environment.ProcessId;

    IUIAutomation uia = null!;
    Field? field;
    IUIAutomationElement? rejected;
    DateTime rejectedUntil;
    string lastReject = "";
    string text = "";
    long textVersion;
    DateTime changedAt = DateTime.MinValue;
    bool freshAttach;
    List<PlacedIssue> issues = new();
    long issuesVersion;
    readonly Dictionary<string, List<Issue>> final = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<Issue>> provisional = new(StringComparer.Ordinal);
    readonly HashSet<string> dismissed = new(StringComparer.Ordinal);
    readonly Dictionary<string, IUIAutomationTextRange?> ranges = new(StringComparer.Ordinal);
    readonly Dictionary<int, (string Process, string App)> names = new();
    DateTime namesAt = DateTime.UtcNow;
    string fingerprint = "";
    bool inFlight;
    /// <summary>The server has no NVIDIA key, so only the local rules run (installed app before setup).</summary>
    bool aiOff;
    DateTime retryAt = DateTime.MinValue;
    DateTime launchTriedAt = DateTime.MinValue;
    CheckState state = CheckState.Idle;
    string? message;
    string? model;
    int ms;
    Rect lastField = Rect.Empty;
    long geometryKey = -1;
    DateTime geometryAt = DateTime.MinValue;
    string geometrySig = "\0";
    GeometrySnapshot? lastGeometry;
    DateTime diagAt = DateTime.MinValue;
    string lastApply = "";
    readonly ShortcutFocusGuard shortcut = new();

    /// <summary>Process name and display name of the app that last had keyboard focus (for "Turn off in …").</summary>
    public volatile string? LastProcess;
    public volatile string? LastAppName;

    public Engine(DesktopConfig config, ApiClient api, Action<EngineSnapshot> onState, Action<GeometrySnapshot?> onGeometry,
        Action<string> onNotice, IClipboardBridge clipboard, bool diag)
    {
        this.config = config;
        this.api = api;
        this.onState = onState;
        this.onGeometry = onGeometry;
        this.onNotice = onNotice;
        this.clipboard = clipboard;
        this.diag = diag;
        thread = new Thread(Run) { IsBackground = true, Name = "NimbleWrite engine" };
        thread.SetApartmentState(ApartmentState.MTA);
    }

    public void Start() => thread.Start();

    /// <summary>Our shortcut was just pressed: undo the focus jump its Alt can cause (see <see cref="ShortcutFocusGuard"/>).</summary>
    public void ShortcutPressed()
    {
        var at = DateTime.UtcNow;
        Post(() => shortcut.Pressed(at));
    }

    void Post(Action a)
    {
        try
        {
            if (!queue.IsAddingCompleted) queue.Add(a);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        life.Cancel();
        queue.CompleteAdding();
        thread.Join(1500);
    }

    void Run()
    {
        try
        {
            uia = (IUIAutomation)new CUIAutomation8();
            var u2 = (IUIAutomation2)uia;
            u2.ConnectionTimeout = 2000;
            u2.TransactionTimeout = 4000;
        }
        catch (Exception e)
        {
            Log.Error("UI Automation init", e);
            SetState(CheckState.Error, "Windows UI Automation isn't available.");
            return;
        }
        Log.Info("engine started");
        while (!life.IsCancellationRequested)
        {
            try
            {
                TickOnce();
            }
            catch (COMException e)
            {
                Log.Info($"UI Automation error 0x{e.HResult:x8}; detaching");
                Detach();
            }
            catch (Exception e)
            {
                Log.Error("engine tick", e);
                Detach();
            }
            var until = DateTime.UtcNow + TickEvery;
            while (!life.IsCancellationRequested)
            {
                var left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) break;
                Action? cmd;
                try
                {
                    if (!queue.TryTake(out cmd, left)) break;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                try
                {
                    cmd();
                }
                catch (COMException e)
                {
                    Log.Info($"UI Automation error 0x{e.HResult:x8} in a command");
                }
                catch (Exception e)
                {
                    Log.Error("engine command", e);
                }
            }
        }
    }

    void TickOnce()
    {
        if (!config.IsActive)
        {
            Detach();
            return;
        }
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return;
        if (Native.ProcessIdOf(fg) == ownPid) return; // our tray menu has the foreground: keep everything as it is
        if (Native.IsIconic(fg))
        {
            Detach();
            return;
        }
        var focused = uia.GetFocusedElement();
        if (focused == null)
        {
            Detach();
            return;
        }
        var now = DateTime.UtcNow;
        if (shortcut.Watching(now)) shortcut.Observe(now, InputSim.ModifiersDown());
        if (field != null && !Same(focused, field.Element))
        {
            if (HoldAfterShortcut(focused, fg, now)) return;
            Detach();
        }
        if (field == null) TryAttach(focused, fg);
        if (field != null) Poll();
        if (diag) WriteDiag();
    }

    /// <summary>
    /// Focus has left the field. If our shortcut's Alt sent it to the app's menu bar, keep the field and put focus back
    /// once the keys are up (see <see cref="ShortcutFocusGuard"/>). True while doing that, so nothing gets hidden.
    /// </summary>
    bool HoldAfterShortcut(IUIAutomationElement focused, IntPtr fg, DateTime now)
    {
        if (!shortcut.Watching(now)) return false;
        var f = field!;
        bool sameWindow;
        int type;
        try
        {
            sameWindow = fg == f.Info.Hwnd && focused.CurrentProcessId == f.Info.Pid;
            type = focused.CurrentControlType;
        }
        catch (COMException)
        {
            return false;
        }
        switch (shortcut.Decide(now, InputSim.ModifiersDown(), sameWindow, type))
        {
            case FocusJump.Wait:
                return true;
            case FocusJump.PutBack:
                Log.Info($"the shortcut's Alt moved focus to the app's {UiaIds.ControlName(type)}; putting it back in the text");
                try
                {
                    f.Element.SetFocus();
                }
                catch (COMException e)
                {
                    Log.Info($"UI Automation error 0x{e.HResult:x8} putting focus back");
                }
                return true;
            case FocusJump.Escape:
                if (Native.GetForegroundWindow() != f.Info.Hwnd) return false;
                Log.Info("focus stayed in the app's menu; pressing Escape to close it");
                InputSim.Press(InputSim.VK_ESCAPE);
                return true;
            default:
                return false;
        }
    }

    bool Same(IUIAutomationElement a, IUIAutomationElement b)
    {
        try
        {
            return uia.CompareElements(a, b) != 0;
        }
        catch (COMException)
        {
            return false;
        }
    }

    void TryAttach(IUIAutomationElement el, IntPtr fg)
    {
        if (rejected != null && DateTime.UtcNow < rejectedUntil && Same(el, rejected)) return;
        var f = Evaluate(el, fg, out string why);
        if (f == null)
        {
            rejected = el;
            rejectedUntil = DateTime.UtcNow.AddSeconds(2);
            lastReject = why;
            return;
        }
        rejected = null;
        lastReject = "";
        field = f;
        text = "";
        textVersion++;
        issues = new List<PlacedIssue>();
        issuesVersion++;
        ranges.Clear();
        lastField = Rect.Empty;
        geometryKey = -1;
        freshAttach = true;
        if (state is CheckState.TooLong or CheckState.Private) state = CheckState.Idle;
        Log.Info($"attached to {f.Info.ProcessName} ({f.Kind})");
        PublishState();
    }

    Field? Evaluate(IUIAutomationElement el, IntPtr fg, out string why)
    {
        int pid = el.CurrentProcessId;
        if (pid == ownPid)
        {
            why = "own window";
            return null;
        }
        var (process, app) = Names(pid);
        LastProcess = process;
        LastAppName = app;
        if (config.IsAppDisabled(process))
        {
            why = "turned off in this app";
            return null;
        }
        if (el.CurrentIsPassword != 0)
        {
            why = "password field";
            return null;
        }
        if (el.CurrentIsEnabled == 0)
        {
            why = "disabled field";
            return null;
        }
        string cls = el.CurrentClassName ?? "", aid = el.CurrentAutomationId ?? "";
        if (DeniedFields.Contains(cls) || DeniedFields.Contains(aid))
        {
            why = "address bar";
            return null;
        }
        var tp = el.GetCurrentPattern(UiaIds.TextPattern) as IUIAutomationTextPattern;
        var vp = el.GetCurrentPattern(UiaIds.ValuePattern) as IUIAutomationValuePattern;
        if (tp == null && vp == null)
        {
            why = "not a text field";
            return null;
        }
        bool editable = vp != null ? vp.CurrentIsReadOnly == 0 : tp!.DocumentRange.GetAttributeValue(UiaIds.IsReadOnlyAttribute) is bool ro && !ro;
        if (!editable)
        {
            why = "read-only";
            return null;
        }
        string kind = $"{UiaIds.ControlName(el.CurrentControlType)}/{el.CurrentFrameworkId}/{(tp != null ? "TextPattern" : "ValuePattern")}";
        bool unscaled = tp != null && string.Equals(el.CurrentFrameworkId, "WPF", StringComparison.Ordinal)
            && Native.GetAwarenessFromDpiAwarenessContext(Native.GetWindowDpiAwarenessContext(fg)) == Native.DPI_AWARENESS_SYSTEM_AWARE
            && Native.GetDpiForWindow(fg) is uint dpi && dpi != 0 && dpi != 96;
        if (unscaled) kind += "/scaled";
        why = "";
        return new Field(el, tp, vp, new TargetInfo(pid, process, app, fg, tp != null), kind) { UnscaledTextRects = unscaled };
    }

    (string Process, string App) Names(int pid)
    {
        if (DateTime.UtcNow - namesAt > TimeSpan.FromMinutes(5))
        {
            names.Clear(); // process ids get reused
            namesAt = DateTime.UtcNow;
        }
        if (names.TryGetValue(pid, out var n)) return n;
        string process = "app", app = "this app";
        try
        {
            using var p = Process.GetProcessById(pid);
            process = p.ProcessName;
            app = process;
            try
            {
                string? path = p.MainModule?.FileName;
                string? d = path == null ? null : FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                if (d != null && d.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) d = d[..^4];
                if (!string.IsNullOrEmpty(d) && d.Length <= 40 && !d.Equals("Electron", StringComparison.OrdinalIgnoreCase)) app = d;
            }
            catch (Exception)
            {
                // Elevated or protected processes don't share their module path.
            }
        }
        catch (Exception)
        {
        }
        names[pid] = (process, app);
        return (process, app);
    }

    void Poll()
    {
        var f = field!;
        string? now = f.Read(TextModel.MaxChars + 1);
        if (now == null)
        {
            Detach();
            return;
        }
        if (!string.Equals(now, text, StringComparison.Ordinal)) OnTextChanged(now);
        var r = f.Element.CurrentBoundingRectangle;
        var rect = new Rect(r.left, r.top, Math.Max(0, r.right - r.left), Math.Max(0, r.bottom - r.top));
        MaybeCheck();
        UpdateGeometry(rect);
    }

    void OnTextChanged(string now)
    {
        string before = text;
        text = now;
        textVersion++;
        ranges.Clear();
        changedAt = freshAttach ? DateTime.MinValue : DateTime.UtcNow;
        freshAttach = false;
        if (now.Length > TextModel.MaxChars)
        {
            issues = new List<PlacedIssue>();
            issuesVersion++;
            SetState(CheckState.TooLong, "This text is longer than 30,000 characters, so live checking is off here.");
            return;
        }
        if (TextModel.LooksSecret(now))
        {
            issues = new List<PlacedIssue>();
            issuesVersion++;
            SetState(CheckState.Private, "This looks like it contains passwords or keys, so NimbleWrite won't check it or send it anywhere.");
            return;
        }
        if (state is CheckState.TooLong or CheckState.Private) state = CheckState.Idle;
        IReadOnlyList<PlacedIssue> carried = before.Length == 0 ? Array.Empty<PlacedIssue>() : TextModel.CarryOver(before, now, issues);
        issues = Compose(carried);
        issuesVersion++;
        PublishState();
    }

    /// <summary>Suggestions for the current text: cached results per paragraph, else what survived the last edit.</summary>
    List<PlacedIssue> Compose(IReadOnlyList<PlacedIssue> carried)
    {
        var list = new List<PlacedIssue>();
        foreach (var p in TextModel.SplitParagraphs(text))
        {
            if (final.TryGetValue(p.Text, out var found) || provisional.TryGetValue(p.Text, out found))
            {
                foreach (var i in found)
                {
                    if (TextModel.Place(p, i) is { } placed) list.Add(placed);
                }
            }
            else
            {
                list.AddRange(carried.Where(c => c.Start >= p.Start && c.End <= p.End));
            }
        }
        return list.Where(i => !dismissed.Contains(i.Signature)).OrderBy(i => i.Start).ToList();
    }

    void MaybeCheck()
    {
        if (inFlight || state is CheckState.TooLong or CheckState.Private) return;
        var now = DateTime.UtcNow;
        if (now - changedAt < Debounce || now < retryAt) return;
        if (TextModel.CountWords(text) < TextModel.MinWords)
        {
            NothingToCheck();
            return;
        }
        var pending = TextModel.SplitParagraphs(text)
            .Select(p => p.Text)
            .Where(t => t.Length <= TextModel.MaxParagraphChars && !final.ContainsKey(t) && !TextModel.LooksLikeCode(t))
            .Distinct(StringComparer.Ordinal)
            .Take(24)
            .ToList();
        if (pending.Count == 0)
        {
            NothingToCheck();
            return;
        }
        var needLocal = pending.Where(t => !provisional.ContainsKey(t)).ToList();
        inFlight = true;
        SetState(CheckState.Checking, null);
        var ct = life.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                if (needLocal.Count > 0)
                {
                    // Instant local rules first, so underlines appear while the model thinks.
                    var quick = await api.AssistAsync(needLocal, ai: false, ct).ConfigureAwait(false);
                    Post(() => Store(needLocal, quick, toFinal: false));
                }
                var full = await api.AssistAsync(pending, ai: true, ct).ConfigureAwait(false);
                Post(() =>
                {
                    inFlight = false;
                    Store(pending, full, toFinal: full.Ai || full.AiConfigured == false);
                    AfterCheck(full);
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception e)
            {
                Post(() =>
                {
                    inFlight = false;
                    CheckFailed(e);
                });
            }
        }, ct);
    }

    void Store(List<string> texts, AssistResponse r, bool toFinal)
    {
        if (fingerprint.Length > 0 && r.Fingerprint != fingerprint)
        {
            // Settings changed in the web app (dialect, categories, model…): earlier results are stale.
            final.Clear();
            provisional.Clear();
        }
        fingerprint = r.Fingerprint;
        var target = toFinal ? final : provisional;
        foreach (var res in r.Results)
        {
            if (int.TryParse(res.Id, out int k) && k >= 0 && k < texts.Count) target[texts[k]] = res.Issues;
        }
        if (final.Count > 3000) final.Clear();
        if (provisional.Count > 3000) provisional.Clear();
        issues = Compose(issues);
        issuesVersion++;
        PublishState();
    }

    void AfterCheck(AssistResponse full)
    {
        model = full.Model ?? model;
        ms = full.Ms;
        // No NVIDIA key yet (installed app): the local rules are the whole check, and that isn't an error.
        aiOff = full.AiConfigured == false;
        if (!full.Ai && full.AiError != null && !aiOff)
        {
            Log.Info($"AI check failed: {Short(full.AiError)}");
            retryAt = DateTime.UtcNow.AddSeconds(30);
            SetState(CheckState.Error, "The AI check isn't available right now, so only basic checks are shown.");
        }
        else
        {
            SetState(CheckState.Idle, null);
        }
    }

    /// <summary>
    /// The field needs no checking, so an earlier failure no longer applies to it: a stale Error or Checking clears at
    /// once. "Can't reach the server" clears only when the server answers again, so the tray keeps telling the truth.
    /// </summary>
    void NothingToCheck()
    {
        if (state is CheckState.Checking or CheckState.Error)
        {
            SetState(CheckState.Idle, null);
            return;
        }
        if (state != CheckState.Offline) return;
        inFlight = true;
        var ct = life.Token;
        _ = Task.Run(async () =>
        {
            bool up = false;
            try
            {
                up = await api.HealthAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            Post(() =>
            {
                inFlight = false;
                if (state != CheckState.Offline) return;
                if (up) SetState(CheckState.Idle, null);
                else retryAt = DateTime.UtcNow.AddSeconds(5);
            });
        });
    }

    void CheckFailed(Exception e)
    {
        if (e is HttpRequestException or TaskCanceledException)
        {
            retryAt = DateTime.UtcNow.AddSeconds(5);
            if (DateTime.UtcNow - launchTriedAt > TimeSpan.FromMinutes(2))
            {
                launchTriedAt = DateTime.UtcNow;
                if (ServerLauncher.TryStart()) retryAt = DateTime.UtcNow.AddSeconds(4);
            }
            SetState(CheckState.Offline, "Can't reach the NimbleWrite server.");
        }
        else
        {
            Log.Info($"check failed: {e.GetType().Name}: {Short(e.Message)}");
            retryAt = DateTime.UtcNow.AddSeconds(15);
            SetState(CheckState.Error, e.Message);
        }
    }

    static string Short(string s) => s.Length > 200 ? s[..200] + "…" : s;

    void SetState(CheckState s, string? msg)
    {
        state = s;
        message = msg;
        PublishState();
    }

    void PublishState()
    {
        onState(new EngineSnapshot(field?.Info, issues.ToArray(), state, message, model, ms, field == null ? 0 : TextModel.CountWords(text), aiOff));
    }

    void UpdateGeometry(Rect fieldRect)
    {
        var f = field!;
        var w = Native.WindowRect(f.Info.Hwnd);
        var win = new Rect(w.Left, w.Top, Math.Max(0, w.Right - w.Left), Math.Max(0, w.Bottom - w.Top));
        var visible = Rect.Intersect(fieldRect, win);
        if (visible.IsEmpty || visible.Width < 8 || visible.Height < 8)
        {
            Publish(null);
            return;
        }
        if (visible != lastField)
        {
            bool first = lastField.IsEmpty;
            lastField = visible;
            if (!first)
            {
                // The field is moving or resizing: hide underlines until it settles, rather than drawing them in the wrong place.
                geometryKey = -1;
                Publish(new GeometrySnapshot(visible, Array.Empty<Underline>()));
                return;
            }
        }
        long key = HashCode.Combine(textVersion, issuesVersion, config.ShowUnderlines);
        if (key == geometryKey && DateTime.UtcNow - geometryAt < TimeSpan.FromMilliseconds(400)) return;
        geometryKey = key;
        geometryAt = DateTime.UtcNow;
        var lines = new List<Underline>();
        if (f.Text != null && config.ShowUnderlines && issues.Count > 0)
        {
            EnsureRanges(f);
            var clip = Rect.Inflate(visible, 2, 4);
            double scale = 1, ox = 0, oy = 0;
            if (f.UnscaledTextRects && Native.GetDpiForWindow(f.Info.Hwnd) is uint dpi && dpi != 0)
            {
                scale = dpi / 96.0;
                var origin = new Native.POINT();
                Native.ClientToScreen(f.Info.Hwnd, ref origin);
                ox = origin.X;
                oy = origin.Y;
            }
            foreach (var i in issues)
            {
                if (!ranges.TryGetValue(i.Id, out var range) || range == null) continue;
                double[]? arr;
                try
                {
                    arr = range.GetBoundingRectangles();
                }
                catch (COMException)
                {
                    continue;
                }
                if (arr == null) continue;
                var rects = new List<Rect>();
                for (int k = 0; k + 3 < arr.Length; k += 4)
                {
                    var rr = new Rect(arr[k], arr[k + 1], Math.Max(0, arr[k + 2]), Math.Max(0, arr[k + 3]));
                    if (scale != 1) rr = new Rect(ox + (rr.X - ox) * scale, oy + (rr.Y - oy) * scale, rr.Width * scale, rr.Height * scale);
                    rr.Intersect(clip);
                    if (!rr.IsEmpty && rr.Width >= 1 && rr.Height >= 4 && IsUncovered(rr, f.Info.Hwnd)) rects.Add(rr);
                }
                if (rects.Count > 0) lines.Add(new Underline(i.Id, i.Issue.Category, rects.ToArray()));
            }
        }
        Publish(new GeometrySnapshot(visible, lines));
    }

    /// <summary>
    /// Our overlay is topmost, so it would draw over anything covering the field (a dialog, another app). Only keep an
    /// underline if the window under its centre is the target app (or one of ours).
    /// </summary>
    bool IsUncovered(Rect r, IntPtr target)
    {
        var at = Native.WindowFromPoint(new Native.POINT { X = (int)(r.X + r.Width / 2), Y = (int)(r.Y + r.Height / 2) });
        if (at == IntPtr.Zero) return false;
        var root = Native.GetAncestor(at, Native.GA_ROOT);
        return root == target || Native.ProcessIdOf(root) == ownPid;
    }

    void Publish(GeometrySnapshot? g)
    {
        string sig = g == null ? "" : Signature(g);
        if (sig == geometrySig) return;
        geometrySig = sig;
        lastGeometry = g;
        onGeometry(g);
    }

    static string Signature(GeometrySnapshot g)
    {
        var sb = new StringBuilder();
        sb.Append((int)g.Field.X).Append(',').Append((int)g.Field.Y).Append(',').Append((int)g.Field.Width).Append(',').Append((int)g.Field.Height);
        foreach (var u in g.Underlines)
        {
            sb.Append('|').Append(u.IssueId);
            foreach (var r in u.Rects) sb.Append(';').Append((int)r.X).Append(',').Append((int)r.Y).Append(',').Append((int)r.Width).Append(',').Append((int)r.Height);
        }
        return sb.ToString();
    }

    void EnsureRanges(Field f)
    {
        var todo = issues.Where(i => !ranges.ContainsKey(i.Id)).OrderBy(i => i.Start).ToList();
        if (todo.Count == 0) return;
        var doc = f.Text!.DocumentRange;
        var cursor = new RangeCursor(doc);
        foreach (var i in todo) ranges[i.Id] = RangeFor(f, doc, cursor, i.Start, i.Issue.Original);
        if (ranges.Count > 400)
        {
            var keep = issues.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var k in ranges.Keys.Where(k => !keep.Contains(k)).ToList()) ranges.Remove(k);
        }
    }

    /// <summary>
    /// The UI Automation range for text[start..start+expected.Length], verified against the expected text. Apps count
    /// characters either as UTF-16 units or as grapheme clusters; we try both, then fall back to a text search.
    /// </summary>
    IUIAutomationTextRange? RangeFor(Field f, IUIAutomationTextRange doc, RangeCursor cursor, int start, string expected)
    {
        int length = expected.Length;
        var r = Attempt(f.Graphemes);
        if (r != null) return r;
        if (!TextModel.IsSimple(text, start + length))
        {
            cursor.Reset();
            r = Attempt(!f.Graphemes);
            cursor.Reset();
            if (r != null)
            {
                f.Graphemes = !f.Graphemes;
                return r;
            }
        }
        return FindOccurrence(doc, start, expected);

        IUIAutomationTextRange? Attempt(bool graphemes)
        {
            int u0 = graphemes ? TextModel.GraphemeIndex(text, start) : start;
            int u1 = graphemes ? TextModel.GraphemeIndex(text, start + length) : start + length;
            var range = cursor.RangeAt(u0, u1 - u0);
            return range != null && SameText(range.GetText(length + 8), expected) ? range : null;
        }
    }

    IUIAutomationTextRange? FindOccurrence(IUIAutomationTextRange doc, int start, string needle)
    {
        int nth = TextModel.OccurrencesBefore(text, needle, start);
        var search = doc.Clone();
        for (int k = 0; k <= nth; k++)
        {
            var found = search.FindText(needle, 0, 0);
            if (found == null) return null;
            if (k == nth) return found;
            search.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, found, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        }
        return null;
    }

    /// <summary>Chromium turns some spaces into no-break spaces; treat them as equal when verifying.</summary>
    static bool SameText(string? a, string b) => a != null && string.Equals(a.Replace('\u00A0', ' '), b.Replace('\u00A0', ' '), StringComparison.Ordinal);

    void Detach()
    {
        if (field == null) return;
        field = null;
        text = "";
        textVersion++;
        issues = new List<PlacedIssue>();
        issuesVersion++;
        ranges.Clear();
        lastField = Rect.Empty;
        geometryKey = -1;
        aiCts?.Cancel();
        if (state is CheckState.Checking or CheckState.TooLong or CheckState.Private) state = CheckState.Idle;
        PublishState();
        Publish(null);
    }

    /// <summary>With --diag: a once-a-second status file for troubleshooting. Structure and positions only, never text.</summary>
    void WriteDiag()
    {
        if (DateTime.UtcNow - diagAt < TimeSpan.FromSeconds(1)) return;
        diagAt = DateTime.UtcNow;
        var f = field;
        var g = lastGeometry;
        var o = new
        {
            at = DateTime.Now.ToString("HH:mm:ss.fff"),
            app = f?.Info.ProcessName,
            appName = f?.Info.AppName,
            kind = f?.Kind,
            canUnderline = f?.Info.CanUnderline,
            graphemes = f?.Graphemes,
            lastReject,
            state = state.ToString(),
            message,
            model,
            ms,
            textLength = text.Length,
            words = TextModel.CountWords(text),
            issues = issues.Select(i => new { i.Start, i.Issue.Length, i.Issue.Category, i.Issue.Type, i.Issue.Source, replacements = i.Issue.Replacements.Count }),
            field = g == null ? null : $"{g.Field.X:0},{g.Field.Y:0},{g.Field.Width:0},{g.Field.Height:0}",
            underlines = g?.Underlines.Select(u => new { u.Category, rects = u.Rects.Select(r => $"{r.X:0},{r.Y:0},{r.Width:0},{r.Height:0}") }),
            lastApply,
        };
        try
        {
            Directory.CreateDirectory(Log.Dir);
            File.WriteAllText(Path.Combine(Log.Dir, "diag.json"), JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
        }
    }
}
