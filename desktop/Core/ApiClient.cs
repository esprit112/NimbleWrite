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

    public ApiClient(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(150) };
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
    public async Task<string> WriteAsync(string task, string text, string? context, Action<string> onText, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/ai/write") { Content = JsonContent.Create(new { task, text, context }, options: Json) };
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
