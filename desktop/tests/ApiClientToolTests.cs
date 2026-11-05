using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NimbleWrite.Desktop.Core;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

/// <summary>
/// The companion's calls to every tool endpoint: method, path and body must match what the server's Zod schemas accept
/// (the web client's src/api.ts is the reference), and long text is clipped to the same caps so it degrades instead of
/// failing with a 400.
/// </summary>
public class ApiClientToolTests
{
    sealed class FakeServer : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Path, string Body)> Requests = new();
        public string Reply = "{}";
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string ContentType = "application/json";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            return new HttpResponseMessage(Status) { Content = new StringContent(Reply, Encoding.UTF8, ContentType) };
        }
    }

    static (ApiClient Api, FakeServer Server) Make(string reply = "{}")
    {
        var server = new FakeServer { Reply = reply };
        return (new ApiClient("http://127.0.0.1:4317", server), server);
    }

    static JsonElement Body(FakeServer s, int i = 0) => JsonDocument.Parse(s.Requests[i].Body).RootElement;

    static string[] Keys(JsonElement e) => e.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task WriteSendsOnlyTheFieldsATaskUses()
    {
        var (api, s) = Make("event: delta\ndata: {\"t\":\"Hola\"}\n\nevent: done\ndata: {\"ms\":5}\n\n");
        s.ContentType = "text/event-stream";
        string got = await api.WriteAsync(new WriteRequest("translate", "Hello", TargetLanguage: "Spanish"), _ => { }, None);
        Assert.Equal("Hola", got);
        Assert.Equal((HttpMethod.Post, "/api/ai/write"), (s.Requests[0].Method, s.Requests[0].Path));
        var b = Body(s);
        Assert.Equal(new[] { "targetLanguage", "task", "text" }, Keys(b));
        Assert.Equal("Spanish", b.GetProperty("targetLanguage").GetString());

        await api.WriteAsync(new WriteRequest("paraphrase", "Hi there", Mode: "custom", Instruction: "like a pirate"), _ => { }, None);
        b = Body(s, 1);
        Assert.Equal(new[] { "instruction", "mode", "task", "text" }, Keys(b));
        Assert.Equal("custom", b.GetProperty("mode").GetString());

        await api.WriteAsync(new WriteRequest("translate", "Hallo", TargetLanguage: "English (British)", SourceLanguage: "German"), _ => { }, None);
        b = Body(s, 2);
        Assert.Equal(new[] { "sourceLanguage", "targetLanguage", "task", "text" }, Keys(b));
        Assert.Equal("German", b.GetProperty("sourceLanguage").GetString());
    }

    [Fact]
    public async Task TheOldWriteOverloadStillSendsTaskTextAndContext()
    {
        var (api, s) = Make("event: done\ndata: {}\n\n");
        await api.WriteAsync("improve", "abc", "context here", _ => { }, None);
        Assert.Equal(new[] { "context", "task", "text" }, Keys(Body(s)));
    }

    [Fact]
    public async Task AgentsPostToTheirRoutesWithClippedText()
    {
        string huge = new('a', 70_000);
        var (api, s) = Make("{\"overall\":\"ok\",\"takeaways\":[\"t\"],\"questions\":[],\"confusions\":[{\"quote\":\"q\",\"issue\":\"i\",\"suggestion\":\"s\"}],\"suggestions\":[]}");
        var r = await api.ReaderAsync(huge, "A busy manager", None);
        Assert.Equal("ok", r.Overall);
        Assert.Equal("q", r.Confusions.Single().Quote);
        Assert.Equal("/api/ai/agents/reader", s.Requests[0].Path);
        Assert.Equal(40_000, Body(s).GetProperty("text").GetString()!.Length);
        Assert.Equal("A busy manager", Body(s).GetProperty("reader").GetString());

        s.Reply = "{\"grade\":\"B+\",\"score\":78,\"summary\":\"s\",\"criteria\":[{\"name\":\"Argument\",\"score\":7.5,\"feedback\":\"f\"}],\"strengths\":[],\"improvements\":[]}";
        var g = await api.GraderAsync(huge, new string('r', 25_000), "undergraduate", None);
        Assert.Equal(7.5, g.Criteria.Single().Score);
        Assert.Equal("/api/ai/agents/grader", s.Requests[1].Path);
        Assert.Equal(60_000, Body(s, 1).GetProperty("text").GetString()!.Length);
        Assert.Equal(20_000, Body(s, 1).GetProperty("rubric").GetString()!.Length);

        s.Reply = "{\"claims\":[{\"claim\":\"c\",\"reason\":\"r\",\"query\":\"q\",\"strength\":\"needs_citation\"}]}";
        Assert.Equal("needs_citation", (await api.ClaimsAsync("text", None)).Single().Strength);
        Assert.Equal("/api/ai/agents/claims", s.Requests[2].Path);

        s.Reply = "{\"score\":63,\"verdict\":\"Mixed\",\"passages\":[{\"text\":\"p\",\"score\":80,\"reasons\":[\"r\"]}],\"signals\":[{\"label\":\"Rhythm\",\"value\":\"even\"}]}";
        var d = await api.DetectAsync("text", None);
        Assert.Equal(63, d.Score);
        Assert.Equal("Rhythm", d.Signals.Single().Label);
        Assert.Equal("/api/ai/agents/detect", s.Requests[3].Path);
    }

    [Fact]
    public async Task SynonymsToneAndImagePromptSendTheirFields()
    {
        var (api, s) = Make("{\"senses\":[{\"meaning\":\"fast\",\"synonyms\":[\"quick\",\"rapid\"]}]}");
        var senses = await api.SynonymsAsync("speedy", "A speedy reply.", "en-GB", None);
        Assert.Equal(new[] { "quick", "rapid" }, senses.Single().Synonyms);
        Assert.Equal("/api/ai/synonyms", s.Requests[0].Path);
        Assert.Equal(new[] { "dialect", "sentence", "word" }, Keys(Body(s)));

        s.Reply = "{\"prompt\":\"A red fox\",\"model\":\"m\",\"ms\":12}";
        var p = await api.ImagePromptAsync("fox", "flux3-klein", enhance: false, None);
        Assert.Equal("A red fox", p.Prompt);
        var b = Body(s, 1);
        Assert.Equal("/api/ai/image-prompt", s.Requests[1].Path);
        Assert.False(b.GetProperty("enhance").GetBoolean());
        Assert.Equal("flux3-klein", b.GetProperty("target").GetString());
    }

    [Fact]
    public async Task PlagiarismAndCitationLookup()
    {
        var (api, s) = Make("{\"overall\":12.5,\"sources\":[{\"title\":\"Doc\",\"kind\":\"doc\",\"similarity\":12.5,\"passages\":[{\"offset\":0,\"length\":4,\"text\":\"abcd\"}]}],\"webSearch\":\"skipped\",\"checkedSources\":3}");
        var r = await api.PlagiarismAsync(new PlagiarismRequest { Text = "abcd", Sources = { new PlagiarismSourceInput { Text = "pasted" } } }, None);
        Assert.Equal(12.5, r.Overall);
        Assert.Equal("doc", r.Sources.Single().Kind);
        var b = Body(s);
        Assert.Equal("/api/plagiarism", s.Requests[0].Path);
        Assert.True(b.GetProperty("includeDocs").GetBoolean());
        Assert.False(b.GetProperty("web").GetBoolean());
        Assert.Equal(new[] { "text" }, Keys(b.GetProperty("sources")[0]));

        s.Reply = "{\"meta\":{\"type\":\"article\",\"title\":\"T\",\"authors\":[{\"family\":\"Smith\"}],\"doi\":\"10.1/x\"},\"formatted\":[{\"style\":\"apa\",\"text\":\"Smith. T.\",\"html\":\"<i>T</i>\"}]}";
        var c = await api.CitationLookupAsync("  10.1/x  ", None);
        Assert.Equal("Smith", c.Meta.Authors.Single().Family);
        Assert.Equal("apa", c.Formatted.Single().Style);
        Assert.Equal("/api/citation/lookup", s.Requests[1].Path);
        Assert.Equal("10.1/x", Body(s, 1).GetProperty("input").GetString());
    }

    [Fact]
    public async Task TuneChangesGoBackExactlyAsTheyCame()
    {
        var (api, s) = Make("{\"reply\":\"Sure.\",\"changes\":[{\"kind\":\"mute\",\"type\":\"passive_voice\",\"title\":\"Passive voice\"},{\"kind\":\"dictionary\",\"word\":\"Nemotron\"}]}");
        var t = await api.TuneAsync("stop flagging passive voice", None);
        Assert.Equal("/api/ai/tune", s.Requests[0].Path);
        Assert.Equal(2, t.Changes.Count);

        s.Reply = "{\"settings\":{\"dialect\":\"en-GB\",\"mutedTypes\":[\"passive_voice\"]},\"applied\":2,\"skipped\":0}";
        var applied = await api.ApplyTuneAsync(t.Changes, None);
        Assert.Equal(2, applied.Applied);
        Assert.Contains("passive_voice", applied.Settings.MutedTypes);
        Assert.Equal("/api/settings/tune", s.Requests[1].Path);
        var sent = Body(s, 1).GetProperty("changes");
        Assert.Equal(new[] { "kind", "title", "type" }, Keys(sent[0]));
        Assert.Equal(new[] { "kind", "word" }, Keys(sent[1]));
    }

    [Fact]
    public async Task SettingsPatchSendsOnlyWhatChanged()
    {
        var (api, s) = Make("{\"dialect\":\"en-US\",\"categories\":{\"correctness\":true,\"clarity\":true,\"engagement\":false,\"delivery\":true}}");
        var after = await api.PatchSettingsAsync(new SettingsPatch { Dialect = "en-US", Categories = new() { ["engagement"] = false } }, None);
        Assert.Equal(HttpMethod.Patch, s.Requests[0].Method);
        Assert.Equal("/api/settings", s.Requests[0].Path);
        Assert.Equal(new[] { "categories", "dialect" }, Keys(Body(s)));
        Assert.Equal(new[] { "engagement" }, Keys(Body(s).GetProperty("categories")));
        Assert.False(after.Categories["engagement"]);

        s.Reply = "{\"dialect\":\"en-GB\",\"defaultGoals\":{\"audience\":\"expert\",\"formality\":\"formal\",\"domain\":\"business\",\"intent\":[\"convince\"]},\"localRules\":false,\"theme\":\"dark\",\"models\":{\"check\":\"c\",\"write\":\"w\",\"translate\":\"t\"},\"dictionary\":[\"x\"]}";
        var got = await api.GetWritingSettingsAsync(None);
        Assert.Equal((HttpMethod.Get, "/api/settings"), (s.Requests[1].Method, s.Requests[1].Path));
        Assert.Equal("expert", got.DefaultGoals.Audience);
        Assert.False(got.LocalRules);
        Assert.Equal("w", got.Models.Write);
    }

    [Fact]
    public async Task StatsInsightsAndDocuments()
    {
        var (api, s) = Make("{\"stats\":{\"words\":120,\"readability\":61,\"avgWordLength\":4.6},\"readability\":{\"label\":\"Standard\",\"audience\":\"13–15 year olds\"},\"score\":88}");
        var st = await api.TextStatsAsync("text", new Dictionary<string, int> { ["correctness"] = 1, ["clarity"] = 0, ["engagement"] = 2, ["delivery"] = 0 }, None);
        Assert.Equal(120, st.Stats.Words);
        Assert.Equal(88, st.Score);
        Assert.Equal("/api/text/stats", s.Requests[0].Path);
        Assert.Equal(2, Body(s).GetProperty("counts").GetProperty("engagement").GetInt32());

        s.Reply = "{\"days\":[{\"date\":\"2026-11-05\",\"stats\":{\"accepted\":{\"correctness\":3},\"checks\":4,\"wordsChecked\":900}}],\"totals\":{\"docs\":2,\"words\":1500,\"accepted\":3}}";
        var ins = await api.InsightsAsync(None);
        Assert.Equal(900, ins.Days.Single().Stats.WordsChecked);
        Assert.Equal((HttpMethod.Get, "/api/insights"), (s.Requests[1].Method, s.Requests[1].Path));

        s.Reply = "{\"docs\":[{\"id\":\"abc-123\",\"title\":\"Notes\",\"snippet\":\"s\",\"wordCount\":5,\"score\":null,\"createdAt\":1,\"updatedAt\":2,\"deletedAt\":null}]}";
        Assert.Equal("Notes", (await api.ListDocsAsync(None)).Single().Title);
        Assert.Equal("/api/docs", s.Requests[2].Path);

        s.Reply = "{\"id\":\"abc-123\",\"title\":\"Notes\",\"text\":\"Hello there.\",\"html\":\"<p>Hello there.</p>\",\"wordCount\":2,\"updatedAt\":2}";
        Assert.Equal("Hello there.", (await api.GetDocAsync("abc-123", None)).Text);
        Assert.Equal("/api/docs/abc-123", s.Requests[3].Path);
    }

    [Fact]
    public async Task ServerErrorsBecomeApiExceptionsWithTheServersMessage()
    {
        var (api, s) = Make("{\"error\":\"Keep at least one suggestion category on.\"}");
        s.Status = HttpStatusCode.BadRequest;
        var e = await Assert.ThrowsAsync<ApiException>(() => api.PatchSettingsAsync(new SettingsPatch { LocalRules = true }, None));
        Assert.Equal(400, e.Status);
        Assert.Equal("Keep at least one suggestion category on.", e.Message);
    }

    [Fact]
    public void ClipNeverSplitsASurrogatePair()
    {
        string s = new string('a', 9) + "😀" + "bbb";
        Assert.Equal(new string('a', 9), ApiClient.Clip(s, 10));
        Assert.Equal(s[..11], ApiClient.Clip(s, 11));
        Assert.Equal("short", ApiClient.Clip("short", 10));
    }

    [Fact]
    public async Task NoRequestEverCarriesAnNvidiaKey()
    {
        var (api, s) = Make("{}");
        await api.ReaderAsync("text", "", None);
        await api.PatchSettingsAsync(new SettingsPatch { Theme = "dark" }, None);
        Assert.All(s.Requests, r => Assert.DoesNotContain("nvapi-", r.Body, StringComparison.OrdinalIgnoreCase));
    }
}
