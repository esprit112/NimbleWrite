using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NimbleWrite.Desktop.Core;

/// <param name="Latest">The newest release's version, without the "v" ("1.1.0"); empty if there isn't one.</param>
/// <param name="Url">Where to get it: always a github.com page.</param>
/// <param name="Message">What to tell the user.</param>
public sealed record UpdateInfo(string Latest, string Url, bool Newer, string Message);

/// <summary>
/// "Check for updates" in About. Asks GitHub for the latest release only when the user clicks it; never on its own.
/// Nothing about the user or their writing is sent, just the app's name and version as the User-Agent GitHub requires.
/// </summary>
public static class UpdateCheck
{
    public const string LatestUrl = "https://api.github.com/repos/esprit112/NimbleWrite/releases/latest";
    public const string ReleasesPage = "https://github.com/esprit112/NimbleWrite/releases";
    public const string ProjectPage = "https://github.com/esprit112/NimbleWrite";

    /// <summary>This app's version ("1.0.8"), from the assembly's informational version.</summary>
    public static string DesktopVersion { get; } =
        Clean(typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "");

    /// <summary>"1.0.8+3f2a9c1" (the SDK appends the commit) → "1.0.8".</summary>
    public static string Clean(string informational)
    {
        int plus = informational.IndexOf('+');
        return (plus >= 0 ? informational[..plus] : informational).Trim();
    }

    /// <summary>True when the release tag ("v1.1.0") is a later version than <paramref name="current"/>. Pre-release suffixes are ignored.</summary>
    public static bool IsNewer(string tag, string current) =>
        TryVersion(tag, out var latest) && TryVersion(current, out var mine) && latest > mine;

    static bool TryVersion(string s, out Version v)
    {
        s = s.Trim().TrimStart('v', 'V');
        int dash = s.IndexOf('-');
        if (dash >= 0) s = s[..dash];
        return Version.TryParse(s, out v!) && v.Major >= 0;
    }

    public static async Task<UpdateInfo> CheckAsync(string current, HttpMessageHandler? handler, CancellationToken ct)
    {
        using var http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromSeconds(15);
        using var req = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("NimbleWrite", string.IsNullOrEmpty(current) ? "dev" : current));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var res = await http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return new UpdateInfo("", ReleasesPage, false, "No releases have been published yet.");
        if (!res.IsSuccessStatusCode) return new UpdateInfo("", ReleasesPage, false, $"GitHub didn't answer ({(int)res.StatusCode}). Try again later.");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
        // Only ever send the user to GitHub itself.
        if (!url.StartsWith("https://github.com/esprit112/NimbleWrite/", StringComparison.OrdinalIgnoreCase)) url = ReleasesPage;
        string latest = tag.Trim().TrimStart('v', 'V');
        bool newer = IsNewer(tag, current);
        string message = newer ? $"NimbleWrite {latest} is available." : $"You have the latest version ({(string.IsNullOrEmpty(current) ? latest : current)}).";
        return new UpdateInfo(latest, url, newer, message);
    }
}
