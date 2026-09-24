using Xunit;

namespace AfterDork.Tests;

public class ScrArgsTests
{
    const string Exe = @"C:\x\AfterDork.exe";
    const string Scr = @"C:\x\OrbitalBox.scr";

    [Theory]
    [InlineData("/s")]
    [InlineData("/S")]
    [InlineData("-s")]
    public void SaverMode(string a)
    {
        var r = ScrArgs.Parse([a], Scr);
        Assert.Equal(ScrMode.Saver, r.Mode);
        Assert.Equal("OrbitalBox", r.ModuleId);
    }

    [Theory]
    [InlineData(new[] { "/p", "1234" })]
    [InlineData(new[] { "/p:1234" })]
    [InlineData(new[] { "/P", "1234" })]
    [InlineData(new[] { "-p", "1234" })]
    public void PreviewCarriesHwnd(string[] a)
    {
        var r = ScrArgs.Parse(a, Scr);
        Assert.Equal(ScrMode.Preview, r.Mode);
        Assert.Equal(1234, r.Hwnd);
    }

    [Theory]
    [InlineData(new[] { "/c" }, 0)]
    [InlineData(new[] { "/c:5678" }, 5678)]
    [InlineData(new[] { "/C", "5678" }, 5678)]
    public void ConfigureMode(string[] a, long hwnd)
    {
        var r = ScrArgs.Parse(a, Scr);
        Assert.Equal(ScrMode.Configure, r.Mode);
        Assert.Equal(hwnd, r.Hwnd);
    }

    [Fact]
    public void NoArgsOnScrIsConfigureAndOnExeIsControlPanel()
    {
        Assert.Equal(ScrMode.Configure, ScrArgs.Parse([], Scr).Mode);
        var r = ScrArgs.Parse([], Exe);
        Assert.Equal(ScrMode.ControlPanel, r.Mode);
        Assert.Null(r.ModuleId);
    }

    [Fact]
    public void ModuleFlagOverridesFileName()
    {
        var r = ScrArgs.Parse(["--module", "SmilesRain", "/s"], Scr);
        Assert.Equal(ScrMode.Saver, r.Mode);
        Assert.Equal("SmilesRain", r.ModuleId);
    }

    [Fact]
    public void PasswordAndMalformedPreviewDoNothing()
    {
        Assert.Equal(ScrMode.Password, ScrArgs.Parse(["/a"], Scr).Mode);
        Assert.Equal(ScrMode.Password, ScrArgs.Parse(["/p"], Scr).Mode);
    }

    [Fact]
    public void HarnessModes()
    {
        var r = ScrArgs.Parse(["--render", "OrbitalBox", "640", "480"], Exe);
        Assert.Equal(ScrMode.Render, r.Mode);
        Assert.Equal(["OrbitalBox", "640", "480"], r.Rest);
        Assert.Equal(ScrMode.SnapshotPanel, ScrArgs.Parse(["--snapshot-panel", "x.png"], Exe).Mode);
    }

    [Theory]
    [InlineData(@"C:\a\CastawayChemist.scr", "CastawayChemist")]
    [InlineData(@"C:\a\castawaychemist.SCR", "CastawayChemist")]
    [InlineData(@"C:\a\AfterDork.exe", null)]
    public void ModuleFromPath(string path, string? id) => Assert.Equal(id, ScrArgs.ModuleFromPath(path));
}
