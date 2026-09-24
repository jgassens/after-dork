using SkiaSharp;

namespace AfterDork.Graphics;

[Flags]
public enum CGGradientDrawingOptions
{
    None = 0,
    DrawsBeforeStartLocation = 1,
    DrawsAfterEndLocation = 2,
}

public sealed class CGGradient
{
    internal readonly SKColor[] Colors;
    internal readonly float[] Locations;

    /// <summary>Like CGGradient(colorsSpace: DeviceRGB, colors:, locations:). Locations may be given in any order.</summary>
    public CGGradient(CGColor[] colors, double[]? locations = null)
    {
        int n = colors.Length;
        var locs = locations ?? Enumerable.Range(0, n).Select(i => n == 1 ? 0.0 : i / (double)(n - 1)).ToArray();
        var order = Enumerable.Range(0, n).OrderBy(i => locs[i]).ToArray();
        Colors = order.Select(i => colors[i].ToSK()).ToArray();
        Locations = order.Select(i => (float)locs[i]).ToArray();
    }
}

/// <summary>
/// A CGContext look-alike over an SKCanvas. The canvas is flipped once on
/// construction so user space is y-up with the origin bottom-left, exactly
/// like an unflipped NSView. Graphics state that Skia's Save() doesn't cover
/// (colours, line style, antialiasing, interpolation, alpha) is kept in a
/// parallel stack so SaveGState/RestoreGState behave like CoreGraphics.
/// </summary>
public class CGContext : IDisposable
{
    struct GState
    {
        public CGColor Fill, Stroke;
        public double LineWidth, MiterLimit, Alpha;
        public CGLineCap Cap;
        public CGLineJoin Join;
        public bool Antialias;
        public CGInterpolationQuality Interpolation;
    }

    internal readonly SKCanvas Canvas;
    readonly int baseSaveCount;
    readonly Stack<GState> stack = new();
    GState gs;
    readonly SKPaint fillPaint = new() { Style = SKPaintStyle.Fill };
    readonly SKPaint strokePaint = new() { Style = SKPaintStyle.Stroke };
    readonly SKPaint imagePaint = new();
    SKPath path = new();

    /// <summary>Height of the user space in points; used for the y-flip.</summary>
    public double Height { get; }

    public CGContext(SKCanvas canvas, double height)
    {
        Canvas = canvas;
        Height = height;
        baseSaveCount = canvas.Save();
        canvas.Translate(0, (float)height);
        canvas.Scale(1, -1);
        gs = Defaults();
    }

    static GState Defaults() => new()
    {
        Fill = CGColor.Black,
        Stroke = CGColor.Black,
        LineWidth = 1,
        MiterLimit = 10,
        Alpha = 1,
        Cap = CGLineCap.Butt,
        Join = CGLineJoin.Miter,
        Antialias = true,
        Interpolation = CGInterpolationQuality.Default,
    };

    public virtual void Dispose()
    {
        Canvas.RestoreToCount(baseSaveCount);
        fillPaint.Dispose();
        strokePaint.Dispose();
        imagePaint.Dispose();
        path.Dispose();
        GC.SuppressFinalize(this);
    }

    // MARK: - Graphics state

    public void SaveGState() { stack.Push(gs); Canvas.Save(); }

    public void RestoreGState()
    {
        if (stack.Count == 0) return;  // CoreGraphics ignores an unbalanced restore
        gs = stack.Pop();
        Canvas.Restore();
    }

    public void SetFillColor(CGColor c) => gs.Fill = c;
    public void SetStrokeColor(CGColor c) => gs.Stroke = c;
    public void SetLineWidth(double w) => gs.LineWidth = w;
    public void SetLineCap(CGLineCap c) => gs.Cap = c;
    public void SetLineJoin(CGLineJoin j) => gs.Join = j;
    public void SetMiterLimit(double m) => gs.MiterLimit = m;
    public void SetShouldAntialias(bool aa) => gs.Antialias = aa;
    public void SetAlpha(double a) => gs.Alpha = a;

