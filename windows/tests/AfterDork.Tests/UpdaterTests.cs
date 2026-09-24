using System.Net;
using System.Text.Json;
using Xunit;

namespace AfterDork.Tests;

public class UpdaterTests
{
    static readonly Version Current = new(1, 2, 1);

    static JsonElement Release(string tag, params string[] assets) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            tag_name = tag,
            html_url = "https://github.com/jgassens/after-dork/releases/tag/" + tag,
            assets = assets.Select(a => new { name = a }),
        })).RootElement;

    [Fact]
    public void SameVersionIsUpToDate() =>
        Assert.Equal(Updater.Outcome.UpToDate, Updater.Evaluate(Release("v1.2.1", "AfterDork-1.2.1.zip"), Current).Outcome);

    [Fact]
    public void OlderReleaseIsUpToDate() =>
        Assert.Equal(Updater.Outcome.UpToDate, Updater.Evaluate(Release("1.1"), Current).Outcome);

    [Fact]
    public void NewerMacOnlyReleaseHasNoWindowsBuild()
    {
        var r = Updater.Evaluate(Release("v1.3.0", "AfterDork-1.3.0.zip", "AfterDork-darwin.zip"), Current);
        Assert.Equal(Updater.Outcome.NoWindowsBuild, r.Outcome);
        Assert.Equal(new Version(1, 3, 0), r.Latest);
    }

    [Theory]
    [InlineData("AfterDork-1.3.0-Windows.zip")]
    [InlineData("AfterDork-1.3.0-win-x64.zip")]
    [InlineData("AfterDorkSetup.msi")]
    public void NewerWindowsReleaseIsOffered(string asset)
    {
        var r = Updater.Evaluate(Release("v1.3.0", "AfterDork-1.3.0.zip", asset), Current);
        Assert.Equal(Updater.Outcome.UpdateAvailable, r.Outcome);
        Assert.StartsWith("https://github.com/", r.PageUrl);
    }

    [Fact]
    public void GarbageTagFails() =>
        Assert.Equal(Updater.Outcome.Failed, Updater.Evaluate(Release("nightly"), Current).Outcome);

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
    public async Task ParsesLiveShapedResponse()
    {
        var body = Release("v9.0.0", "AfterDork-9.0.0-windows.zip").GetRawText();
        var r = await Updater.CheckAsync("https://example.invalid/x", new Stub(HttpStatusCode.OK, body), Current);
        Assert.Equal(Updater.Outcome.UpdateAvailable, r.Outcome);
    }
}
