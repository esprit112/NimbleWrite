using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NimbleWrite.Desktop.Core;
using NimbleWrite.Desktop.UI;
using NimbleWrite.Desktop.UI.Panels;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

/// <summary>The tabbed assistant panel and its building blocks, built for real on an STA thread with a fake host.</summary>
public sealed class PanelTests
{
    // ---- building blocks ----

    [Fact]
    public void ChipGroupRaisesSelectedChangedOncePerRealChange() => OnSta(() =>
    {
        var g = new ChipGroup(new[] { ("standard", "Standard", ""), ("formal", "Formal", ""), ("custom", "Custom", "Your own voice") }, "standard");
        var seen = new List<string>();
        g.SelectedChanged += id => seen.Add(id);

        g.Select("formal");
        g.Select("formal");
        g.Select("nope");
        g.Select("custom");

        Assert.Equal(new[] { "formal", "custom" }, seen);
        Assert.Equal("custom", g.Selected);
        g.Select("standard", notify: false);
        Assert.Equal(2, seen.Count);
        Assert.Equal("standard", g.Selected);
    });

    [Fact]
    public void ChipGroupTellsScreenReadersWhichChipIsSelected() => OnSta(() =>
    {
        var g = new ChipGroup(new[] { ("a", "Alpha", ""), ("b", "Beta", "") }, "b");
        var chips = g.Chips.ToList();
        Assert.Equal(2, chips.Count);
        Assert.Equal("", AutomationProperties.GetItemStatus(chips[0]));
        Assert.Equal("Selected", AutomationProperties.GetItemStatus(chips[1]));
        Assert.Equal("Alpha", UIElementAutomationPeer.CreatePeerForElement(chips[0]).GetName());
    });

    [Fact]
    public void InputBoxHonoursMaxLengthAndSubmitsOnTheRightKeys() => OnSta(() =>
    {
        var single = new InputBox("Language", maxLength: 10);
        single.Text = "A very long language name";
        Assert.Equal(10, single.Text.Length);
        int submits = 0;
        single.Submitted += () => submits++;
        Assert.True(single.HandleKey(Key.Enter, ModifierKeys.None));
        Assert.Equal(1, submits);
        Assert.True(single.HandleKey(Key.Escape, ModifierKeys.None));
        Assert.Equal(1, submits);
        Assert.False(single.HandleKey(Key.A, ModifierKeys.None));

        var multi = new InputBox("Tell the AI what to do", maxLength: 4000, multiline: true);
        multi.Submitted += () => submits++;
        Assert.False(multi.HandleKey(Key.Enter, ModifierKeys.None)); // a new line
        Assert.Equal(1, submits);
        Assert.True(multi.HandleKey(Key.Enter, ModifierKeys.Control));
        Assert.Equal(2, submits);
    });

    [Fact]
    public void InputBoxIsNamedForScreenReadersAndHasAPasteButton() => OnSta(() =>
    {
        var box = new InputBox("Reader persona", maxLength: 300);
        Assert.Equal("Reader persona", AutomationProperties.GetName(box.Box));
        Assert.Contains(Descendants(box).OfType<ClickBorder>(), b => (b.ToolTip as string ?? "").StartsWith("Paste", StringComparison.Ordinal));
    });

    // ---- tabs ----

    [Fact]
    public void TabSetBuildsATabOnlyWhenItIsFirstShown() => OnSta(() =>
    {
        var host = new FakeHost();
        var tabs = PanelWindow.CreateTabs(host);
        var set = new TabSet(tabs, "assistant");
        Assert.Equal("assistant", set.Active.Id);
        Assert.All(tabs, t => Assert.False(t.IsBuilt));
        _ = set.Active.View;
        Assert.True(set.Active.IsBuilt);
        Assert.False(tabs.Single(t => t.Id == "review").IsBuilt);

        Assert.False(set.Select("assistant"));
        Assert.False(set.Select("not-a-tab"));
        Assert.True(set.Select("review"));
        Assert.Equal("review", set.Active.Id);

        Assert.Equal("review", new TabSet(tabs, "gone-in-a-later-version").Active.Id);
        Assert.Equal("review", new TabSet(tabs, null).Active.Id);
    });

