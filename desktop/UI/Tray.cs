using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NimbleWrite.Desktop.Core;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace NimbleWrite.Desktop.UI;

internal enum TrayLook { Normal, Paused, Offline }

/// <summary>The notification-area icon and its menu: on/off, pause, per-app switches, start with Windows, quit.</summary>
internal sealed class Tray : IDisposable
{
    readonly WF.NotifyIcon icon;
    readonly WF.ContextMenuStrip menu = new();
    readonly DesktopConfig config;
    readonly Engine engine;
    readonly string editorUrl;
    readonly Action quit;
    readonly Action? connect;
    readonly Func<bool> hasKey;
    readonly Func<bool> shortcutWorks;
    EngineSnapshot? last;
    TrayLook look = (TrayLook)(-1);
    SD.Icon? current;

    /// <param name="connect">Opens "Connect to NVIDIA" (installed app only; null hides the menu item).</param>
    /// <param name="shortcutWorks">False when Windows refused the assistant shortcut (taken by another app, or not valid).</param>
    public Tray(DesktopConfig config, Engine engine, string editorUrl, Action quit, Action? connect = null, Func<bool>? hasKey = null, Func<bool>? shortcutWorks = null)
    {
        this.config = config;
        this.engine = engine;
        this.editorUrl = editorUrl;
        this.quit = quit;
        this.connect = connect;
        this.hasKey = hasKey ?? (() => false);
        this.shortcutWorks = shortcutWorks ?? (() => true);
        menu.Opening += (_, _) => Rebuild();
        icon = new WF.NotifyIcon { Text = "NimbleWrite", ContextMenuStrip = menu, Visible = true };
        icon.MouseUp += (_, e) =>
        {
            if (e.Button == WF.MouseButtons.Left) ShowMenu();
        };
        Refresh();
    }

