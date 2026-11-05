using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NimbleWrite.Desktop.Core;

public sealed class ApiException : Exception
{
    public ApiException(string message, int status) : base(message) => Status = status;

    public int Status { get; }
}

public readonly record struct SseEvent(string Event, string Data);

/// <summary>Minimal server-sent-events parser: feed it lines, get an event back after each blank line.</summary>
public sealed class SseParser
{
    string evt = "message";
    readonly StringBuilder data = new();

    public SseEvent? Feed(string line)
    {
        if (line.Length == 0)
        {
            if (data.Length == 0)
            {
                evt = "message";
                return null;
            }
            var e = new SseEvent(evt, data.ToString());
            evt = "message";
            data.Clear();
            return e;
        }
        if (line[0] == ':') return null;
        int colon = line.IndexOf(':');
        string field = colon < 0 ? line : line[..colon];
        string value = colon < 0 ? "" : line[(colon + 1)..];
        if (value.StartsWith(' ')) value = value[1..];
        if (field == "event") evt = value;
        else if (field == "data")
        {
            if (data.Length > 0) data.Append('\n');
            data.Append(value);
        }
        return null;
    }
}

/// <summary>Talks to the local NimbleWrite server. The NVIDIA key lives there and never reaches this app.</summary>
public sealed class ApiClient : IDisposable
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    readonly HttpClient http;

    public ApiClient(string baseUrl) : this(baseUrl, null)
    {
    }

    /// <summary>Tests pass a fake <paramref name="handler"/> so no request leaves the process.</summary>
    internal ApiClient(string baseUrl, HttpMessageHandler? handler)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.BaseAddress = new Uri(BaseUrl + "/");
        http.Timeout = TimeSpan.FromSeconds(150);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NimbleWrite-Desktop/1.0");
    }

    public string BaseUrl { get; }

    /// <summary>
    /// The editor page for a document. The editor routes on the URL's hash, so this is /#/doc/&lt;id&gt;: the server answers
    /// a plain /doc/&lt;id&gt; too, but with the documents list.
    /// </summary>
    public string DocUrl(string id) => $"{BaseUrl}/#/doc/{Uri.EscapeDataString(id)}";

    public async Task<bool> HealthAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var r = await http.GetAsync("api/health", cts.Token).ConfigureAwait(false);
            return r.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>The server's status (is a key set up, is it the installed copy), or null when it can't be reached.</summary>
    public async Task<HealthInfo?> HealthInfoAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var r = await http.GetAsync("api/health", cts.Token).ConfigureAwait(false);
            if (!r.IsSuccessStatusCode) return null;
            return await r.Content.ReadFromJsonAsync<HealthInfo>(Json, cts.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Have the local server try a key with NVIDIA before it's saved. The server uses it for that one request.</summary>
    public async Task<KeyTestResult> TestKeyAsync(string key, CancellationToken ct)
    {
        using var r = await http.PostAsJsonAsync("api/setup/test-key", new { key }, Json, ct).ConfigureAwait(false);
        await EnsureOk(r, ct).ConfigureAwait(false);
        return await r.Content.ReadFromJsonAsync<KeyTestResult>(Json, ct).ConfigureAwait(false) ?? new KeyTestResult { Reason = "error" };
    }

    /// <summary>Suggestions for each paragraph (ids are the list indexes). <paramref name="ai"/>=false runs only the instant local rules.</summary>
    public async Task<AssistResponse> AssistAsync(IReadOnlyList<string> paragraphs, bool ai, CancellationToken ct)
    {
        var body = new { paragraphs = paragraphs.Select((t, i) => new { id = i.ToString(), text = t }).ToArray(), ai };
        using var r = await http.PostAsJsonAsync("api/ai/assist", body, Json, ct).ConfigureAwait(false);
        await EnsureOk(r, ct).ConfigureAwait(false);
        return await r.Content.ReadFromJsonAsync<AssistResponse>(Json, ct).ConfigureAwait(false) ?? new AssistResponse();
    }

    public async Task<ToneResult> ToneAsync(string text, CancellationToken ct)
    {
        using var r = await http.PostAsJsonAsync("api/ai/tone", new { text }, Json, ct).ConfigureAwait(false);
        await EnsureOk(r, ct).ConfigureAwait(false);
        return await r.Content.ReadFromJsonAsync<ToneResult>(Json, ct).ConfigureAwait(false) ?? new ToneResult();
    }

    public async Task AddToDictionaryAsync(string word, CancellationToken ct)
    {
        using var r = await http.PostAsJsonAsync("api/settings/dictionary", new { word }, Json, ct).ConfigureAwait(false);
        await EnsureOk(r, ct).ConfigureAwait(false);
    }

    /// <summary>Create a document in the NimbleWrite editor and return its id.</summary>
    public async Task<string> CreateDocAsync(string title, string text, CancellationToken ct)
    {
        using var r = await http.PostAsJsonAsync("api/docs", new { title, text }, Json, ct).ConfigureAwait(false);
        await EnsureOk(r, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return doc.RootElement.GetProperty("id").GetString() ?? throw new ApiException("The server didn't return a document id.", 500);
    }

    /// <summary>Stream an AI rewrite. <paramref name="onText"/> receives the text so far after each chunk.</summary>
    public Task<string> WriteAsync(string task, string text, string? context, Action<string> onText, CancellationToken ct) =>
        WriteAsync(new WriteRequest(task, text, context), onText, ct);

    /// <summary>Stream any write task (rewrite, paraphrase, humanize, translate, custom instruction…). Fields left null aren't sent.</summary>
    public async Task<string> WriteAsync(WriteRequest request, Action<string> onText, CancellationToken ct)
    {
        var body = request with
        {
            Text = Clip(request.Text, 60_000),
            Context = request.Context == null ? null : Clip(request.Context, 60_000),
            Instruction = request.Instruction == null ? null : Clip(request.Instruction, 4000),
            TargetLanguage = request.TargetLanguage == null ? null : Clip(request.TargetLanguage, 60),
            SourceLanguage = request.SourceLanguage == null ? null : Clip(request.SourceLanguage, 60),
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/ai/write") { Content = JsonContent.Create(body, options: Json) };
        using var r = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureOk(r, ct).ConfigureAwait(false);
        await using var stream = await r.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var parser = new SseParser();
        var sb = new StringBuilder();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (parser.Feed(line) is not { } ev) continue;
            using var doc = JsonDocument.Parse(ev.Data);
            switch (ev.Event)
            {
                case "delta":
                    if (doc.RootElement.TryGetProperty("t", out var t)) sb.Append(t.GetString());
                    onText(sb.ToString());
                    break;
                case "error":
                    throw new ApiException(doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "The AI request failed." : "The AI request failed.", 502);
                case "done":
                    return sb.ToString();
            }
        }
        return sb.ToString();
    }

    // ---- the editor's tools (limits mirror the server's Zod schemas, like src/api.ts) ----

    public Task<ReaderReactions> ReaderAsync(string text, string reader, CancellationToken ct) =>
        SendAsync<ReaderReactions>(HttpMethod.Post, "api/ai/agents/reader", new { text = Clip(text, 40_000), reader = Clip(reader, 300) }, ct);

    public Task<GraderResult> GraderAsync(string text, string rubric, string level, CancellationToken ct) =>
        SendAsync<GraderResult>(HttpMethod.Post, "api/ai/agents/grader", new { text = Clip(text, 60_000), rubric = Clip(rubric, 20_000), level = Clip(level, 80) }, ct);

    public async Task<List<ClaimToCite>> ClaimsAsync(string text, CancellationToken ct) =>
        (await SendAsync<ClaimsResponse>(HttpMethod.Post, "api/ai/agents/claims", new { text = Clip(text, 40_000) }, ct).ConfigureAwait(false)).Claims;

    public Task<AiDetectResult> DetectAsync(string text, CancellationToken ct) =>
        SendAsync<AiDetectResult>(HttpMethod.Post, "api/ai/agents/detect", new { text = Clip(text, 60_000) }, ct);

    public async Task<List<SynonymSense>> SynonymsAsync(string word, string sentence, string dialect, CancellationToken ct) =>
        (await SendAsync<SynonymsResponse>(HttpMethod.Post, "api/ai/synonyms", new { word = Clip(word, 60), sentence = Clip(sentence, 1200), dialect }, ct).ConfigureAwait(false)).Senses;

    /// <summary><paramref name="enhance"/>=false restructures only what was written; true lets the model add detail.</summary>
    public Task<ImagePromptResult> ImagePromptAsync(string idea, string target, bool enhance, CancellationToken ct) =>
        SendAsync<ImagePromptResult>(HttpMethod.Post, "api/ai/image-prompt", new { idea = Clip(idea, 4000), target, enhance }, ct);

    public Task<PlagiarismResult> PlagiarismAsync(PlagiarismRequest request, CancellationToken ct)
    {
        var body = new PlagiarismRequest
        {
            Text = Clip(request.Text, 200_000),
            DocId = request.DocId,
            IncludeDocs = request.IncludeDocs,
            Web = request.Web,
            Sources = request.Sources.Take(10).Select(s => new PlagiarismSourceInput
            {
                Title = s.Title == null ? null : Clip(s.Title, 300),
                Text = s.Text == null ? null : Clip(s.Text, 400_000),
                Url = s.Url == null ? null : Clip(s.Url, 2000),
            }).ToList(),
        };
        return SendAsync<PlagiarismResult>(HttpMethod.Post, "api/plagiarism", body, ct);
    }

    /// <summary>Read a URL or DOI and format it in every citation style.</summary>
    public Task<CitationLookupResult> CitationLookupAsync(string input, CancellationToken ct) =>
        SendAsync<CitationLookupResult>(HttpMethod.Post, "api/citation/lookup", new { input = Clip(input.Trim(), 2000) }, ct);

    /// <summary>The Tune chat: proposes settings changes from a plain-language request. Nothing changes until <see cref="ApplyTuneAsync"/>.</summary>
    public Task<TuneResult> TuneAsync(string message, CancellationToken ct) =>
        SendAsync<TuneResult>(HttpMethod.Post, "api/ai/tune", new { message = Clip(message.Trim(), 800) }, ct);

    public Task<TuneApplyResult> ApplyTuneAsync(IEnumerable<TuneChange> changes, CancellationToken ct) =>
        SendAsync<TuneApplyResult>(HttpMethod.Post, "api/settings/tune", new { changes = changes.Take(20).ToList() }, ct);

    public Task<WritingSettings> GetWritingSettingsAsync(CancellationToken ct) => SendAsync<WritingSettings>(HttpMethod.Get, "api/settings", null, ct);

    /// <summary>Change some writing settings; the server merges them, so changes made in the editor are kept.</summary>
    public Task<WritingSettings> PatchSettingsAsync(SettingsPatch patch, CancellationToken ct) => SendAsync<WritingSettings>(HttpMethod.Patch, "api/settings", patch, ct);

    /// <summary>Word counts, readability and (given the open suggestion counts per category) the overall score.</summary>
    public Task<TextStatsResponse> TextStatsAsync(string text, IReadOnlyDictionary<string, int>? counts, CancellationToken ct) =>
        SendAsync<TextStatsResponse>(HttpMethod.Post, "api/text/stats", new { text = Clip(text, 200_000), counts }, ct);

    public Task<InsightsResponse> InsightsAsync(CancellationToken ct) => SendAsync<InsightsResponse>(HttpMethod.Get, "api/insights", null, ct);

    public async Task<List<DocSummary>> ListDocsAsync(CancellationToken ct) =>
        (await SendAsync<DocsResponse>(HttpMethod.Get, "api/docs", null, ct).ConfigureAwait(false)).Docs;

    public Task<DocDetail> GetDocAsync(string id, CancellationToken ct) => SendAsync<DocDetail>(HttpMethod.Get, $"api/docs/{Uri.EscapeDataString(id)}", null, ct);

    async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct) where T : new()
    {
        using var req = new HttpRequestMessage(method, path);
        if (body != null) req.Content = JsonContent.Create(body, body.GetType(), options: Json);
        using var r = await http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureOk(r, ct).ConfigureAwait(false);
        return await r.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false) ?? new T();
    }

    /// <summary>At most <paramref name="max"/> UTF-16 units, never ending on half of a surrogate pair (an emoji cut in two).</summary>
    internal static string Clip(string s, int max)
    {
        if (s.Length <= max) return s;
        int n = max;
        if (n > 0 && char.IsHighSurrogate(s[n - 1])) n--;
        return s[..n];
    }

    static async Task EnsureOk(HttpResponseMessage r, CancellationToken ct)
    {
        if (r.IsSuccessStatusCode) return;
        string message = $"The NimbleWrite server returned HTTP {(int)r.StatusCode}.";
        try
        {
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) message = e.GetString()!;
        }
        catch (JsonException)
        {
        }
        throw new ApiException(message, (int)r.StatusCode);
    }

    public void Dispose() => http.Dispose();
}
