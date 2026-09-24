using AfterDork.Graphics;

namespace AfterDork;

public enum OptionKind { Slider, Check }

/// <summary>One user-facing option, as OptionSpec in ControlPanel/main.swift.</summary>
public sealed record OptionSpec(string Key, string Label, OptionKind Kind, double Min, double Max, bool IsInt, double Def)
{
    public static OptionSpec Slider(string key, string label, double min, double max, bool isInt, double def) =>
        new(key, label, OptionKind.Slider, min, max, isInt, def);

    public static OptionSpec Check(string key, string label, double def) =>
        new(key, label, OptionKind.Check, 0, 1, true, def);

    /// <summary>Readout text: "15" for integer sliders, "1.0x" for the rest.</summary>
    public string Format(double v) => IsInt ? ((int)v).ToString() : $"{v:0.0}x";
}

public sealed record Module(string Id, string Display, IReadOnlyList<OptionSpec> Options)
{
    /// <summary>Creates the saver view, looked up by convention as AfterDork.Savers.{Id}View.</summary>
    public SaverView Make(CGRect frame, bool isPreview)
    {
        var type = typeof(Module).Assembly.GetType($"AfterDork.Savers.{Id}View")
                   ?? throw new InvalidOperationException($"Saver {Id} is not ported yet.");
        return (SaverView)Activator.CreateInstance(type, frame, isPreview)!;
    }

    public bool IsAvailable => typeof(Module).Assembly.GetType($"AfterDork.Savers.{Id}View") is not null;
}

/// <summary>The module catalog, copied exactly from ControlPanel/main.swift:263-339.</summary>
public static class ModuleCatalog
{
    public static readonly IReadOnlyList<Module> All =
    [
        new("FlyingFlasks", "Flying Flasks",
        [
            OptionSpec.Slider("flock", "Flock size", 4, 40, true, 15),
            OptionSpec.Check("drips", "Drip liquid", 1),
        ]),
        new("GlasswarePipes", "Schlenk Pipes",
        [
            OptionSpec.Slider("speed", "Growth speed", 0.3, 3, false, 1),
            OptionSpec.Check("alembics", "Fancy glassware", 1),
        ]),
        new("LatticeMaze", "Lattice Maze",
        [
            OptionSpec.Slider("speed", "Crawl speed", 0.3, 3, false, 1),
            OptionSpec.Slider("gas", "Gas molecules", 0, 60, true, 24),
        ]),
        new("MystifyPolymers", "Mystify Origami",
        [
            OptionSpec.Slider("speed", "Speed", 0.3, 3, false, 1),
            OptionSpec.Slider("echo", "Echo depth", 4, 30, true, 18),
        ]),
        new("StoddartReef", "Stoddart Reef",
        [
            OptionSpec.Slider("population", "Population", 3, 20, true, 12),
            OptionSpec.Check("bubbles", "Bubbles", 1),
        ]),
        new("OrbitalBox", "Orbital Box",
        [
            OptionSpec.Slider("spin", "Spin speed", 0.2, 3, false, 1),
            OptionSpec.Slider("morph", "Morph speed", 0.3, 3, false, 1),
        ]),
        new("SmilesRain", "SMILES Rain",
        [
            OptionSpec.Slider("speed", "Rain speed", 0.3, 3, false, 1),
            OptionSpec.Check("reveals", "Reveal names", 1),
        ]),
        new("CastawayChemist", "Castaway Chemist",
        [
            OptionSpec.Slider("chaos", "Chaos", 0.3, 3, false, 1),
            OptionSpec.Check("quench", "NMR quenches", 1),
        ]),
    ];

    public static Module? Find(string id) =>
        All.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