    void ShowMenu()
    {
        // Same as a right-click, so the menu closes properly when you click elsewhere.
        var show = typeof(WF.NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        if (show != null) show.Invoke(icon, null);
        else menu.Show(WF.Cursor.Position);
    }

    public void Update(EngineSnapshot s)
    {
        last = s;
        Refresh();
    }

    public void Refresh()
    {
        var want = !config.IsActive ? TrayLook.Paused : last?.State == CheckState.Offline ? TrayLook.Offline : TrayLook.Normal;
        if (want != look)
        {
            look = want;
            var old = current;
            current = IconFactory.Tray(WF.SystemInformation.SmallIconSize.Width, want);
            icon.Icon = current;
            old?.Dispose();
        }
        string tip = "NimbleWrite: " + Status();
        icon.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    public void Balloon(string title, string text) => icon.ShowBalloonTip(4000, title, text, WF.ToolTipIcon.None);

    string Status()
    {
        if (!config.Enabled) return "turned off";
        if (DateTime.Now < config.PausedUntil) return $"paused until {config.PausedUntil:HH:mm}";
        var s = last;
        if (s?.State == CheckState.Offline) return "can't reach the server";
        if (s?.Target == null) return "click into any text box";
        int n = s.Issues.Count;
        if (s.State == CheckState.Checking) return $"checking in {s.Target.AppName}…";
        return n == 0 ? $"all clear in {s.Target.AppName}" : $"{n} suggestion{(n == 1 ? "" : "s")} in {s.Target.AppName}";
    }

    void Rebuild()
    {
        foreach (var item in menu.Items.OfType<WF.ToolStripItem>().ToList()) item.Dispose();
        menu.Items.Clear();
        menu.Items.Add(new WF.ToolStripMenuItem("NimbleWrite") { Enabled = false, Font = new SD.Font(menu.Font, SD.FontStyle.Bold) });
        string status = Status();
        menu.Items.Add(new WF.ToolStripMenuItem(char.ToUpperInvariant(status[0]) + status[1..]) { Enabled = false });
        menu.Items.Add(new WF.ToolStripSeparator());

        menu.Items.Add(Item(config.Enabled ? "Turn off everywhere" : "Turn on", () =>
        {
            config.Enabled = !config.Enabled;
            config.PausedUntil = DateTime.MinValue;
            config.Save();
            Refresh();
        }));
        bool paused = DateTime.Now < config.PausedUntil;
        if (config.Enabled)
        {
            menu.Items.Add(Item(paused ? "Resume now" : "Pause for 1 hour", () =>
            {
                config.PausedUntil = paused ? DateTime.MinValue : DateTime.Now.AddHours(1);
                Refresh();
            }));
        }
        string? proc = last?.Target?.ProcessName ?? engine.LastProcess;
        string? app = last?.Target?.AppName ?? engine.LastAppName ?? proc;
        if (proc != null && app != null)
        {
            bool off = config.IsAppDisabled(proc);
            menu.Items.Add(Item(off ? $"Turn on in {app}" : $"Turn off in {app}", () => config.SetAppDisabled(proc, !off)));
        }
        var disabled = config.DisabledApps.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
        if (disabled.Count > 0)
        {
            var sub = new WF.ToolStripMenuItem("Turned off in");
            foreach (var a in disabled) sub.DropDownItems.Add(Item(a, () => config.SetAppDisabled(a, false), "Click to turn NimbleWrite back on in this app"));
            menu.Items.Add(sub);
        }
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(Check("Show underlines", config.ShowUnderlines, v =>
        {
            config.ShowUnderlines = v;
            config.Save();
        }));
        menu.Items.Add(Check("Start with Windows", DesktopConfig.StartsWithWindows, DesktopConfig.SetStartWithWindows));
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(Item("Open the NimbleWrite editor", () => Open(editorUrl)));
        if (connect != null) menu.Items.Add(Item(hasKey() ? "NVIDIA API key…" : "Connect to NVIDIA…", connect, "Your own NVIDIA API key turns on AI suggestions"));
        menu.Items.Add(new WF.ToolStripMenuItem(ShortcutLabel(config.Hotkey, shortcutWorks())) { Enabled = false });
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(Item("Quit NimbleWrite", quit));
    }

    /// <summary>
    /// The menu's line about the assistant shortcut. When Windows refuses the shortcut (another app registered it
    /// first, or desktop.json holds something that isn't a shortcut), pressing it does nothing, so don't list it as
    /// working: say why, and point to the badge, which always opens the assistant.
    /// </summary>
    internal static string ShortcutLabel(string hotkey, bool registered)
    {
        if (registered) return $"Assistant shortcut: {hotkey}";
        return HotkeyListener.TryParse(hotkey, out _, out _)
            ? $"{hotkey} is used by another app: click the N badge instead"
            : $"Shortcut \"{hotkey}\" in desktop.json isn't valid: click the N badge instead";
    }

    static WF.ToolStripMenuItem Item(string text, Action onClick, string? tip = null)
    {
        var item = new WF.ToolStripMenuItem(text) { ToolTipText = tip };
        item.Click += (_, _) => onClick();
        return item;
    }

    static WF.ToolStripMenuItem Check(string text, bool on, Action<bool> onChange)
    {
        var item = new WF.ToolStripMenuItem(text) { Checked = on, CheckOnClick = true };
        item.CheckedChanged += (_, _) => onChange(item.Checked);
        return item;
    }

    static void Open(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Error("open url", e);
        }
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
        current?.Dispose();
    }
}

/// <summary>Draws the NimbleWrite mark for the tray at the exact pixel size Windows wants.</summary>
internal static class IconFactory
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr handle);

    public static SD.Icon Tray(int px, TrayLook look)
    {
        px = Math.Max(16, px);
        using var bmp = new SD.Bitmap(px, px, SD.Imaging.PixelFormat.Format32bppArgb);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = SD.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(SD.Color.Transparent);
            float s = px / 64f;
            using var bg = new SD.SolidBrush(look == TrayLook.Paused ? SD.Color.FromArgb(0x8A, 0x94, 0x90) : SD.Color.FromArgb(0x0B, 0x6B, 0x3A));
            using var path = Rounded(px, 16 * s);
            g.FillPath(bg, path);
            SD.PointF[] n = { new(18, 44), new(18, 20), new(24, 20), new(40, 36), new(40, 20), new(46, 20), new(46, 44), new(40, 44), new(24, 28), new(24, 44) };
            for (int i = 0; i < n.Length; i++) n[i] = new SD.PointF(n[i].X * s, n[i].Y * s);
            g.FillPolygon(SD.Brushes.White, n);
            using var dot = new SD.SolidBrush(look == TrayLook.Offline ? SD.Color.FromArgb(0xF5, 0x9E, 0x0B) : SD.Color.FromArgb(0x76, 0xB9, 0x00));
            float r = (look == TrayLook.Offline ? 9 : 6) * s;
            g.FillEllipse(dot, 50 * s - r, 46 * s - r, 2 * r, 2 * r);
        }
        IntPtr h = bmp.GetHicon();
        try
        {
            return (SD.Icon)SD.Icon.FromHandle(h).Clone();
        }
        finally
        {
            DestroyIcon(h);
        }
    }

    static SD.Drawing2D.GraphicsPath Rounded(float size, float radius)
    {
        var p = new SD.Drawing2D.GraphicsPath();
        float d = radius * 2;
        p.AddArc(0, 0, d, d, 180, 90);
        p.AddArc(size - d, 0, d, d, 270, 90);
        p.AddArc(size - d, size - d, d, d, 0, 90);
        p.AddArc(0, size - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