    public CGInterpolationQuality InterpolationQuality
    {
        get => gs.Interpolation;
        set => gs.Interpolation = value;
    }

    // MARK: - Transforms

    public void TranslateBy(double x, double y) => Canvas.Translate((float)x, (float)y);
    public void ScaleBy(double x, double y) => Canvas.Scale((float)x, (float)y);
    public void RotateBy(double radians) => Canvas.RotateRadians((float)radians);

    // MARK: - Current path (not part of the graphics state, as in CG)

    public void BeginPath() => path.Reset();
    public void Move(CGPoint to) => PathOps.Move(path, to);
    public void AddLine(CGPoint to) => PathOps.AddLine(path, to);
    public void AddLines(ReadOnlySpan<CGPoint> pts) => PathOps.AddLines(path, pts);
    public void AddQuadCurve(CGPoint to, CGPoint control) => PathOps.AddQuadCurve(path, to, control);
    public void AddCurve(CGPoint to, CGPoint control1, CGPoint control2) => PathOps.AddCurve(path, to, control1, control2);
    public void AddArc(CGPoint center, double radius, double startAngle, double endAngle, bool clockwise) =>
        PathOps.AddArc(path, center, radius, startAngle, endAngle, clockwise);
    public void AddRect(CGRect r) => PathOps.AddRect(path, r);
    public void AddEllipse(CGRect r) => PathOps.AddEllipse(path, r);
    public void AddPath(CGPath p) => path.AddPath(p.Sk, SKPathAddMode.Append);
    public void ClosePath() => path.Close();
    public bool IsPathEmpty => path.IsEmpty;

    public void FillPath(bool evenOdd = false)
    {
        path.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        Canvas.DrawPath(path, FillPaint());
        path.Reset();
    }

    public void StrokePath()
    {
        Canvas.DrawPath(path, StrokePaint());
        path.Reset();
    }

    public void Clip(bool evenOdd = false)
    {
        path.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        Canvas.ClipPath(path, SKClipOperation.Intersect, gs.Antialias);
        path.Reset();
    }

    public void Clip(CGRect to) => Canvas.ClipRect(to.ToSK(), SKClipOperation.Intersect, false);

    // MARK: - Direct drawing (these clear the current path, as CG does)

    public void Fill(CGRect r)
    {
        path.Reset();
        Canvas.DrawRect(r.ToSK(), FillPaint());
    }

    public void Fill(ReadOnlySpan<CGRect> rects)
    {
        path.Reset();
        var p = FillPaint();
        foreach (var r in rects) Canvas.DrawRect(r.ToSK(), p);
    }

    public void Fill(IEnumerable<CGRect> rects)
    {
        path.Reset();
        var p = FillPaint();
        foreach (var r in rects) Canvas.DrawRect(r.ToSK(), p);
    }

    public void Stroke(CGRect r)
    {
        path.Reset();
        Canvas.DrawRect(r.ToSK(), StrokePaint());
    }

    public void Stroke(CGRect r, double width)
    {
        SetLineWidth(width);
        Stroke(r);
    }

    public void FillEllipse(CGRect r)
    {
        path.Reset();
        Canvas.DrawOval(r.ToSK(), FillPaint());
    }

    public void StrokeEllipse(CGRect r)
    {
        path.Reset();
        Canvas.DrawOval(r.ToSK(), StrokePaint());
    }

    // MARK: - Gradients

