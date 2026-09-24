using AfterDork.Graphics;
using AfterDork.Savers;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace AfterDork.Tests;

// Drives the Castaway Chemist state machine headlessly through its Trace
// hook and checks that every slapstick branch is reachable.
[Collection("settings")]
public class CastawayTests : IDisposable
{
    readonly ITestOutputHelper output;

    public CastawayTests(ITestOutputHelper output)
    {
        this.output = output;
        Settings.Overrides.Clear();
    }

    public void Dispose()
    {
        Settings.Overrides.Clear();
        CastawayChemistView.Trace = null;
        CastawayChemistView.ForcePaintedPortrait = false;
        Rng.Unseed();
    }

    sealed record Run(List<(string ev, long tick)> Events)
    {
        public int Count(string ev) => Events.Count(e => e.ev == ev);
        public long First(string ev) => Events.FirstOrDefault(e => e.ev == ev).tick;
    }

    /// Events arrive as "name [detail] tick=N"; split off the tick.
    static (string, long) Parse(string s)
    {
        int i = s.LastIndexOf(" tick=", StringComparison.Ordinal);
        return (s[..i], long.Parse(s[(i + 6)..]));
    }

    static Run Simulate(int seed, int frames, double chaos, bool quench,
                        int w = 1280, int h = 720, int drawEvery = 500)
    {
        Settings.Overrides["CastawayChemist"] = new() { ["chaos"] = chaos, ["quench"] = quench };
        Rng.Seed(seed);
        var events = new List<(string, long)>();
        CastawayChemistView.Trace = s => events.Add(Parse(s));
        try
        {
            var view = new CastawayChemistView(new CGRect(0, 0, w, h), false);
            using var surface = drawEvery > 0 ? SKSurface.Create(new SKImageInfo(w, h)) : null;
            for (int i = 0; i < frames; i++)
            {
                view.AnimateOneFrame();
                if (surface is not null && i % drawEvery == 0) SaverRenderer.Render(surface.Canvas, view, 1);
            }
        }
        finally
        {
            CastawayChemistView.Trace = null;
        }
        return new Run(events);
    }

    void Summarize(string label, Run r)
    {
        var counts = r.Events.GroupBy(e => e.ev).OrderBy(g => g.Key)
            .Select(g => $"{g.Key}={g.Count()} (first tick {g.First().tick})");
        output.WriteLine($"{label}: {string.Join(", ", counts)}");
    }

    [Fact]
    public void EveryAccidentAndBothRefillCoinFlipsHappen()
    {
        // 200k frames at chaos 3; add seeds only if a 50/50 branch was unlucky.
        var total = new List<(string, long)>();
        foreach (int seed in new[] { 1, 2, 3, 4, 5 })
        {
            var r = Simulate(seed, 200_000, chaos: 3, quench: true);
            Summarize($"seed {seed}", r);
            total.AddRange(r.Events);
            var all = new Run(total);
            if (new[] { "flip1 fall", "flip1 hook", "flip2 tip", "flip2 fill" }.All(e => all.Count(e) > 0)) break;
        }
        var t = new Run(total);
        foreach (var ev in new[]
                 {
                     "startBoom", "startRotoDrop", "launchTube", "tubeCeiling", "startQuench",
                     "typing", "sleeping", "zzz", "wake", "startRefill",
                     "flip1 fall", "flip1 hook", "flip2 tip", "flip2 fill",
                     "rf tankFly", "rf nmrFly", "rf landing", "rf cleanup", "refillDone",
                 })
        {
            Assert.True(t.Count(ev) > 0, $"never saw {ev}");
        }

        // Each coin flip leads straight into its phase.
        var evs = t.Events;
        for (int i = 0; i < evs.Count - 1; i++)
        {
            string? next = evs[i].Item1 switch
            {
                "flip1 fall" => "rf falling",
                "flip1 hook" => "rf hooking",
                "flip2 tip" => "rf tankTip",
                "flip2 fill" => "rf descending",
                _ => null,
            };
            if (next is not null) Assert.Equal(next, evs[i + 1].Item1);
        }
        // Falls loop back to the ladder; tank and magnet flights both land.
        Assert.Contains(evs.Zip(evs.Skip(1)), p => p.First.Item1 == "rf rising" && p.Second.Item1 == "rf toLadder");
        Assert.Contains(evs.Zip(evs.Skip(1)), p => p.First.Item1 == "rf tankFly" && p.Second.Item1 == "rf landing");
        Assert.Contains(evs.Zip(evs.Skip(1)), p => p.First.Item1 == "rf nmrFly" && p.Second.Item1 == "rf landing");
        // Every saga that starts finishes (bar one still running at the end of a run).
        Assert.InRange(t.Count("startRefill") - t.Count("refillDone"), 0, 5);
    }

