using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NimbleWrite.Desktop.Core;
using NimbleWrite.Desktop.UI.Panels;

namespace NimbleWrite.Desktop.UI;

/// <summary>What the Settings and About windows need from the app. Tests pass a fake that never touches the real config file or registry.</summary>
internal interface ISettingsHost
{
    DesktopConfig Config { get; }
    ApiClient Api { get; }
    string EditorUrl { get; }
    /// <summary>The installed copy (bundled server, saved NVIDIA key), as opposed to a development copy.</summary>
    bool Packaged { get; }
    bool HasKey { get; }
    bool StartWithWindows { get; set; }
    bool ShortcutRegistered { get; }
    /// <summary>The app NimbleWrite was last used in (process name and display name), if any.</summary>
    string? LastApp { get; }
    string? LastAppName { get; }
    /// <summary><see cref="Config"/> was changed: save it and refresh the tray.</summary>
    void ConfigChanged();
    void SetAppDisabled(string process, bool off);
    void ResetApps();
    /// <summary>Switch the assistant shortcut to <paramref name="spec"/>; false (and nothing changes) if Windows won't give it to us.</summary>
    bool TryShortcut(string spec);
    void ShowKeyWindow();
    /// <summary>Quit and start again, to apply a new port or colours.</summary>
    void Restart();
    void OpenUrl(string url);
}

/// <summary>
/// NimbleWrite's settings, in a normal window with sections down the left like the editor's Settings page. Desktop
/// preferences are saved in desktop.json; writing settings are shared with the editor and saved on the server with
/// PATCH, so only what changed is sent and nothing set in the editor is lost.
/// </summary>
internal sealed class SettingsWindow : AppWindow
{
    /// <summary>The sections, and the matching section of the editor's Settings page (for "All settings in the editor").</summary>
    internal static readonly (string Id, string Label, string Glyph, string? Web)[] Sections =
    {
        ("general", "General", Icons.Settings, null),
        ("shortcut", "Shortcut", Icons.Keyboard, null),
        ("apps", "Apps", Icons.Apps, null),
        ("writing", "Writing", Icons.Writing, "suggestions"),
        ("ai", "AI & connection", Icons.Connection, "models"),
        ("appearance", "Appearance", Icons.Appearance, "appearance"),
    };

    // Copied from the web app (shared/types.ts DIALECTS, shared/meta.ts CATEGORY_META and GOAL_OPTIONS); a test keeps them in step.
    internal static readonly (string Id, string Label)[] Dialects =
    {
        ("en-GB", "British English"), ("en-US", "American English"), ("en-CA", "Canadian English"), ("en-AU", "Australian English"), ("en-IN", "Indian English"),
    };

    internal static readonly (string Id, string Label, string Description)[] Categories =
    {
        ("correctness", "Correctness", "Spelling, grammar and punctuation"),
        ("clarity", "Clarity", "Concise, easy-to-follow sentences"),
        ("engagement", "Engagement", "Vivid, varied word choice"),
        ("delivery", "Delivery", "Tone, formality and confidence"),
    };

    internal static readonly (string Id, string Label, string Hint)[] Audience =
    {
        ("general", "General", "Easy for anyone to read with minimal effort."),
        ("knowledgeable", "Knowledgeable", "Requires focus to read and understand."),
        ("expert", "Expert", "May require rereading to understand."),
    };

    internal static readonly (string Id, string Label, string Hint)[] Formality =
    {
        ("informal", "Informal", "Relaxed: contractions, slang and emoji are fine."),
        ("neutral", "Neutral", "Restricts slang but allows standard casual expressions."),
        ("formal", "Formal", "Restricts slang, colloquialisms and casual expressions."),
    };

    internal static readonly (string Id, string Label, string Hint)[] Domain =
    {
        ("academic", "Academic", "Strictly applies all rules and formal writing conventions."),
        ("business", "Business", "Applies almost all rules, with some leeway for informality."),
        ("general", "General", "Applies most rules and conventions with medium strictness."),
        ("email", "Email", "Applies most rules, adjusted for email conventions."),
        ("casual", "Casual", "Applies most rules but allows stylistic flexibility."),
        ("creative", "Creative", "Allows some intentional bending of rules for expressive writing."),
    };

