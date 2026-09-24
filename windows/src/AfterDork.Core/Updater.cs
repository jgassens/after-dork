using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace AfterDork;

/// <summary>
/// Stands in for Sparkle. Windows builds ship as their own GitHub releases
/// (tagged windows-v1.2.3, never marked "latest" so the Mac download links
/// keep working), so this lists recent releases, finds the newest one that
/// carries a Windows download, and offers it if it's newer than this build.
/// The control panel checks once a day at launch (quietly) and whenever
/// "Updates…" is pressed.
/// </summary>
public static class Updater
{
    public const string ReleasesApi = "https://api.github.com/repos/jgassens/after-dork/releases?per_page=30";

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

    /// <summary>"windows-v1.3.0", "v1.3", "1.3.0" → 1.3.0.</summary>
    public static Version? ParseTag(string tag)
    {
        var t = tag.Trim();
        int i = t.IndexOfAny("0123456789".ToCharArray());
        if (i < 0 || !Version.TryParse(t[i..], out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>
    /// Decides what GitHub's release list (or a single release object) means
    /// for this build: the newest published, non-prerelease release that has
    /// a Windows asset wins.
    /// </summary>
    public static Check Evaluate(JsonElement releases, Version current)
    {
        IEnumerable<JsonElement> all = releases.ValueKind == JsonValueKind.Array ? releases.EnumerateArray() : [releases];
        Version? best = null;
        string? page = null;
        foreach (var r in all)
        {
            if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
            if (r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) continue;
            bool hasWindows = r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array
                && assets.EnumerateArray().Any(a => a.TryGetProperty("name", out var n) && IsWindowsAsset(n.GetString() ?? ""));
            if (!hasWindows) continue;
            var v = ParseTag(r.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "");
            if (v is null || (best is not null && v <= best)) continue;
            best = v;
            page = r.TryGetProperty("html_url", out var h) ? h.GetString() : null;
        }
        if (best is null) return new Check(Outcome.NoWindowsBuild, current, null, null, null);
        return new Check(best > current ? Outcome.UpdateAvailable : Outcome.UpToDate, current, best, page, null);
    }
}