    public void DrawLinearGradient(CGGradient g, CGPoint start, CGPoint end, CGGradientDrawingOptions options)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len == 0) return;
        using var shader = SKShader.CreateLinearGradient(start.ToSK(), end.ToSK(), g.Colors, g.Locations, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, IsAntialias = false };
        ApplyAlpha(paint);
        bool before = options.HasFlag(CGGradientDrawingOptions.DrawsBeforeStartLocation);
        bool after = options.HasFlag(CGGradientDrawingOptions.DrawsAfterEndLocation);
        if (before && after) { Canvas.DrawPaint(paint); return; }
        // Paint only the band between the perpendiculars through start and
        // end, extended past whichever ends the options allow.
        const double Big = 1e5;
        double ux = dx / len, uy = dy / len, nx = -uy * Big, ny = ux * Big;
        double sx = start.X - (before ? ux * Big : 0), sy = start.Y - (before ? uy * Big : 0);
        double ex = end.X + (after ? ux * Big : 0), ey = end.Y + (after ? uy * Big : 0);
        using var band = new SKPath();
        band.MoveTo((float)(sx + nx), (float)(sy + ny));
        band.LineTo((float)(sx - nx), (float)(sy - ny));
        band.LineTo((float)(ex - nx), (float)(ey - ny));
        band.LineTo((float)(ex + nx), (float)(ey + ny));
        band.Close();
        Canvas.DrawPath(band, paint);
    }

    public void DrawRadialGradient(CGGradient g, CGPoint startCenter, double startRadius,
                                   CGPoint endCenter, double endRadius, CGGradientDrawingOptions options)
    {
        using var shader = SKShader.CreateTwoPointConicalGradient(
            startCenter.ToSK(), (float)startRadius, endCenter.ToSK(), (float)endRadius,
            g.Colors, g.Locations, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, IsAntialias = gs.Antialias };
        ApplyAlpha(paint);
        if (options.HasFlag(CGGradientDrawingOptions.DrawsAfterEndLocation)) { Canvas.DrawPaint(paint); return; }
        Canvas.DrawCircle(endCenter.ToSK(), (float)endRadius, paint);
    }

    // MARK: - Images

    /// <summary>Draws an image into rect with its top row at rect.MaxY, i.e. upright in y-up space (as CG does).</summary>
    public void Draw(CGImage image, CGRect rect)
    {
        Canvas.Save();
        Canvas.Translate((float)rect.MinX, (float)rect.MaxY);
        Canvas.Scale(1, -1);
        var dst = new SKRect(0, 0, (float)Math.Abs(rect.Width), (float)Math.Abs(rect.Height));
        imagePaint.IsAntialias = gs.Antialias;
        imagePaint.Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(gs.Alpha, 0, 1) * 255));
        Canvas.DrawImage(image.Sk, dst, Sampling(), imagePaint);
        Canvas.Restore();
    }

    internal SKSamplingOptions Sampling() => gs.Interpolation switch
    {
        CGInterpolationQuality.None => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
        CGInterpolationQuality.High => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
        _ => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
    };

    // MARK: - Paint helpers

    internal bool Antialias => gs.Antialias;

    internal SKColor WithAlpha(CGColor c)
    {
        var k = c.ToSK();
        if (gs.Alpha >= 1) return k;
        return k.WithAlpha((byte)Math.Round(k.Alpha * Math.Clamp(gs.Alpha, 0, 1)));
    }

    void ApplyAlpha(SKPaint p)
    {
        if (gs.Alpha < 1) p.Color = SKColors.Black.WithAlpha((byte)Math.Round(Math.Clamp(gs.Alpha, 0, 1) * 255));
    }

    SKPaint FillPaint()
    {
        fillPaint.Color = WithAlpha(gs.Fill);
        fillPaint.IsAntialias = gs.Antialias;
        return fillPaint;
    }

    SKPaint StrokePaint()
    {
        var p = strokePaint;
        p.Color = WithAlpha(gs.Stroke);
        p.IsAntialias = gs.Antialias;
        p.StrokeWidth = (float)gs.LineWidth;
        p.StrokeMiter = (float)gs.MiterLimit;
        p.StrokeCap = gs.Cap switch { CGLineCap.Round => SKStrokeCap.Round, CGLineCap.Square => SKStrokeCap.Square, _ => SKStrokeCap.Butt };
        p.StrokeJoin = gs.Join switch { CGLineJoin.Round => SKStrokeJoin.Round, CGLineJoin.Bevel => SKStrokeJoin.Bevel, _ => SKStrokeJoin.Miter };
        return p;
    }
}
