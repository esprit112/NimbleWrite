using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI;

/// <summary>Owns the on-screen windows and turns engine snapshots and mouse movement into what you see.</summary>
internal sealed class UiController
{
    readonly DesktopConfig config;
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly OverlayWindow overlay = new();
    readonly BadgeWindow badge = new();
    readonly CardWindow card;
    readonly PanelWindow panel;
    readonly DispatcherTimer hoverTimer;
    Engine engine = null!;
    Tray? tray;
    EngineSnapshot? state;
    GeometrySnapshot? geo;
    string? hoverCandidate;
    DateTime hoverSince;
    DateTime? leftCardAt;
    string? highlight;

    public UiController(DesktopConfig config)
    {
        this.config = config;
        card = new CardWindow(this);
        panel = new PanelWindow(this);
        badge.Clicked += TogglePanel;
        hoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Input, (_, _) => OnHover(), dispatcher);
    }

    public void Bind(Engine e, Tray t)
    {
        engine = e;
        tray = t;
        hoverTimer.Start();
    }

    public void OnState(EngineSnapshot s)
    {
        bool newField = s.Target != null && !ReferenceEquals(s.Target, state?.Target);
        state = s;
        tray?.Update(s);
        if (s.Target == null)
        {
            HideAll();
            return;
        }
        if (newField) panel.ResetForNewField();
        if (badge.IsVisible) badge.Update(s);
        if (panel.IsVisible) panel.Update(s);
        if (card.IsVisible && !card.IsToast && s.Issues.All(i => i.Id != card.IssueId)) HideCard();
    }

    public void OnGeometry(GeometrySnapshot? g)
    {
        geo = g;
        if (g == null || state?.Target == null)
        {
            overlay.Hide();
            badge.Hide();
            if (card.IsVisible && !card.IsToast) HideCard();
            return;
        }
        overlay.Render(g, highlight);
        badge.Update(state);
        badge.PlaceInField(g.Field);
        if (card.IsVisible) card.BringToFront();
        if (panel.IsVisible)
        {
            panel.Reposition(g.Field);
            panel.BringToFront();
        }
    }

    public void OnNotice(string text)
    {
        if (panel.IsVisible)
        {
            panel.ShowNotice(text);
            return;
        }
        if (geo != null && state?.Target != null)
        {
            var f = geo.Field;
            card.ShowToast(text, new Rect(f.Right - 2, f.Bottom - 2, 1, 1));
            return;
        }
        tray?.Balloon("NimbleWrite", text);
    }

    void OnHover()
    {
        if (geo == null || state?.Target == null || !Native.GetCursorPos(out var p)) return;
        var pt = new Point(p.X, p.Y);
        if (card.IsVisible && card.ContainsScreenPoint(pt))
        {
            leftCardAt = null;
            return;
        }
        if (panel.IsVisible && panel.ContainsScreenPoint(pt)) return;
        string? over = null;
        foreach (var u in geo.Underlines)
        {
            if (u.Rects.Any(r => new Rect(r.X - 1, r.Y, r.Width + 2, r.Height + 4).Contains(pt)))
            {
                over = u.IssueId;
                break;
            }
        }
        var now = DateTime.UtcNow;
        if (over != null)
        {
            leftCardAt = null;
            if (over != hoverCandidate)
            {
                hoverCandidate = over;
                hoverSince = now;
            }
            else if (card.IssueId != over && now - hoverSince > TimeSpan.FromMilliseconds(220))
            {
                ShowCard(over);
            }
            return;
        }
        hoverCandidate = null;
        if (card.IsVisible && !card.IsToast)
        {
            leftCardAt ??= now;
            if (now - leftCardAt > TimeSpan.FromMilliseconds(450)) HideCard();
        }
    }

    void ShowCard(string id)
    {
        var issue = state?.Issues.FirstOrDefault(i => i.Id == id);
        var line = geo?.Underlines.FirstOrDefault(u => u.IssueId == id);
        if (issue == null || line == null || line.Rects.Length == 0) return;
        card.ShowIssue(issue, line.Rects[0]);
        SetHighlight(id);
    }

    void HideCard()
    {
        card.Close2();
        leftCardAt = null;
        SetHighlight(null);
    }

    public void SetHighlight(string? id)
    {
        if (highlight == id) return;
        highlight = id;
        overlay.SetHighlight(id);
    }

    void HideAll()
    {
        overlay.Hide();
        badge.Hide();
        card.Close2();
        panel.Hide();
        highlight = null;
        hoverCandidate = null;
    }

    // ---- commands from the windows ----

    public void Accept(string id, int index)
    {
        HideCard();
        engine.Apply(id, index);
    }

    public void Dismiss(string id)
    {
        HideCard();
        engine.Dismiss(id);
    }

    public void AddWord(string id)
    {
        HideCard();
        engine.AddToDictionary(id);
    }

    public void AcceptAll(IReadOnlyList<string> ids) => engine.ApplyAll(ids);

    public void Rewrite(string task) => engine.Rewrite(task, u => dispatcher.InvokeAsync(() => panel.OnRewrite(u)));

    public void CancelRewrite() => engine.CancelRewrite();

    public void ApplyRewrite(TextScope scope, string text) => engine.ApplyRewrite(scope, text);

    public void DetectTone() => engine.DetectTone((t, err) => dispatcher.InvokeAsync(() => panel.OnTone(t, err)));

    public void OpenEditor() => engine.OpenInEditor();

    public void PauseHour()
    {
        config.PausedUntil = DateTime.Now.AddHours(1);
        HideAll();
        tray?.Refresh();
        tray?.Balloon("NimbleWrite is paused", $"Back on at {config.PausedUntil:HH:mm}. Resume any time from the tray icon.");
    }

    public void TurnOffInCurrentApp()
    {
        var t = state?.Target;
        if (t == null) return;
        config.SetAppDisabled(t.ProcessName, true);
        HideAll();
        tray?.Balloon($"NimbleWrite is off in {t.AppName}", "Turn it back on from the tray icon menu.");
    }

    public void ClosePanel()
    {
        panel.Hide();
        SetHighlight(null);
    }

    public void OpenPanelFromCard()
    {
        HideCard();
        if (!panel.IsVisible) TogglePanel();
    }

    public void TogglePanel()
    {
        if (panel.IsVisible)
        {
            ClosePanel();
            return;
        }
        if (state?.Target == null || geo == null)
        {
            Log.Info($"assistant requested with no field (target={(state?.Target != null)}, geometry={(geo != null)})");
            tray?.Balloon("NimbleWrite", "Click into a text box first, then press " + config.Hotkey + " again.");
            return;
        }
        card.Close2();
        panel.Open(state, geo.Field);
        Log.Info($"assistant opened ({(geo.Field.IsEmpty ? "no field rectangle" : "beside the field")})");
    }
}