    [Fact]
    public void TabsFollowTheWebEditorsOrderAndAreUnique() => OnSta(() =>
    {
        var ids = PanelWindow.CreateTabs(new FakeHost()).Select(t => t.Id).ToList();
        Assert.Equal(new[] { "review", "assistant", "paraphrase", "humanize", "reader", "grader", "detector", "translate", "synonyms", "performance" }, ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    });

    [Fact]
    public void ReviewReusesCardsWhenTheListChanges() => OnSta(() =>
    {
        var review = new ReviewTab(new FakeHost());
        _ = review.View;
        var a = Placed(0, "teh", "the");
        var b = Placed(10, "recieve", "receive");
        var c = Placed(30, "alot", "a lot");

        review.OnState(Snap(a, b));
        var first = review.Cards.ToList();
        Assert.Equal(2, first.Count);

        review.OnState(Snap(b, c));
        var second = review.Cards.ToList();
        Assert.Equal(2, second.Count);
        Assert.Same(first[1], second[0]); // b kept its card
        Assert.DoesNotContain(first[0], second); // a's card is gone

        review.OnState(Snap(b, c));
        Assert.Equal(second, review.Cards.ToList());
    });

    [Fact]
    public void ReviewShowsAtMostSixtyCardsAndFixAllForTwoOrMoreFixes() => OnSta(() =>
    {
        var host = new FakeHost();
        var review = new ReviewTab(host);
        _ = review.View;
        review.OnState(Snap(Enumerable.Range(0, 75).Select(i => Placed(i * 10, "teh", "the")).ToArray()));
        Assert.Equal(60, review.Cards.Count());
        var fixAll = Descendants(review.View).OfType<ClickBorder>().Single(x => ((TextBlock)x.Child).Text.StartsWith("Fix all", StringComparison.Ordinal));
        Assert.Contains("(75)", ((TextBlock)fixAll.Child).Text);
    });

    [Fact]
    public void AnInsertResultKeepsTheOriginalAndAddsAfterIt()
    {
        var sel = new TextScope(6, 11, "Hello there", "Note: Hello there. Bye", false);
        Assert.Equal("Shorter", RewriteResult.Compose(sel, "  Shorter\n", "shorten"));
        Assert.Equal("Hello there\n\nA summary.", RewriteResult.Compose(sel, " A summary. ", "summarize"));
        Assert.Equal("Hello there and more.", RewriteResult.Compose(sel, "and more.", "continue"));
        var endsInSpace = sel with { Text = "Hello there " };
        Assert.Equal("Hello there and more.", RewriteResult.Compose(endsInSpace, "and more.", "continue"));
        var endsInNewline = sel with { Text = "Hello there\n" };
        Assert.Equal("Hello there\n\nIdeas", RewriteResult.Compose(endsInNewline, "Ideas", "brainstorm"));
        Assert.True(RewriteResult.Inserts("reply"));
        Assert.False(RewriteResult.Inserts("custom"));
    }

    [Fact]
    public void ARewriteSupersededByAnotherToolSaysSoAndIgnoresLateText() => OnSta(() =>
    {
        var host = new FakeHost();
        var r = new RewriteResult(host);
        r.Start(new WriteRequest("improve", ""));
        Assert.Same(r, host.LastSink);
        r.OnUpdate(new AiUpdate(4, "Better", false, null, Scope()));
        r.Interrupted();
        Assert.Contains("another tool", r.Status);
        r.OnUpdate(new AiUpdate(4, "Better text", true, null, Scope()));
        Assert.Contains("another tool", r.Status);
        Assert.False(r.CanApply);
    });

    [Fact]
    public void ARewriteIgnoresUpdatesFromAnOlderRequestAndAppliesTheFinishedOne() => OnSta(() =>
    {
        var host = new FakeHost();
        var r = new RewriteResult(host);
        r.Start(new WriteRequest("shorten", ""));
        r.OnUpdate(new AiUpdate(7, "", false, null, Scope()));
        r.OnUpdate(new AiUpdate(6, "stale", true, null, Scope()));
        Assert.False(r.CanApply);
        r.OnUpdate(new AiUpdate(7, "Short.", true, null, Scope()));
        Assert.True(r.CanApply);
        r.Apply();
        Assert.Equal("Short.", host.Applied);
        Assert.False(r.CanApply);
    });

    // ---- the whole panel ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryTabBuildsAndFitsThePanelInLightAndDark(bool dark) => OnSta(() =>
    {
        Theme.Apply(dark);
        var host = new FakeHost();
        var panel = new PanelWindow(host, "review");
        var snap = Snap(Placed(0, "teh", "the"), Placed(10, "recieve", "receive"), Placed(30, "alot", "a lot"));
        string? dir = Environment.GetEnvironmentVariable("NW_RENDER_DIR");
        foreach (var tab in panel.Tabs.All)
        {
            panel.ShowTab(tab.Id, snap);
            Check(tab.Id, "");
            // The bigger states: a custom voice box, and the full language list unfolded.
            if (tab is RewriteTab { Modes: { } modes })
            {
                modes.Select("custom");
                Check(tab.Id, "-custom");
            }
            if (tab is RewriteTab { To: { } to })
            {
                to.Filter("");
                Check(tab.Id, "-all-languages");
            }
            if (ShowSampleResults(tab)) Check(tab.Id, "-results");
        }
        Assert.Equal(panel.Tabs.All[^1].Id, host.LastTab);
        Theme.Apply(false);

        void Check(string id, string state)
        {
            var tab = panel.Tabs.Active;
            var bmp = panel.RenderForTest(snap);
            Assert.True(bmp.PixelWidth > 400 && bmp.PixelHeight > 400);
            Assert.Equal(PanelWindow.ContentWidth, panel.ContentHost.ActualWidth, 0);
            Assert.Same(tab.Hosted, panel.ContentHost.Content);
            foreach (var fe in Descendants(panel.ContentHost).OfType<FrameworkElement>().Where(e => e.ActualWidth > 0))
            {
                var r = fe.TransformToAncestor(panel.ContentHost).TransformBounds(new Rect(fe.RenderSize));
                Assert.True(r.Right <= PanelWindow.ContentWidth + 1, $"{id}{state}: {fe.GetType().Name} \"{(fe as TextBlock)?.Text}\" sticks out to {r.Right:0}");
            }
            if (dir != null) Save(bmp, Path.Combine(dir, $"panel-{id}{state}-{(dark ? "dark" : "light")}.png"));
        }
    });

