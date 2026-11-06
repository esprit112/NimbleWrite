using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using NimbleWrite.Desktop.Core;
using NimbleWrite.Desktop.UI;
using Xunit;
using static NimbleWrite.Desktop.Tests.WpfTestKit;

namespace NimbleWrite.Desktop.Tests;

/// <summary>The Settings and About windows, the config they change, and the update check.</summary>
public sealed class SettingsAboutTests
{
    // ---- shortcut capture ----

    [Theory]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, Key.N, "Ctrl+Alt+N")]
    [InlineData(ModifierKeys.Shift | ModifierKeys.Control, Key.Space, "Ctrl+Shift+Space")]
    [InlineData(ModifierKeys.Windows | ModifierKeys.Alt, Key.D5, "Alt+Win+5")]
    [InlineData(ModifierKeys.Control, Key.F9, "Ctrl+F9")]
    public void ShortcutsAreWrittenTheWayDesktopJsonStoresThem(ModifierKeys mods, Key key, string want)
    {
        Assert.Equal(want, HotkeyListener.Format(mods, key));
        Assert.True(HotkeyListener.TryParse(want, out _, out _));
    }

    [Theory]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, Key.LeftCtrl)] // still holding modifiers
    [InlineData(ModifierKeys.None, Key.N)] // would fire on every N
    [InlineData(ModifierKeys.Shift, Key.A)] // would steal capital A
    [InlineData(ModifierKeys.Control, Key.System)]
    public void ShortcutsThatWouldBreakTypingAreRefused(ModifierKeys mods, Key key) => Assert.Null(HotkeyListener.Format(mods, key));

    // ---- port ----

    [Theory]
    [InlineData("4317", null)]
    [InlineData(" 50000 ", null)]
    [InlineData("80", "Use a port from 1024 to 65535.")]
    [InlineData("70000", "Use a port from 1024 to 65535.")]
    [InlineData("43a7", "Type a port number, like 4317.")]
    [InlineData("", "Type a port number, like 4317.")]
    public void PortsAreCheckedBeforeSaving(string text, string? problem) => Assert.Equal(problem, SettingsWindow.PortProblem(text, out _));

    [Theory]
    [InlineData("http://127.0.0.1:4317", 4317)]
    [InlineData("http://127.0.0.1:4399/", 4399)]
    [InlineData("nonsense", 4317)]
    public void ThePortIsReadFromTheServerUrl(string url, int port) => Assert.Equal(port, SettingsWindow.PortOf(url));

    // ---- config ----

    [Fact]
    public void AnOlderDesktopJsonLoadsWithTheNewDefaults()
    {
        var c = DesktopConfig.FromJson("{\"enabled\":false,\"hotkey\":\"Ctrl+Shift+K\",\"disabledApps\":[\"Code\"]}");
        Assert.False(c.Enabled);
        Assert.Equal("Ctrl+Shift+K", c.Hotkey);
        Assert.Equal(new[] { "Code" }, c.DisabledApps);
        Assert.Equal("system", c.Theme);
        Assert.Equal("review", c.LastTab);
        Assert.Equal(3, c.RecentLanguages.Count);
    }

    [Fact]
    public void SavingKeepsSettingsANewerVersionWrote()
    {
        var c = DesktopConfig.FromJson("{\"theme\":\"dark\",\"futureThing\":{\"a\":1},\"futureFlag\":true}");
        Assert.Equal("dark", c.Theme);
        c.Enabled = false;
        using var doc = JsonDocument.Parse(c.ToJson());
        Assert.Equal(1, doc.RootElement.GetProperty("futureThing").GetProperty("a").GetInt32());
        Assert.True(doc.RootElement.GetProperty("futureFlag").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Theory]
    [InlineData("dark", false, true)]
    [InlineData("light", true, false)]
    [InlineData("system", true, true)]
    [InlineData("system", false, false)]
    [InlineData("purple", true, true)]
    public void TheAppearanceSettingOverridesWindows(string pref, bool windowsDark, bool dark) => Assert.Equal(dark, Theme.ResolveDark(pref, windowsDark));

    // ---- Settings window ----

    [Fact]
    public void WritingChangesSendOnlyWhatChanged() => OnSta(() =>
    {
        var (host, server) = Host();
        var w = new SettingsWindow(host, "writing");
        Wait(w.Loading);
        Assert.Equal(HttpMethod.Get, server.Requests.Single().Method);

        w.DialectChips!.Select("en-US");
        Wait(w.Pending);
        Assert.Equal(("PATCH", "{\"dialect\":\"en-US\"}"), Last(server));

        Wait(w.SetCategory("engagement", false));
        Assert.Equal(("PATCH", "{\"categories\":{\"engagement\":false}}"), Last(server));

        w.AudienceChips!.Select("expert");
        Wait(w.Pending);
        Assert.Equal(("PATCH", "{\"defaultGoals\":{\"audience\":\"expert\",\"formality\":\"neutral\",\"domain\":\"general\",\"intent\":[\"inform\"]}}"), Last(server));

        Wait(w.SetIntent("convince", true));
        Assert.Contains("\"intent\":[\"inform\",\"convince\"]", Last(server).Body);

        Wait(w.SetLocalRules(false));
        Assert.Equal(("PATCH", "{\"localRules\":false}"), Last(server));
        Assert.Equal("Saved", w.WritingStatus);
        Assert.All(server.Requests, r => Assert.NotEqual(HttpMethod.Put, r.Method)); // never replace the whole settings object
    });

    [Fact]
    public void TheLastSuggestionCategoryCantBeTurnedOff() => OnSta(() =>
    {
        var (host, server) = Host("{\"dialect\":\"en-GB\",\"categories\":{\"correctness\":true,\"clarity\":false,\"engagement\":false,\"delivery\":false}}");
        var w = new SettingsWindow(host, "writing");
        Wait(w.Loading);
        Wait(w.SetCategory("correctness", false));
        Assert.Single(server.Requests); // just the GET
        Assert.Equal("Keep at least one suggestion category on.", w.WritingStatus);
    });

    [Fact]
    public void WritingSettingsSayWhenTheCheckerIsntRunning() => OnSta(() =>
    {
        var (host, server) = Host();
        server.Down = true;
        var w = new SettingsWindow(host, "writing");
        Wait(w.Loading);
        Assert.Contains("isn't running", w.WritingStatus);
        Assert.Null(w.DialectChips);
    });

    [Fact]
    public void DesktopChangesGoThroughTheHostAndNeverTheRealFile() => OnSta(() =>
    {
        var (host, _) = Host();
        var w = new SettingsWindow(host, "general");
        w.SetEnabled(false);
        w.SetUnderlines(false);
        w.SetStartWithWindows(true);
        Assert.False(host.Config.Enabled);
        Assert.False(host.Config.ShowUnderlines);
        Assert.True(host.StartWithWindows);
        Assert.Equal(2, host.Changes);

        w.TogglePause();
        Assert.True(host.Config.PausedUntil > DateTime.Now.AddMinutes(59));
        w.TogglePause();
        Assert.Equal(DateTime.MinValue, host.Config.PausedUntil);

        host.Config.DisabledApps = new() { "Code" };
        w.ShowSection("apps");
        w.TurnOffLastApp();
        Assert.Equal(new[] { "Code", "slack" }, host.Config.DisabledApps);
        w.TurnBackOn("Code");
        Assert.Equal(new[] { "slack" }, host.Config.DisabledApps);
        w.ResetApps();
        Assert.Equal(DesktopConfig.DefaultDisabledApps, host.Config.DisabledApps);

        Assert.Equal("Ctrl+Shift+Space opens the assistant.", w.ApplyShortcut("Ctrl+Shift+Space"));
        Assert.Equal("Ctrl+Shift+Space", host.Config.Hotkey);
        host.ShortcutTaken = true;
        Assert.Equal("Another app already uses Ctrl+Alt+K. Try a different one.", w.ApplyShortcut("Ctrl+Alt+K"));
        Assert.Equal("Ctrl+Shift+Space", host.Config.Hotkey);

        Assert.Null(w.ApplyPort("4400"));
        Assert.Equal("http://127.0.0.1:4400", host.Config.ServerUrl);
        Assert.Equal("Use a port from 1024 to 65535.", w.ApplyPort("22"));

        w.SetAppearance("dark");
        Assert.Equal("dark", host.Config.Theme);
    });

    [Theory]
    [InlineData("writing", "/#/settings/suggestions")]
    [InlineData("ai", "/#/settings/models")]
    [InlineData("appearance", "/#/settings/appearance")]
    [InlineData("general", "/#/settings")]
    public void AllSettingsInTheEditorOpensTheMatchingSection(string section, string path) => OnSta(() =>
    {
        var (host, _) = Host();
        var w = new SettingsWindow(host, section);
        w.OpenEditorSettings();
        Assert.Equal("http://127.0.0.1:4399" + path, host.Opened.Last());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySectionBuildsAndFitsInLightAndDark(bool dark) => OnSta(() =>
    {
        Theme.Apply(dark);
        try
        {
            string? dir = Environment.GetEnvironmentVariable("NW_RENDER_DIR");
            foreach (bool packaged in new[] { true, false })
            {
                var (host, _) = Host();
                host.Packaged = packaged;
                var w = new SettingsWindow(host, "general");
                foreach (var id in SettingsWindow.Sections.Select(s => s.Id))
                {
                    if (!packaged && id != "ai") continue;
                    w.ShowSection(id);
                    Wait(w.Loading);
                    var bmp = w.RenderForTest();
                    foreach (var fe in Descendants(w.SectionHost).OfType<FrameworkElement>().Where(e => e.ActualWidth > 0 && e.IsVisible))
                    {
                        var r = fe.TransformToAncestor(w.SectionHost).TransformBounds(new Rect(fe.RenderSize));
                        Assert.True(r.Right <= w.SectionHost.ActualWidth + 1, $"{id}: {fe.GetType().Name} \"{(fe as System.Windows.Controls.TextBlock)?.Text}\" sticks out to {r.Right:0} of {w.SectionHost.ActualWidth:0}");
                    }
                    if (dir != null) Save(bmp, Path.Combine(dir, $"settings-{id}{(packaged ? "" : "-dev")}-{(dark ? "dark" : "light")}.png"));
                }
            }
            var (h2, _) = Host();
            var about = new AboutWindow(h2);
            Wait(about.Loading);
            var a = about.RenderForTest();
            Assert.Contains("1.0.8", about.ServerVersionText);
            if (dir != null) Save(a, Path.Combine(dir, $"about-{(dark ? "dark" : "light")}.png"));
        }
        finally
        {
            Theme.Apply(false);
        }
    });

    [Fact]
    public void ListsCopiedFromTheWebStillMatch()
    {
        string root = RepoRoot();
        string types = File.ReadAllText(Path.Combine(root, "shared", "types.ts"));
        string meta = File.ReadAllText(Path.Combine(root, "shared", "meta.ts"));
        string dialects = Regex.Match(types, @"export const DIALECTS[^=]*= \[(.*?)\];", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal(Regex.Matches(dialects, @"\{ id: '([^']+)', label: '([^']+)' \}").Select(m => (m.Groups[1].Value, m.Groups[2].Value)), SettingsWindow.Dialects.Select(d => (d.Id, d.Label)));

        var cats = Regex.Matches(meta, @"  (\w+): \{ label: '([^']+)', color: '[^']+', description: '([^']+)' \}").Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value));
        Assert.Equal(cats, SettingsWindow.Categories);

        foreach (var (name, options) in new[] { ("audience", SettingsWindow.Audience), ("formality", SettingsWindow.Formality), ("domain", SettingsWindow.Domain), ("intent", SettingsWindow.Intent) })
        {
            string block = Regex.Match(meta, $@"  {name}: \[(.*?)\],", RegexOptions.Singleline).Groups[1].Value;
            var web = Regex.Matches(block, @"\{ id: '([^']+)', label: '([^']+)', hint: '([^']+)' \}").Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)).ToArray();
            Assert.NotEmpty(web);
            Assert.Equal(web, options);
        }
    }

    // ---- About ----

    [Theory]
    [InlineData("1.0.8+3f2a9c1", "1.0.8")]
    [InlineData("1.1.0", "1.1.0")]
    [InlineData("", "")]
    public void VersionsDropTheBuildSuffix(string informational, string want) => Assert.Equal(want, UpdateCheck.Clean(informational));

    [Theory]
    [InlineData("v1.1.0", "1.0.8", true)]
    [InlineData("v1.0.8", "1.0.8", false)]
    [InlineData("1.0.10", "1.0.9", true)]
    [InlineData("v1.0.7", "1.0.8", false)]
    [InlineData("v1.1.0-beta", "1.1.0", false)]
    [InlineData("latest", "1.0.8", false)]
    public void ANewerReleaseIsSpotted(string tag, string current, bool newer) => Assert.Equal(newer, UpdateCheck.IsNewer(tag, current));

    [Fact]
    public async Task TheUpdateCheckAsksGitHubOnceAndOnlyTrustsGitHubLinks()
    {
        var gh = new Recorder { Reply = "{\"tag_name\":\"v1.1.0\",\"html_url\":\"https://github.com/esprit112/NimbleWrite/releases/tag/v1.1.0\"}" };
        var r = await UpdateCheck.CheckAsync("1.0.8", gh, CancellationToken.None);
        Assert.True(r.Newer);
        Assert.Equal("1.1.0", r.Latest);
        Assert.Equal("https://github.com/esprit112/NimbleWrite/releases/tag/v1.1.0", r.Url);
        var req = Assert.Single(gh.Requests);
        Assert.Equal("https://api.github.com/repos/esprit112/NimbleWrite/releases/latest", req.Url);
        Assert.StartsWith("NimbleWrite/1.0.8", req.UserAgent);

        gh.Reply = "{\"tag_name\":\"v1.2.0\",\"html_url\":\"https://evil.example/download\"}";
        Assert.Equal(UpdateCheck.ReleasesPage, (await UpdateCheck.CheckAsync("1.0.8", gh, CancellationToken.None)).Url);

        gh.Status = HttpStatusCode.NotFound;
        var none = await UpdateCheck.CheckAsync("1.0.8", gh, CancellationToken.None);
        Assert.False(none.Newer);
        Assert.Equal("No releases have been published yet.", none.Message);
    }

    [Fact]
    public void DiagnosticsHoldNoTextNoKeyAndNoAppNames()
    {
        var config = new DesktopConfig { DisabledApps = new() { "SecretProjectTool", "Code" }, Hotkey = "Ctrl+Alt+N" };
        var health = new HealthInfo { Ok = true, NimConfigured = true, Version = "1.0.8", Packaged = true, Models = new ModelSettings { Check = "c-model", Write = "w-model", Translate = "t-model" } };
        string d = AboutWindow.Diagnostics("1.0.8", health, config, packaged: true, hasKey: true, shortcutRegistered: true, startWithWindows: false);
        Assert.Contains("Desktop 1.0.8 (installed copy)", d);
        Assert.Contains("Checker 1.0.8", d);
        Assert.Contains("Apps turned off: 2", d);
        Assert.Contains("w-model", d);
        Assert.DoesNotContain("SecretProjectTool", d);
        Assert.DoesNotContain("nvapi", d, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, d, StringComparison.OrdinalIgnoreCase); // no paths with the user's name

        string offline = AboutWindow.Diagnostics("1.0.8", null, config, packaged: false, hasKey: false, shortcutRegistered: false, startWithWindows: false);
        Assert.Contains("Checker: not running", offline);
        Assert.Contains("development copy", offline);
    }

    [Fact]
    public void TheLicenceIsFoundBesideTheAppOrInTheRepository()
    {
        string dir = Directory.CreateTempSubdirectory("nw-lic").FullName;
        try
        {
            Assert.Null(AboutWindow.LicenceFile(dir));
            File.WriteAllText(Path.Combine(dir, "LICENSE"), "MIT");
            Assert.Equal(Path.Combine(dir, "LICENSE"), AboutWindow.LicenceFile(dir));
            Directory.CreateDirectory(Path.Combine(dir, "licenses"));
            File.WriteAllText(Path.Combine(dir, "licenses", "THIRD-PARTY-NOTICES.txt"), "notices");
            Assert.Equal(Path.Combine(dir, "licenses", "THIRD-PARTY-NOTICES.txt"), AboutWindow.LicenceFile(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
        Assert.EndsWith("LICENSE", AboutWindow.LicenceFile(AppContext.BaseDirectory)); // the repository's, found by walking up
    }

    // ---- helpers ----

    static (string Method, string Body) Last(FakeServer s) => (s.Requests[^1].Method.Method, s.Requests[^1].Body);

    const string SettingsJson = "{\"dialect\":\"en-GB\",\"defaultGoals\":{\"audience\":\"knowledgeable\",\"formality\":\"neutral\",\"domain\":\"general\",\"intent\":[\"inform\"]},\"showGoalsOnNewDoc\":true,\"categories\":{\"correctness\":true,\"clarity\":true,\"engagement\":true,\"delivery\":true},\"localRules\":true,\"theme\":\"system\",\"models\":{\"check\":\"nvidia/nemotron-3.5-lightning-30b-a3b\",\"write\":\"nvidia/nemotron-3-ultra-550b-a55b\",\"translate\":\"nvidia/nemotron-3-ultra-550b-a55b\"},\"mutedTypes\":[]}";

    static (FakeSettingsHost Host, FakeServer Server) Host(string settings = SettingsJson)
    {
        var server = new FakeServer { Settings = settings };
        return (new FakeSettingsHost(new ApiClient("http://127.0.0.1:4399", server)), server);
    }

    static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "shared", "meta.ts"))) return d.FullName;
        }
        throw new InvalidOperationException("Couldn't find the repository root above the test binaries.");
    }

    /// <summary>The local server: GET settings and health; PATCH settings merges what was sent and echoes the result.</summary>
    internal sealed class FakeServer : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Path, string Body)> Requests = new();
        public string Settings = SettingsJson;
        public bool Down;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Down) throw new HttpRequestException("No server.");
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/health")
            {
                return Json("{\"ok\":true,\"nimConfigured\":true,\"searchConfigured\":false,\"packaged\":false,\"version\":\"1.0.8\",\"models\":{\"check\":\"nvidia/nemotron-3.5-lightning-30b-a3b\",\"write\":\"nvidia/nemotron-3-ultra-550b-a55b\",\"translate\":\"nvidia/nemotron-3-ultra-550b-a55b\"}}");
            }
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, path, body));
            if (request.Method == HttpMethod.Patch)
            {
                var merged = System.Text.Json.Nodes.JsonNode.Parse(Settings)!.AsObject();
                foreach (var (k, v) in System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject())
                {
                    if (k == "categories")
                    {
                        foreach (var (c, on) in v!.AsObject()) merged["categories"]![c] = on!.DeepClone();
                    }
                    else merged[k] = v?.DeepClone();
                }
                Settings = merged.ToJsonString();
            }
            return Json(Settings);
        }

        static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };
    }

    sealed class Recorder : HttpMessageHandler
    {
        public readonly List<(string Url, string UserAgent)> Requests = new();
        public string Reply = "{}";
        public HttpStatusCode Status = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.UserAgent.ToString()));
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Reply, Encoding.UTF8, "application/json") });
        }
    }

    /// <summary>Changes the config in memory only: tests must never write the user's real desktop.json or registry.</summary>
    internal sealed class FakeSettingsHost : ISettingsHost
    {
        public FakeSettingsHost(ApiClient api) => Api = api;

        public DesktopConfig Config { get; } = new() { ServerUrl = "http://127.0.0.1:4399" };
        public ApiClient Api { get; }
        public string EditorUrl => "http://127.0.0.1:4399";
        public bool Packaged { get; set; } = true;
        public bool HasKey { get; set; } = true;
        public bool StartWithWindows { get; set; }
        public bool ShortcutRegistered { get; set; } = true;
        public bool ShortcutTaken;
        public string? LastApp => "slack";
        public string? LastAppName => "Slack";
        public int Changes;
        public readonly List<string> Opened = new();
        public int KeyWindows, Restarts;

        public void ConfigChanged() => Changes++;

        public void SetAppDisabled(string process, bool off)
        {
            var next = Config.DisabledApps.Where(a => !string.Equals(a, process, StringComparison.OrdinalIgnoreCase)).ToList();
            if (off) next.Add(process);
            Config.DisabledApps = next;
        }

        public void ResetApps() => Config.DisabledApps = new(DesktopConfig.DefaultDisabledApps);

        public bool TryShortcut(string spec)
        {
            if (ShortcutTaken) return false;
            Config.Hotkey = spec;
            return true;
        }

        public void ShowKeyWindow() => KeyWindows++;
        public void Restart() => Restarts++;
        public void OpenUrl(string url) => Opened.Add(url);
    }
}
