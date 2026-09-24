using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace AfterDork;

/// <summary>
/// Stands in for Sparkle: asks GitHub for the latest release and offers the
/// Windows download if it's newer than this build. The control panel checks
/// once a day at launch (quietly) and whenever "Updates…" is pressed.
/// </summary>
public static class Updater
{
    public const string ReleasesApi = "https://api.github.com/repos/jgassens/after-dork/releases/latest";

    public enum Outcome { UpToDate, UpdateAvailable, NoWindowsBuild, Failed }

    public sealed record Check(Outcome Outcome, Version Current, Version? Latest, string? PageUrl, string? Message);

    /// <summary>This build's version (major.minor.patch).</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = (Assembly.GetEntryAssembly() ?? typeof(Updater).Assembly).GetName().Version ?? new Version(1, 2, 1);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static async Task<Check> CheckAsync(string url = ReleasesApi, HttpMessageHandler? handler = null, Version? current = null)
    {
        var cur = current ?? CurrentVersion;
        try
        {
            using var http = handler is null ? new HttpClient() : new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AfterDork", cur.ToString()));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                return new Check(Outcome.Failed, cur, null, null, $"GitHub said {(int)resp.StatusCode}.");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return Evaluate(doc.RootElement, cur);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new Check(Outcome.Failed, cur, null, null, e.Message);
        }
    }

    /// <summary>A release asset meant for Windows (not, say, "darwin").</summary>
    public static bool IsWindowsAsset(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("windows") || n.Contains("-win") || n.Contains("_win") || n.Contains("win64") || n.Contains("win-x64")
            || n.EndsWith(".msi") || n.EndsWith(".exe");
    }

    /// <summary>Decides what a GitHub release JSON means for this build.</summary>
    public static Check Evaluate(JsonElement release, Version current)
    {
        string tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string page = release.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
            return new Check(Outcome.Failed, current, null, page, $"Unrecognised release tag “{tag}”.");
        latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
        bool hasWindows = release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array
            && assets.EnumerateArray().Any(a => a.TryGetProperty("name", out var n) && IsWindowsAsset(n.GetString() ?? ""));
        if (latest <= current) return new Check(Outcome.UpToDate, current, latest, page, null);
        return new Check(hasWindows ? Outcome.UpdateAvailable : Outcome.NoWindowsBuild, current, latest, page, null);
    }
}
