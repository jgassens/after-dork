using System.Diagnostics;
using AfterDork.Graphics;
using SkiaSharp;

namespace AfterDork;

/// <summary>
/// Offscreen rendering for previews, visual checks and soak tests — the
/// Windows twin of Harness/main.swift's snapshot mode.
/// </summary>
public static class Harness
{
    public sealed record Result(int Frames, double AnimateMsAvg, double DrawMsAvg, double DrawMsMax, List<string> Files);

    /// <param name="drawEvery">Draw every Nth frame (1 = every frame, which also times drawing).</param>
    /// <param name="capture">Frames to save as PNG; null = quarter, half and last, like the Mac harness.</param>
    public static Result Render(Module module, int width, int height, int frames, string? outPrefix,
                                bool preview = false, float scale = 1, int drawEvery = 1,
                                ISet<int>? capture = null, bool captureAll = false,
                                Action<SaverView, int>? onFrame = null)
    {
        var view = module.Make(new CGRect(0, 0, width, height), preview);
        view.StartAnimation();
        capture ??= new HashSet<int> { frames / 4, frames / 2, frames - 1 };
        var info = new SKImageInfo((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var files = new List<string>();
        var sw = new Stopwatch();
        double animMs = 0, drawMs = 0, drawMax = 0;
        int draws = 0;
        for (int i = 0; i < frames; i++)
        {
            sw.Restart();
            view.AnimateOneFrame();
            animMs += sw.Elapsed.TotalMilliseconds;
            onFrame?.Invoke(view, i);
            bool save = outPrefix is not null && (captureAll || capture.Contains(i));
            if (save || (drawEvery > 0 && i % drawEvery == 0))
            {
                sw.Restart();
                SaverRenderer.Render(surface.Canvas, view, scale);
                surface.Canvas.Flush();
                double d = sw.Elapsed.TotalMilliseconds;
                drawMs += d;
                drawMax = Math.Max(drawMax, d);
                draws++;
            }
            if (save)
            {
                var path = $"{outPrefix}-{i:0000}.png";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using var img = surface.Snapshot();
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                using (var fs = File.Create(path)) data.SaveTo(fs);
                files.Add(path);
            }
        }
        view.StopAnimation();
        return new Result(frames, animMs / Math.Max(1, frames), drawMs / Math.Max(1, draws), drawMax, files);
    }
}
