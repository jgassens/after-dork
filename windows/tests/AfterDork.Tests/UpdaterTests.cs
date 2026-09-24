using System.Net;
using System.Text.Json;
using Xunit;

namespace AfterDork.Tests;

public class UpdaterTests
{
    static readonly Version Current = new(1, 2, 1);

    static object Release(string tag, bool prerelease = false, params string[] assets) => new
    {
        tag_name = tag,
        prerelease,
        draft = false,
        html_url = "https://github.com/jgassens/after-dork/releases/tag/" + tag,
        assets = assets.Select(a => new { name = a }),
    };

    static JsonElement Json(object o) => JsonDocument.Parse(JsonSerializer.Serialize(o)).RootElement;

    [Fact]
    public void NewestWindowsReleaseWinsEvenWhenMacIsNewer()
    {
        var list = Json(new[]
        {
            Release("v1.4.0", false, "AfterDork-1.4.0.zip"),                      // Mac, newest overall
            Release("windows-v1.3.0", false, "AfterDork-1.3.0-Windows.zip"),
            Release("windows-v1.2.1", false, "AfterDork-1.2.1-Windows.zip"),
        });
        var r = Updater.Evaluate(list, Current);
        Assert.Equal(Updater.Outcome.UpdateAvailable, r.Outcome);
        Assert.Equal(new Version(1, 3, 0), r.Latest);
        Assert.EndsWith("windows-v1.3.0", r.PageUrl);
    }

    [Fact]
    public void SameWindowsVersionIsUpToDate() =>
        Assert.Equal(Updater.Outcome.UpToDate,
            Updater.Evaluate(Json(new[] { Release("windows-v1.2.1", false, "AfterDork-1.2.1-Windows.zip") }), Current).Outcome);

    [Fact]
    public void MacOnlyReleasesMeanNoWindowsBuild() =>
        Assert.Equal(Updater.Outcome.NoWindowsBuild,
            Updater.Evaluate(Json(new[] { Release("v1.3.0", false, "AfterDork-1.3.0.zip", "AfterDork-darwin.zip") }), Current).Outcome);

    [Fact]
    public void PrereleasesAreIgnored() =>
        Assert.Equal(Updater.Outcome.UpToDate,
            Updater.Evaluate(Json(new[]
            {
                Release("windows-v2.0.0", true, "AfterDork-2.0.0-Windows.zip"),
                Release("windows-v1.2.1", false, "AfterDork-1.2.1-Windows.zip"),
            }), Current).Outcome);

    [Theory]
    [InlineData("AfterDork-1.3.0-Windows.zip", true)]
    [InlineData("AfterDork-1.3.0-win-x64.zip", true)]
    [InlineData("AfterDorkSetup.msi", true)]
    [InlineData("AfterDork-1.3.0.zip", false)]
    [InlineData("AfterDork-darwin.zip", false)]
    public void WindowsAssetDetection(string name, bool expected) => Assert.Equal(expected, Updater.IsWindowsAsset(name));

    [Theory]
    [InlineData("windows-v1.3.0", "1.3.0")]
    [InlineData("v1.2", "1.2.0")]
    [InlineData("1.2.1", "1.2.1")]
    public void TagParsing(string tag, string expected) => Assert.Equal(Version.Parse(expected), Updater.ParseTag(tag));

    [Fact]
    public void GarbageTagIsSkipped() =>
        Assert.Equal(Updater.Outcome.NoWindowsBuild,
            Updater.Evaluate(Json(new[] { Release("nightly", false, "AfterDork-Windows.zip") }), Current).Outcome);

    sealed class Stub(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Contains("AfterDork", request.Headers.UserAgent.ToString());
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task HttpFailureIsReportedNotThrown()
    {
        var r = await Updater.CheckAsync("https://example.invalid/x", new Stub(HttpStatusCode.Forbidden, ""), Current);
        Assert.Equal(Updater.Outcome.Failed, r.Outcome);
    }

    [Fact]
    public async Task ParsesReleaseListResponse()
    {
        var body = JsonSerializer.Serialize(new[] { Release("windows-v9.0.0", false, "AfterDork-9.0.0-Windows.zip") });
        var r = await Updater.CheckAsync("https://example.invalid/x", new Stub(HttpStatusCode.OK, body), Current);
        Assert.Equal(Updater.Outcome.UpdateAvailable, r.Outcome);
    }
}