    internal static readonly (string Id, string Label, string Hint)[] Intent =
    {
        ("inform", "Inform", "Explain facts or ideas."),
        ("describe", "Describe", "Paint a picture with detail."),
        ("convince", "Convince", "Persuade the reader."),
        ("tell_story", "Tell a story", "Narrate events."),
    };

    const string DefaultShortcut = "Ctrl+Alt+N";

    readonly ISettingsHost host;
    readonly Dictionary<string, FrameworkElement> built = new();
    readonly StackPanel nav = new();
    readonly ScrollViewer scroll;
    string section = "general";

    // General
    TextBlock? pauseText;
    Border? pauseButton;
    TextBlock? generalStatus;
    // Shortcut
    TextBlock? shortcutNow, shortcutStatus;
    TextBox? capture;
    string? candidate;
    // Apps
    StackPanel? appsList;
    Border? lastAppButton;
    // Writing
    StackPanel? writingBody;
    TextBlock? writingStatus;
    WritingSettings? writing;
    readonly Dictionary<string, ToggleRow> categoryToggles = new();
    // AI & connection
    StackPanel? aiBody;
    TextBox? portBox;
    TextBlock? portStatus;
    Border? portRestart;
    // Appearance
    TextBlock? appearanceStatus;
    Border? appearanceRestart;

    public SettingsWindow(ISettingsHost host, string? start = null)
    {
        this.host = host;
        Title = "NimbleWrite Settings";
        Width = 780;
        Height = 600;
        MinWidth = 680;
        MinHeight = 440;

        var root = new Grid { Background = Theme.Surface };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(204) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var side = new StackPanel { Margin = new Thickness(12, 16, 12, 12) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 16) };
        brand.Children.Add(UiKit.Logo(26));
        var title = UiKit.Label("Settings", 16, FontWeights.SemiBold);
        title.Margin = new Thickness(10, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(title);
        side.Children.Add(brand);
        side.Children.Add(nav);
        AutomationName(nav, "Settings sections");
        root.Children.Add(new Border { Background = Theme.Raised, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = side });

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SectionHost = new ContentControl { Focusable = false };
        scroll = new ScrollViewer
        {
            Content = SectionHost,
            Padding = new Thickness(28, 20, 22, 16),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        main.Children.Add(scroll);
        main.Children.Add(Footer());
        Grid.SetColumn(main, 1);
        root.Children.Add(main);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
        ShowSection(Sections.Any(s => s.Id == start) ? start! : "general");
    }

    /// <summary>The current section's content (inside the scrolling area).</summary>
    internal ContentControl SectionHost { get; }

    /// <summary>The section's server request while it loads (writing settings, status); completed when there's nothing to load.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>The last writing-settings save.</summary>
    internal Task Pending { get; private set; } = Task.CompletedTask;

    internal ChipGroup? DialectChips { get; private set; }
    internal ChipGroup? AudienceChips { get; private set; }

    internal string WritingStatus => writingStatus?.Text ?? "";

    public void ShowSection(string id)
    {
        if (Sections.All(s => s.Id != id)) id = "general";
        section = id;
        if (!built.TryGetValue(id, out var view))
        {
            view = id switch
            {
                "shortcut" => BuildShortcut(),
                "apps" => BuildApps(),
                "writing" => BuildWriting(),
                "ai" => BuildAi(),
                "appearance" => BuildAppearance(),
                _ => BuildGeneral(),
            };
            built[id] = view;
        }
        SectionHost.Content = view;
        scroll.ScrollToTop();
        RenderNav();
        // Shared with the editor and the server, so read them fresh each time the section is shown.
        Loading = id switch
        {
            "writing" => LoadWritingAsync(),
            "ai" => LoadAiAsync(),
            _ => Task.CompletedTask,
        };
        if (id == "apps") RenderApps();
        if (id == "general") RenderPause();
    }

    void RenderNav()
    {
        nav.Children.Clear();
        foreach (var (id, label, glyph, _) in Sections)
        {
            bool on = id == section;
            var row = new DockPanel();
            var icon = UiKit.Glyph(glyph, 15, on ? Theme.BrandText : Theme.Muted);
            icon.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(icon);
            row.Children.Add(UiKit.Label(label, 13, on ? FontWeights.SemiBold : FontWeights.Normal, on ? Theme.BrandText : Theme.Text));
            var b = UiKit.Clickable(row, () => ShowSection(id), Kind.Ghost);
            b.Padding = new Thickness(10, 7, 10, 7);
            b.Margin = new Thickness(0, 0, 0, 2);
            if (on) b.Background = Theme.Hover;
            System.Windows.Automation.AutomationProperties.SetName(b, label);
            System.Windows.Automation.AutomationProperties.SetItemStatus(b, on ? "Selected" : "");
            nav.Children.Add(b);
        }
    }

    FrameworkElement Footer()
    {
        var bar = new DockPanel { LastChildFill = true };
        var link = UiKit.Button("All settings in the editor", OpenEditorSettings, Kind.Secondary, "Opens the matching page of the editor's Settings in your browser");
        DockPanel.SetDock(link, Dock.Right);
        bar.Children.Add(link);
        var note = UiKit.Label("Personal dictionary, style guide, snippets and AI models are in the editor.", 12, null, Theme.Muted, wrap: true);
        note.VerticalAlignment = VerticalAlignment.Center;
        note.Margin = new Thickness(0, 0, 12, 0);
        bar.Children.Add(note);
        var footer = new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(28, 10, 22, 12), Child = bar };
        Grid.SetRow(footer, 1);
        return footer;
    }

