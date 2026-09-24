using Xunit;

namespace AfterDork.Tests;

[Collection("settings")]
public class SettingsTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"afterdork-test-{Guid.NewGuid():N}.json");

    public SettingsTests()
    {
        Environment.SetEnvironmentVariable(Settings.PathVariable, path);
        Settings.Overrides.Clear();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Settings.PathVariable, null);
        Settings.Overrides.Clear();
        File.Delete(path);
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        Assert.Equal(1.5, Settings.Value("OrbitalBox", "spin", 1.5));
        Assert.True(Settings.Flag("FlyingFlasks", "drips", true));
        Assert.Empty(Settings.AllSettings());
    }

    [Fact]
    public void RoundTripsBoolIntAndDouble()
    {
        Settings.Write(new()
        {
            ["FlyingFlasks"] = new() { ["flock"] = 22, ["drips"] = false },
            ["OrbitalBox"] = new() { ["spin"] = 2.5 },
        });
        Assert.Equal(22, Settings.Value("FlyingFlasks", "flock", 15));
        Assert.False(Settings.Flag("FlyingFlasks", "drips", true));
        Assert.Equal(2.5, Settings.Value("OrbitalBox", "spin", 1));
        var all = Settings.AllSettings();
        Assert.IsType<long>(all["FlyingFlasks"]["flock"]);
        Assert.IsType<bool>(all["FlyingFlasks"]["drips"]);
        Assert.IsType<double>(all["OrbitalBox"]["spin"]);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        File.WriteAllText(path, "{ not json");
        Assert.Equal(3, Settings.Value("X", "y", 3));
    }

    [Fact]
    public void OverridesWinOverFile()
    {
        Settings.Write(new() { ["OrbitalBox"] = new() { ["spin"] = 2.0 } });
        Settings.Overrides["OrbitalBox"] = new() { ["spin"] = 0.5 };
        Assert.Equal(0.5, Settings.Value("OrbitalBox", "spin", 1));
    }

    [Fact]
    public void NumbersBridgeToFlagsAndBoolsToValues()
    {
        Settings.Write(new() { ["M"] = new() { ["a"] = 0, ["b"] = true } });
        Assert.False(Settings.Flag("M", "a", true));
        Assert.Equal(1, Settings.Value("M", "b", 0));
    }
}

public class CatalogTests
{
    [Fact]
    public void CatalogMatchesTheMacControlPanel()
    {
        var ids = ModuleCatalog.All.Select(m => m.Id).ToArray();
        Assert.Equal(["FlyingFlasks", "GlasswarePipes", "LatticeMaze", "MystifyPolymers",
                      "StoddartReef", "OrbitalBox", "SmilesRain", "CastawayChemist"], ids);
        Assert.All(ModuleCatalog.All, m => Assert.Equal(2, m.Options.Count));
        var flock = ModuleCatalog.Find("FlyingFlasks")!.Options[0];
        Assert.Equal(new OptionSpec("flock", "Flock size", OptionKind.Slider, 4, 40, true, 15), flock);
        Assert.Equal("Schlenk Pipes", ModuleCatalog.Find("glasswarepipes")!.Display);
    }

    [Fact]
    public void ReadoutFormatting()
    {
        var f = OptionSpec.Slider("spin", "Spin speed", 0.2, 3, false, 1);
        var i = OptionSpec.Slider("flock", "Flock size", 4, 40, true, 15);
        Assert.Equal("1.0x", f.Format(1));
        Assert.Equal("2.5x", f.Format(2.49));
        Assert.Equal("15", i.Format(15));
    }
}