    [Fact]
    public void TypingLeadsToSleepThenZzz()
    {
        var r = Simulate(7, 60_000, chaos: 1, quench: true, drawEvery: 0);
        Summarize("seed 7 chaos 1", r);
        var evs = r.Events.Select(e => e.ev).ToList();
        int sleep = evs.IndexOf("sleeping");
        Assert.True(sleep > 0, "never fell asleep");
        Assert.Equal("typing", evs.Take(sleep).Last(e => e is "typing" or "sleeping" or "wake"));
        Assert.Equal("zzz", evs[sleep + 1]);
        Assert.Contains("wake", evs.Skip(sleep));
    }

    [Fact]
    public void QuenchOptionOffMeansNoSpontaneousQuench()
    {
        var off = Simulate(1, 200_000, chaos: 3, quench: false, drawEvery: 0);
        Summarize("quench=false", off);
        Assert.Equal(0, off.Count("startQuench"));
        Assert.True(off.Count("startRefill") > 0);
        var on = Simulate(1, 200_000, chaos: 3, quench: true, drawEvery: 0);
        Assert.True(on.Count("startQuench") > 0);
    }

    [Fact]
    public void LowChaosIsCalmer()
    {
        var calm = Simulate(3, 100_000, chaos: 0.3, quench: true, drawEvery: 0);
        var wild = Simulate(3, 100_000, chaos: 3, quench: true, drawEvery: 0);
        Summarize("chaos 0.3", calm);
        Summarize("chaos 3", wild);
        int Accidents(Run r) => r.Count("startBoom") + r.Count("startRotoDrop") + r.Count("launchTube");
        Assert.True(Accidents(calm) < Accidents(wild));
        Assert.True(calm.Count("startRefill") < wild.Count("startRefill"));
    }

    [Fact]
    public void TimelineForCaptures()
    {
        // Same seed/size/settings as `render CastawayChemist 1280 720 N --seed 1 --set chaos=3`;
        // capture frame index = tick - 1.
        var r = Simulate(1, 20_000, chaos: 3, quench: true, drawEvery: 0);
        foreach (var (ev, tick) in r.Events.Where(e => !e.ev.StartsWith("chooseNext") && e.ev != "zzz"))
            output.WriteLine($"{tick,6} {ev}");
        Assert.True(r.Count("startBoom") > 0);
    }

    [Theory]
    [InlineData(1280, 720, false)]
    [InlineData(152, 112, true)]
    [InlineData(478, 298, true)]
    public void PaintedPortraitFallbackDraws(int w, int h, bool preview)
    {
        CastawayChemistView.ForcePaintedPortrait = true;
        Rng.Seed(1);
        var view = new CastawayChemistView(new CGRect(0, 0, w, h), preview);
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        for (int i = 0; i < 40; i++) view.AnimateOneFrame();
        SaverRenderer.Render(surface.Canvas, view, 1);
        using var img = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        // The portrait's centre (0.355W, 0.745H y-up) is painted, not left black.
        Assert.NotEqual(SKColors.Black, bmp.GetPixel(w * 355 / 1000, h / 4));
        var path = Path.Combine(Path.GetTempPath(), $"castaway-painted-{w}x{h}.png");
        using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
        using (var fs = File.Create(path)) data.SaveTo(fs);
        output.WriteLine(path);
    }
}