    internal void OpenEditorSettings()
    {
        string? web = Sections.First(s => s.Id == section).Web;
        host.OpenUrl(host.EditorUrl.TrimEnd('/') + "/#/settings" + (web != null ? "/" + web : ""));
    }

    // ---- building blocks ----

    static StackPanel Page(string title, string intro)
    {
        var p = new StackPanel();
        p.Children.Add(UiKit.Label(title, 20, FontWeights.SemiBold));
        var t = UiKit.Label(intro, 13, null, Theme.Muted, wrap: true);
        t.Margin = new Thickness(0, 4, 0, 16);
        p.Children.Add(t);
        return p;
    }

    static Border Card(string? title, string? description, UIElement body)
    {
        var s = new StackPanel();
        if (title != null) s.Children.Add(UiKit.Label(title, 14, FontWeights.SemiBold));
        if (description != null)
        {
            var d = UiKit.Label(description, 12.5, null, Theme.Muted, wrap: true);
            d.Margin = new Thickness(0, 2, 0, 10);
            s.Children.Add(d);
        }
        else if (title != null) s.Children.Add(new Border { Height = 8 });
        s.Children.Add(body);
        return new Border
        {
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 14),
            Child = s,
        };
    }

    static TextBlock SubHeading(string text, double top = 10)
    {
        var t = UiKit.Label(text, 12.5, FontWeights.SemiBold, Theme.Text);
        t.Margin = new Thickness(0, top, 0, 4);
        return t;
    }

    static TextBox Field(string text, double width, string name)
    {
        var box = new TextBox
        {
            Text = text,
            Width = width,
            Height = 32,
            FontSize = 13.5,
            Padding = new Thickness(8, 5, 8, 5),
            Background = Theme.Raised,
            Foreground = Theme.Text,
            BorderBrush = Theme.Line,
            CaretBrush = Theme.Text,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationName(box, name);
        return box;
    }

    /// <summary>A status line with a "Restart now" button that appears when a change needs a restart.</summary>
    (TextBlock Status, Border Restart, FrameworkElement Row) RestartRow()
    {
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var restart = UiKit.Button("Restart now", host.Restart, Kind.Primary, "Quit NimbleWrite and start it again");
        restart.Visibility = Visibility.Collapsed;
        restart.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(restart, Dock.Right);
        row.Children.Add(restart);
        var status = StatusLine(0);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(status);
        return (status, restart, row);
    }

    // ---- General ----

    FrameworkElement BuildGeneral()
    {
        var p = Page("General", "How NimbleWrite behaves in your other apps.");
        var body = new StackPanel();
        body.Children.Add(new ToggleRow("Check my writing in other apps", host.Config.Enabled, SetEnabled));
        var pause = new DockPanel { Margin = new Thickness(26, 2, 0, 8), LastChildFill = true };
        pauseButton = UiKit.Button("Pause for 1 hour", TogglePause, Kind.Secondary);
        DockPanel.SetDock(pauseButton, Dock.Right);
        pause.Children.Add(pauseButton);
        pauseText = UiKit.Label("", 12.5, null, Theme.Muted, wrap: true);
        pauseText.VerticalAlignment = VerticalAlignment.Center;
        pause.Children.Add(pauseText);
        body.Children.Add(pause);
        body.Children.Add(new ToggleRow("Underline suggestions in the text", host.Config.ShowUnderlines, SetUnderlines));
        body.Children.Add(new ToggleRow("Start NimbleWrite when I sign in to Windows", host.StartWithWindows, SetStartWithWindows));
        generalStatus = StatusLine(4);
        body.Children.Add(generalStatus);
        p.Children.Add(Card(null, null, body));
        RenderPause();
        return p;
    }

    internal void SetEnabled(bool on)
    {
        host.Config.Enabled = on;
        host.Config.PausedUntil = DateTime.MinValue;
        host.ConfigChanged();
        RenderPause();
    }

    internal void SetUnderlines(bool on)
    {
        host.Config.ShowUnderlines = on;
        host.ConfigChanged();
    }

    internal void SetStartWithWindows(bool on)
    {
        try
        {
            host.StartWithWindows = on;
        }
        catch (Exception e)
        {
            Log.Error("start with Windows", e);
            if (generalStatus != null) Announce(generalStatus, "Windows didn't allow that change.", ErrorBrush);
        }
    }

    internal void TogglePause()
    {
        bool paused = DateTime.Now < host.Config.PausedUntil;
        host.Config.PausedUntil = paused ? DateTime.MinValue : DateTime.Now.AddHours(1);
        host.ConfigChanged();
        RenderPause();
    }

    void RenderPause()
    {
        if (pauseText == null || pauseButton == null) return;
        bool paused = DateTime.Now < host.Config.PausedUntil;
        pauseText.Text = !host.Config.Enabled ? "Turned off everywhere." : paused ? $"Paused until {host.Config.PausedUntil:HH:mm}." : "Checking as you type.";
        ((TextBlock)pauseButton.Child).Text = paused ? "Resume now" : "Pause for 1 hour";
        pauseButton.Visibility = host.Config.Enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Shortcut ----

    FrameworkElement BuildShortcut()
    {
        var p = Page("Shortcut", "Press it in any app to open the assistant for the text box you're in.");
        var body = new StackPanel();
        shortcutNow = UiKit.Label("", 13, FontWeights.SemiBold, Theme.Text, wrap: true);
        body.Children.Add(shortcutNow);
        body.Children.Add(SubHeading("New shortcut", 12));
        capture = Field(host.Config.Hotkey, 260, "New shortcut: press the keys together");
        capture.IsReadOnly = true;
        capture.IsReadOnlyCaretVisible = false;
        capture.PreviewKeyDown += OnCaptureKey;
        body.Children.Add(capture);
        var hint = Widgets.Fine("Click the box, then press the keys together. Use Ctrl, Alt or Win with a letter, number or F-key.");
        hint.Margin = new Thickness(0, 4, 0, 10);
        body.Children.Add(hint);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(UiKit.Spaced(UiKit.Button("Use this shortcut", () => ApplyShortcut(candidate ?? capture.Text), Kind.Primary), 8, 0));
        buttons.Children.Add(UiKit.Button($"Back to {DefaultShortcut}", () =>
        {
            capture.Text = DefaultShortcut;
            ApplyShortcut(DefaultShortcut);
        }, Kind.Ghost));
        body.Children.Add(buttons);
        shortcutStatus = StatusLine(10);
        body.Children.Add(shortcutStatus);
        p.Children.Add(Card(null, null, body));
        RenderShortcutNow();
        return p;
    }

    void OnCaptureKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= ModifierKeys.Windows;
        // Tab, Shift+Tab and Escape keep their usual jobs, so the box never traps the keyboard.
        if (key == Key.Tab && (mods & ~ModifierKeys.Shift) == 0 || key == Key.Escape && mods == 0) return;
        e.Handled = true;
        string? spec = HotkeyListener.Format(mods, key);
        if (spec != null)
        {
            candidate = spec;
            capture!.Text = spec;
            Announce(shortcutStatus!, $"Press \u201CUse this shortcut\u201D to switch to {spec}.", Theme.Muted);
        }
        else if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            capture!.Text = string.Join("+", new[] { (ModifierKeys.Control, "Ctrl"), (ModifierKeys.Alt, "Alt"), (ModifierKeys.Shift, "Shift"), (ModifierKeys.Windows, "Win") }
                .Where(m => mods.HasFlag(m.Item1)).Select(m => m.Item2)) + "+…";
        }
        else Announce(shortcutStatus!, "Hold Ctrl, Alt or Win as well, so the shortcut doesn't get in the way of typing.", WarnBrush);
    }

    /// <summary>Switch the shortcut; says what happened (also shown under the box).</summary>
    internal string ApplyShortcut(string spec)
    {
        spec = spec.Trim();
        bool ok = false;
        string message;
        if (!HotkeyListener.TryParse(spec, out _, out _)) message = "That isn't a shortcut NimbleWrite can use. Press the keys together in the box.";
        else if (string.Equals(spec, host.Config.Hotkey, StringComparison.OrdinalIgnoreCase) && host.ShortcutRegistered)
        {
            ok = true;
            message = $"{spec} already opens the assistant.";
        }
        else if (host.TryShortcut(spec))
        {
            ok = true;
            message = $"{spec} opens the assistant.";
        }
        else message = $"Another app already uses {spec}. Try a different one.";
        candidate = null;
        if (shortcutStatus != null) Announce(shortcutStatus, message, ok ? OkBrush : ErrorBrush);
        RenderShortcutNow();
        return message;
    }

    void RenderShortcutNow()
    {
        if (shortcutNow == null) return;
        shortcutNow.Text = host.ShortcutRegistered ? $"Now: {host.Config.Hotkey}" : Tray.ShortcutLabel(host.Config.Hotkey, false);
        shortcutNow.Foreground = host.ShortcutRegistered ? Theme.Text : WarnBrush;
    }

    // ---- Apps ----

    FrameworkElement BuildApps()
    {
        var p = Page("Apps", "NimbleWrite stays off in these apps. Terminals, code editors and password managers are off from the start.");
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        lastAppButton = UiKit.Button("", TurnOffLastApp, Kind.Secondary, "Stop checking in the app you last used NimbleWrite in");
        actions.Children.Add(UiKit.Spaced(lastAppButton, 8, 0));
        actions.Children.Add(UiKit.Button("Reset to the default list", ResetApps, Kind.Ghost, "Turn NimbleWrite back on everywhere except the apps it starts off in"));
        p.Children.Add(actions);
        appsList = new StackPanel();
        p.Children.Add(Card("Turned off in", null, appsList));
        return p;
    }

    void RenderApps()
    {
        if (appsList == null || lastAppButton == null) return;
        appsList.Children.Clear();
        var apps = host.Config.DisabledApps.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
        if (apps.Count == 0) appsList.Children.Add(UiKit.Label("NimbleWrite is on in every app.", 12.5, null, Theme.Muted));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var a in apps)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 10, 2) };
            var on = UiKit.Button("Turn on", () => TurnBackOn(a), Kind.Ghost, $"Check writing in {a} again");
            DockPanel.SetDock(on, Dock.Right);
            row.Children.Add(on);
            var name = UiKit.Label(a, 12.5);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.ToolTip = a;
            row.Children.Add(name);
            grid.Children.Add(row);
        }
        appsList.Children.Add(grid);
        string? last = host.LastApp;
        bool canAdd = last != null && !host.Config.IsAppDisabled(last);
        lastAppButton.Visibility = canAdd ? Visibility.Visible : Visibility.Collapsed;
        if (canAdd) ((TextBlock)lastAppButton.Child).Text = $"Turn off in {host.LastAppName ?? last}";
    }

    internal void TurnOffLastApp()
    {
        if (host.LastApp is not { } p) return;
        host.SetAppDisabled(p, true);
        RenderApps();
    }

    internal void TurnBackOn(string process)
    {
        host.SetAppDisabled(process, false);
        RenderApps();
    }

    internal void ResetApps()
    {
        host.ResetApps();
        RenderApps();
    }

    // ---- Writing (shared with the editor; saved on the server) ----

    FrameworkElement BuildWriting()
    {
        var p = Page("Writing", "How NimbleWrite checks your writing, here and in the editor. Changes are saved straight away.");
        writingStatus = StatusLine(0);
        writingStatus.Margin = new Thickness(0, 0, 0, 8);
        p.Children.Add(writingStatus);
        writingBody = new StackPanel();
        p.Children.Add(writingBody);
        return p;
    }

    async Task LoadWritingAsync()
    {
        Announce(writingStatus!, "Loading…", Theme.Muted);
        try
        {
            writing = await host.Api.GetWritingSettingsAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            writing = null;
            DialectChips = AudienceChips = null;
            writingBody!.Children.Clear();
            Announce(writingStatus!, "NimbleWrite's checker isn't running, so writing settings can't be changed right now.", WarnBrush);
            var retry = UiKit.Button("Try again", () => Loading = LoadWritingAsync(), Kind.Secondary);
            retry.HorizontalAlignment = HorizontalAlignment.Left;
            writingBody.Children.Add(retry);
            return;
        }
        Announce(writingStatus!, "", Theme.Muted);
        RenderWriting();
    }

    void RenderWriting()
    {
        var w = writing;
        if (w == null || writingBody == null) return;
        writingBody.Children.Clear();
        categoryToggles.Clear();

        DialectChips = new ChipGroup(Dialects.Select(d => (d.Id, d.Label, "")), w.Dialect);
        DialectChips.SelectedChanged += id => Save(new SettingsPatch { Dialect = id });
        writingBody.Children.Add(Card("English variant", "NimbleWrite checks spelling and suggests vocabulary for the variant you choose, and never \u2018corrects\u2019 you into another one.", DialectChips));

        var cats = new StackPanel();
        foreach (var (id, label, description) in Categories)
        {
            var t = new ToggleRow($"{label}: {description}", w.Categories.GetValueOrDefault(id, true), on => _ = SetCategory(id, on));
            categoryToggles[id] = t;
            cats.Children.Add(t);
        }
        cats.Children.Add(new ToggleRow("Instant local checks: repeated words, spacing and your variant\u2019s spellings, checked instantly alongside the AI", w.LocalRules, on => _ = SetLocalRules(on)));
        writingBody.Children.Add(Card("Suggestions", "Choose which kinds of suggestions NimbleWrite shows.", cats));

        var goals = new StackPanel();
        goals.Children.Add(SubHeading("Audience", 0));
        AudienceChips = Chips(Audience, w.DefaultGoals.Audience, id => SetGoals(g => g.Audience = id));
        goals.Children.Add(AudienceChips);
        goals.Children.Add(SubHeading("Formality"));
        goals.Children.Add(Chips(Formality, w.DefaultGoals.Formality, id => SetGoals(g => g.Formality = id)));
        goals.Children.Add(SubHeading("Domain"));
        goals.Children.Add(Chips(Domain, w.DefaultGoals.Domain, id => SetGoals(g => g.Domain = id)));
        goals.Children.Add(SubHeading("Intent"));
        foreach (var (id, label, hint) in Intent)
        {
            goals.Children.Add(new ToggleRow($"{label}: {hint}", w.DefaultGoals.Intent.Contains(id), on => _ = SetIntent(id, on)));
        }
        goals.Children.Add(UiKit.Divider(10, 6));
        goals.Children.Add(new ToggleRow("Show Set Goals for new documents", w.ShowGoalsOnNewDoc, on => Save(new SettingsPatch { ShowGoalsOnNewDoc = on })));
        writingBody.Children.Add(Card("Default goals for new documents", "Goals tailor suggestions to your audience, formality, domain and intent. You can change them for each document in the editor.", goals));
    }

    static ChipGroup Chips((string Id, string Label, string Hint)[] options, string selected, Action<string> changed)
    {
        var c = new ChipGroup(options, selected);
        c.SelectedChanged += changed;
        return c;
    }

    Task Save(SettingsPatch patch) => Pending = SaveAsync(patch);

    async Task SaveAsync(SettingsPatch patch)
    {
        Announce(writingStatus!, "Saving…", Theme.Muted);
        try
        {
            writing = await host.Api.PatchSettingsAsync(patch, CancellationToken.None);
            Announce(writingStatus!, "Saved", OkBrush);
        }
        catch (ApiException e)
        {
            Announce(writingStatus!, e.Message, ErrorBrush);
            RenderWriting(); // back to what the server has
        }
        catch (Exception)
        {
            Announce(writingStatus!, "Couldn't save: NimbleWrite's checker isn't running.", ErrorBrush);
            RenderWriting();
        }
    }

    internal Task SetCategory(string id, bool on)
    {
        var w = writing;
        if (w == null) return Task.CompletedTask;
        if (!on && !w.Categories.Any(kv => kv.Key != id && kv.Value))
        {
            Announce(writingStatus!, "Keep at least one suggestion category on.", WarnBrush);
            if (categoryToggles.TryGetValue(id, out var t)) t.On = true;
            return Task.CompletedTask;
        }
        return Save(new SettingsPatch { Categories = new() { [id] = on } });
    }

    internal Task SetLocalRules(bool on) => Save(new SettingsPatch { LocalRules = on });

    internal Task SetIntent(string id, bool on) => SetGoals(g =>
    {
        g.Intent.Remove(id);
        if (on) g.Intent.Add(id);
    });

    Task SetGoals(Action<Goals> change)
    {
        var w = writing;
        if (w == null) return Task.CompletedTask;
        var g = new Goals { Audience = w.DefaultGoals.Audience, Formality = w.DefaultGoals.Formality, Domain = w.DefaultGoals.Domain, Intent = new(w.DefaultGoals.Intent) };
        change(g);
        return Save(new SettingsPatch { DefaultGoals = g });
    }

    // ---- AI & connection ----

    FrameworkElement BuildAi()
    {
        var p = Page("AI & connection", "Where NimbleWrite's AI runs, and how the app reaches its checker on this PC.");
        aiBody = new StackPanel();
        p.Children.Add(aiBody);

        var port = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        portBox = Field(PortOf(host.Config.ServerUrl).ToString(CultureInfo.InvariantCulture), 100, "Port");
        portBox.Margin = new Thickness(0, 0, 8, 0);
        portBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            ApplyPort(portBox.Text);
        };
        row.Children.Add(portBox);
        row.Children.Add(UiKit.Button("Apply", () => ApplyPort(portBox.Text), Kind.Secondary));
        port.Children.Add(row);
        var note = Widgets.Fine(host.Packaged
            ? "The checker only listens on this PC (127.0.0.1). Change the port if another program needs 4317."
            : "Development copy: start the server on the same port (PORT=\u2026 npm start).");
        note.Margin = new Thickness(0, 6, 0, 0);
        port.Children.Add(note);
        var (status, restart, restartRow) = RestartRow();
        portStatus = status;
        portRestart = restart;
        port.Children.Add(restartRow);
        p.Children.Add(Card("Port", "The address NimbleWrite uses to reach its checker.", port));
        return p;
    }

    async Task LoadAiAsync()
    {
        aiBody!.Children.Clear();
        aiBody.Children.Add(UiKit.Label("Checking the connection\u2026", 12.5, null, Theme.Muted));
        var h = await host.Api.HealthInfoAsync(CancellationToken.None);
        RenderAi(h);
    }

    void RenderAi(HealthInfo? h)
    {
        aiBody!.Children.Clear();
        var nvidia = new StackPanel();
        string state = h == null ? "The checker isn't running, so the connection can't be checked."
            : h.NimConfigured ? "Connected. AI suggestions, rewrites and all the assistant's tools are on."
            : "Not connected. NimbleWrite uses its built-in spelling and grammar checks.";
        nvidia.Children.Add(UiKit.Label(state, 13, null, h?.NimConfigured == true ? OkBrush : Theme.Text, wrap: true));
        if (host.Packaged)
        {
            var key = UiKit.Button(host.HasKey ? "Change or remove the key\u2026" : "Connect to NVIDIA\u2026", host.ShowKeyWindow, Kind.Primary);
            key.HorizontalAlignment = HorizontalAlignment.Left;
            key.Margin = new Thickness(0, 10, 0, 0);
            nvidia.Children.Add(key);
        }
        else
        {
            var dev = Widgets.Fine("This is a development copy: it uses NVIDIA_API_KEY from the .env file in the NimbleWrite folder.");
            dev.Margin = new Thickness(0, 8, 0, 0);
            nvidia.Children.Add(dev);
        }
        aiBody.Children.Add(Card("NVIDIA", "NimbleWrite's AI runs on NVIDIA's hosted models with your own API key, which never leaves this PC except to NVIDIA.", nvidia));

        if (h == null) return;
        var models = new StackPanel();
        if (h.Models != null)
        {
            models.Children.Add(Model("Checking", h.Models.Check));
            models.Children.Add(Model("Writing and rewriting", h.Models.Write));
            models.Children.Add(Model("Translating", h.Models.Translate));
        }
        models.Children.Add(Model("Web search", h.SearchConfigured ? "Set up" : "Not set up: plagiarism checks compare with your documents and the sources you add"));
        var change = UiKit.Button("Change models in the editor", () => host.OpenUrl(host.EditorUrl.TrimEnd('/') + "/#/settings/models"), Kind.Secondary);
        change.HorizontalAlignment = HorizontalAlignment.Left;
        change.Margin = new Thickness(0, 10, 0, 0);
        models.Children.Add(change);
        aiBody.Children.Add(Card("Models", "The models the checker is using now.", models));
    }

    static FrameworkElement Model(string label, string value)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        s.Children.Add(UiKit.Label(label, 12, null, Theme.Muted));
        var v = UiKit.Label(value, 12.5, null, Theme.Text, wrap: true);
        s.Children.Add(v);
        return s;
    }

    /// <summary>Save a new port; null if it was fine, else the problem (also shown under the box).</summary>
    internal string? ApplyPort(string text)
    {
        string? problem = PortProblem(text, out int port);
        if (problem != null)
        {
            if (portStatus != null) Announce(portStatus, problem, ErrorBrush);
            return problem;
        }
        string url = $"http://127.0.0.1:{port}"; // loopback only: the checker must never be reachable from the network
        if (!string.Equals(url, host.Config.ServerUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            host.Config.ServerUrl = url;
            host.ConfigChanged();
            ShowRestart(portStatus, portRestart, $"Saved. Restart NimbleWrite to use port {port}.");
        }
        else if (portStatus != null) Announce(portStatus, $"NimbleWrite already uses port {port}.", Theme.Muted);
        return null;
    }

    internal static string? PortProblem(string text, out int port)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port)) return "Type a port number, like 4317.";
        return port is < 1024 or > 65535 ? "Use a port from 1024 to 65535." : null;
    }

    internal static int PortOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Port > 0 ? u.Port : 4317;

    void ShowRestart(TextBlock? status, Border? restart, string text)
    {
        if (status != null) Announce(status, text, Theme.Text);
        if (restart != null) restart.Visibility = Visibility.Visible;
    }

    // ---- Appearance ----

    FrameworkElement BuildAppearance()
    {
        var p = Page("Appearance", "The colours of NimbleWrite's panel, cards and windows.");
        var body = new StackPanel();
        var chips = new ChipGroup(new[] { ("system", "Same as Windows", ""), ("light", "Light", ""), ("dark", "Dark", "") }, host.Config.Theme);
        chips.SelectedChanged += SetAppearance;
        body.Children.Add(chips);
        var (status, restart, row) = RestartRow();
        appearanceStatus = status;
        appearanceRestart = restart;
        body.Children.Add(row);
        var note = Widgets.Fine("The editor in your browser has its own appearance setting.");
        note.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(note);
        p.Children.Add(Card("Colours", null, body));
        return p;
    }

    internal void SetAppearance(string id)
    {
        host.Config.Theme = id;
        host.ConfigChanged();
        ShowRestart(appearanceStatus, appearanceRestart, "Saved. Restart NimbleWrite to see the new colours.");
    }

    /// <summary>Lay the window out at its size and draw it, without showing it (render tests).</summary>
    internal BitmapSource RenderForTest()
    {
        var root = (FrameworkElement)Content;
        var size = new Size(Width, Height);
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        bmp.Freeze();
        return bmp;
    }
}