    // ---- helpers ----

    /// <summary>Fill a tool tab with realistic results (long strings included) so the render check covers them.</summary>
    static bool ShowSampleResults(PanelTab tab)
    {
        const string essay = "Social media has changed how young people communicate. In today's fast-paced digital world, it is crucial to leverage connection. Schools should teach screen time.";
        var scope = new TextScope(0, essay.Length, essay, essay, true);
        switch (tab)
        {
            case SynonymsTab syn:
                syn.Show(new List<SynonymSense>
                {
                    new() { Meaning = "altered or made different", Synonyms = { "transformed", "reshaped", "altered", "revolutionised", "modified" } },
                    new() { Meaning = "swapped one thing for another", Synonyms = { "replaced", "switched", "exchanged" } },
                }, new TextScope(17, 7, "changed", essay, false));
                return true;
            case ReaderTab reader:
                reader.Show(new ReaderReactions
                {
                    Overall = "A clear, if brief, argument. A sceptical parent would want evidence for the claims about anxiety and some sense of what 'teach screen time' means in practice.",
                    Takeaways = { "Social media changed communication", "Schools should act" },
                    Questions = { "What evidence links social media to anxiety?", "Who teaches this, and in which lessons?" },
                    Confusions = { new() { Quote = "it is crucial to leverage connection", Issue = "Vague: leverage connection how?", Suggestion = "Say what students should actually do." } },
                    Suggestions = { "Add one statistic with a source.", "End with a concrete recommendation." },
                }, scope);
                return true;
            case GraderTab grader:
                grader.Show(new GraderResult
                {
                    Grade = "B+",
                    Score = 78,
                    Summary = "A focused argument with a clear position; it needs evidence and a stronger conclusion to reach the top band.",
                    Criteria = { new() { Name = "Argument and position", Score = 82, Feedback = "Clear thesis, consistently held." }, new() { Name = "Use of evidence", Score = 48, Feedback = "One unsourced statistic; cite it and add a counterpoint." } },
                    Strengths = { "Clear structure" },
                    Improvements = { "Cite the 70% statistic", "Answer the strongest objection" },
                }, scope);
                return true;
            case DetectorTab detector:
                detector.Show(new AiDetectResult
                {
                    Score = 63,
                    Verdict = "Mixed: parts read as AI-assisted",
                    Signals = { new() { Label = "Sentence length variation", Value = "Low (very even rhythm)" }, new() { Label = "Stock phrases", Value = "2 found" } },
                    Passages =
                    {
                        new() { Text = "In today's fast-paced digital world, it is crucial to leverage connection.", Score = 88, Reasons = { "Stock opener", "Abstract verb 'leverage'" } },
                        new() { Text = "Schools should teach screen time.", Score = 22, Reasons = { } },
                    },
                }, scope);
                return true;
            default:
                return false;
        }
    }

