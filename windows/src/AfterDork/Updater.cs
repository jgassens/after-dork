using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace AfterDork.App;

/// <summary>
/// Stands in for Sparkle: asks GitHub for the latest release and offers the
/// Windows download if it's newer than this build. Checks once a day at
/// launch (quietly) and whenever "Updates…" is pressed.
/// </summary>
internal static class Updater
{
    public const string ReleasesApi = "https://api.github.com/repos/jgassens/after-dork/releases/latest";

    public enum Outcome { UpToDate, UpdateAvailable, NoWindowsBuild, Failed }

    public sealed record Check(Outcome Outcome, Version Current, Version? Latest, string? PageUrl, string? Message);

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 2, 1);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static async Task<Check> CheckAsync(string url = ReleasesApi, HttpMessageHandler? handler = null)
    {
        var current = CurrentVersion;
        try
        {
            using var http = handler is null ? new HttpClient() : new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AfterDork", current.ToString()));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                return new Check(Outcome.Failed, current, null, null, $"GitHub said {(int)resp.StatusCode}.");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return Evaluate(doc.RootElement, current);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new Check(Outcome.Failed, current, null, null, e.Message);
        }
    }

    /// <summary>Decides what a release JSON means for this build (separate for testing).</summary>
    public static Check Evaluate(JsonElement release, Version current)
    {
        string tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string page = release.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
            return new Check(Outcome.Failed, current, null, page, $"Unrecognised release tag “{tag}”.");
        latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
        bool hasWindows = release.TryGetProperty("assets", out var assets) && assets.EnumerateArray().Any(a =>
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            return name.Contains("win", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
        });
        if (latest <= current) return new Check(Outcome.UpToDate, current, latest, page, null);
        return new Check(hasWindows ? Outcome.UpdateAvailable : Outcome.NoWindowsBuild, current, latest, page, null);
    }
}
