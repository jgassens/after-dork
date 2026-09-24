using AfterDork.Graphics;
using AfterDork.Savers;
using SkiaSharp;
using Xunit;

namespace AfterDork.Tests;

// GlasswarePipes keeps its background and display list in a CGLayer that it
// repairs incrementally. These tests pin that the cached picture is identical
// to a plain full redraw, frame after frame, through growth and a flush.
[Collection("settings")]
public class SaverCacheTests : IDisposable
{
    public SaverCacheTests()
    {
        Environment.SetEnvironmentVariable(Settings.PathVariable,
            Path.Combine(Path.GetTempPath(), $"afterdork-missing-{Guid.NewGuid():N}.json"));
        Settings.Overrides.Clear();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Settings.PathVariable, null);
        Settings.Overrides.Clear();
        GlasswarePipesView.UseLayerCache = true;
        Rng.Unseed();
    }

    static uint[] Pixels(SKSurface s)
    {
        using var img = s.Snapshot();
        var px = new uint[img.Width * img.Height];
        unsafe
        {
            fixed (uint* p = px)
            {
                var info = new SKImageInfo(img.Width, img.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                Assert.True(img.ReadPixels(info, (IntPtr)p, img.Width * 4, 0, 0));
            }
        }
        return px;
    }

    [Theory]
    [InlineData(1280, 720, 1.0f)]
    [InlineData(800, 500, 1.5f)]
    public void ReefBackdropLayerMatchesDirectDrawing(int w, int h, float scale)
    {
        Rng.Seed(3);
        var view = new StoddartReefView(new CGRect(0, 0, w, h), false);
        var info = new SKImageInfo((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var cached = SKSurface.Create(info);
        using var plain = SKSurface.Create(info);
        try
        {
            for (int i = 0; i < 60; i++)
            {
                view.AnimateOneFrame();
                if (i % 20 != 19) continue;
                StoddartReefView.UseBackdropLayer = true;
                SaverRenderer.Render(cached.Canvas, view, scale);
                StoddartReefView.UseBackdropLayer = false;
                SaverRenderer.Render(plain.Canvas, view, scale);
                var a = Pixels(cached);
                var b = Pixels(plain);
                int diff = 0;
                for (int k = 0; k < a.Length; k++) if (a[k] != b[k]) diff++;
                Assert.True(diff == 0, $"frame {i}: {diff} pixels differ");
            }
        }
        finally { StoddartReefView.UseBackdropLayer = true; }
    }

    [Theory]
    [InlineData(640, 400, 1.0f, 3.0)]
    [InlineData(480, 300, 1.5f, 3.0)]
    [InlineData(640, 400, 1.0f, 0.3)]
    public void CachedFramesMatchFullRedraw(int w, int h, float scale, double speed)
    {
        Settings.Overrides["GlasswarePipes"] = new() { ["speed"] = speed };
        Rng.Seed(7);
        var view = new GlasswarePipesView(new CGRect(0, 0, w, h), false);
        var info = new SKImageInfo((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var cached = SKSurface.Create(info);
        using var plain = SKSurface.Create(info);
        int compared = 0;
        bool flushed = false;
        for (int i = 0; i < 900; i++)
        {
            int before = view.ItemCount;
            view.AnimateOneFrame();
            if (view.ItemCount < before) flushed = true;
            // Draw the cache on an irregular cadence so several changes pile up between repairs.
            if (i % 3 == 0 || i % 7 == 0)
            {
                GlasswarePipesView.UseLayerCache = true;
                SaverRenderer.Render(cached.Canvas, view, scale);
            }
            if (i % 35 == 34)
            {
                GlasswarePipesView.UseLayerCache = true;
                SaverRenderer.Render(cached.Canvas, view, scale);
                GlasswarePipesView.UseLayerCache = false;
                SaverRenderer.Render(plain.Canvas, view, scale);
                var a = Pixels(cached);
                var b = Pixels(plain);
                int diff = 0;
                string first = "";
                for (int k = 0; k < a.Length; k++)
                {
                    if (a[k] == b[k]) continue;
                    if (diff < 8) first += $" ({k % info.Width},{k / info.Width}) {a[k]:X8} vs {b[k]:X8};";
                    diff++;
                }
                Assert.True(diff == 0, $"frame {i}: {diff} pixels differ:{first}");
                compared++;
            }
        }
        Assert.True(compared >= 20);
        if (speed >= 3) Assert.True(flushed, "expected the hood to fill and flush");
    }
}