    static TextScope Scope() => new(0, 5, "Hello", "Hello", true);

    static PlacedIssue Placed(int start, string original, string fix) =>
        new($"id{start}", start, new Issue { Offset = start, Length = original.Length, Original = original, Replacements = { fix }, Category = "correctness", Type = "spelling", Title = "Spelling", Explanation = $"Did you mean \"{fix}\"?" }, $"sig{start}");

    static EngineSnapshot Snap(params PlacedIssue[] issues) =>
        new(new TargetInfo(1, "notepad", "Notepad", IntPtr.Zero, true), issues, CheckState.Idle, null, null, 0, 40);

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        // A control on its own (not in a window) has no template or layout yet: lay it out once.
        if (root is FrameworkElement fe && fe.Parent == null && VisualTreeHelper.GetParent(fe) == null && fe.ActualWidth == 0)
        {
            fe.Measure(new Size(1000, 2000));
            fe.Arrange(new Rect(fe.DesiredSize));
        }
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    static void Save(BitmapSource bmp, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }

    static void OnSta(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        failure?.Throw();
    }

    /// <summary>Any request fails as if the server were down, so a tab that calls out by mistake shows an error instead of hanging.</summary>
    sealed class NoServer : System.Net.Http.HttpMessageHandler
    {
        protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken ct) =>
            throw new System.Net.Http.HttpRequestException("No server in tests.");
    }

    internal sealed class FakeHost : IPanelActions
    {
        public System.Collections.Generic.List<string> Recent = new() { "French", "Spanish", "German" };
        public IReadOnlyList<string> RecentLanguages => Recent;
        public void UsedLanguage(string language) => Recent = Languages.PushRecent(Recent, language);
        public RewriteResult? LastSink;
        public string? Applied;
        public string? LastTab;

        public string Hotkey => "Ctrl+Alt+N";
        public ApiClient Api { get; } = new("http://127.0.0.1:1", new NoServer());
        public (TextScope? Scope, string? Problem) NextCapture = (null, "Click into a text box first.");
        public TextScope? Humanized;
        public string? Opened;
        public int Rechecks;
        public string EditorUrl => "http://127.0.0.1:4317";
        public System.Threading.Tasks.Task<(TextScope? Scope, string? Problem)> CaptureAsync(int maxChars, string verb) => System.Threading.Tasks.Task.FromResult(NextCapture);
        public void OpenUrl(string url) => Opened = url;
        public void Recheck() => Rechecks++;
        public void HumanizePassage(TextScope scope) => Humanized = scope;
        public void Accept(string id, int index) { }
        public void Dismiss(string id) { }
        public void AddWord(string id) { }
        public void AcceptAll(IReadOnlyList<string> ids) { }
        public void SetHighlight(string? id) { }
        public void Rewrite(WriteRequest request, RewriteResult sink, TextScope? on = null) => LastSink = sink;
        public void CancelRewrite(RewriteResult sink) { }
        public void ApplyRewrite(TextScope scope, string text, string? doneNotice = null) => Applied = text;
        public void DetectTone(Action<ToneResult?, string?> done) { }
        public void Notice(string text) { }
        public void OpenEditor() { }
        public void OpenWritingSettings() { }
        public void PauseHour() { }
        public void TurnOffInCurrentApp() { }
        public void ClosePanel() { }
        public void TabChanged(string id) => LastTab = id;
    }
}
