using SkiaSharp;

namespace AfterDork.Graphics;

/// <summary>
/// CGLayer stand-in: an offscreen surface created for a destination context
/// (CGLayer(ctx, size:, auxiliaryInfo:)) at that context's device
/// resolution, so drawing it back with <see cref="LayerDrawing.Draw(CGContext, CGLayer, CGPoint)"/>
/// is a 1:1 pixel copy. Its <see cref="Context"/> is y-up in the layer's
/// user space (points), and keeps its graphics state between uses, as a
/// CGLayer's context does. Savers use it to cache expensive static drawing.
/// </summary>
public sealed class CGLayer : IDisposable
{
    readonly SKBitmap bitmap;
    readonly SKCanvas canvas;

    /// <summary>Device pixels per user-space point, taken from the destination context.</summary>
    internal readonly double Scale;

    public CGSize Size { get; }
    public CGContext Context { get; }

    public CGLayer(CGContext ctx, CGSize size)
    {
        Scale = DeviceScale(ctx);
        Size = size;
        int pw = Math.Max(1, (int)Math.Ceiling(size.Width * Scale - 1e-6));
        int ph = Math.Max(1, (int)Math.Ceiling(size.Height * Scale - 1e-6));
        bitmap = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        canvas = new SKCanvas(bitmap);
        canvas.Scale((float)Scale);
        Context = new CGContext(canvas, size.Height);
    }

    internal static double DeviceScale(CGContext ctx)
    {
        var m = ctx.Canvas.TotalMatrix;
        return Math.Sqrt(Math.Abs((double)m.ScaleX * m.ScaleY - (double)m.SkewX * m.SkewY));
    }

    /// <summary>
    /// Windows addition: false when the destination's device resolution no
    /// longer matches the layer's (e.g. the window moved to a monitor with a
    /// different DPI), so the caller should make a new layer.
    /// </summary>
    public bool IsCompatible(CGContext ctx) => Math.Abs(DeviceScale(ctx) - Scale) < 1e-6;

    internal SKImage Snapshot()
    {
        canvas.Flush();
        // Wraps the pixels without copying; the caller disposes it straight after drawing.
        return SKImage.FromPixels(bitmap.PeekPixels());
    }

    public void Dispose()
    {
        Context.Dispose();
        canvas.Dispose();
        bitmap.Dispose();
    }
}

public static class LayerDrawing
{
    /// <summary>ctx.draw(layer, at:): the layer's bottom-left corner at the point, at its own size.</summary>
    public static void Draw(this CGContext ctx, CGLayer layer, CGPoint at) =>
        ctx.Draw(layer, new CGRect(at, layer.Size));

    /// <summary>ctx.draw(layer, in:): the layer stretched to fill rect, upright.</summary>
    public static void Draw(this CGContext ctx, CGLayer layer, CGRect rect)
    {
        if (layer.Size.Width <= 0 || layer.Size.Height <= 0) return;
        var c = ctx.Canvas;
        c.Save();
        c.Translate((float)rect.MinX, (float)rect.MaxY);
        c.Scale((float)(rect.Width / layer.Size.Width / layer.Scale),
                (float)(-rect.Height / layer.Size.Height / layer.Scale));
        c.ClipRect(new SKRect(0, 0, (float)(layer.Size.Width * layer.Scale), (float)(layer.Size.Height * layer.Scale)));
        using var paint = new SKPaint { IsAntialias = ctx.Antialias };
        paint.Color = SKColors.White.WithAlpha(ctx.WithAlpha(CGColor.White).Alpha);
        using var img = layer.Snapshot();
        // At its own size the layer maps pixel-for-pixel onto a destination
        // of the same resolution; nearest sampling keeps that copy exact
        // despite float rounding in the transform (e.g. at 1.5x).
        bool oneToOne = rect.Width == layer.Size.Width && rect.Height == layer.Size.Height
                        && Math.Abs(CGLayer.DeviceScale(ctx) - layer.Scale) < 1e-6;
        var sampling = oneToOne ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None) : ctx.Sampling();
        c.DrawImage(img, 0, 0, sampling, paint);
        c.Restore();
    }
}
