using AfterDork.Graphics;
using Xunit;

namespace AfterDork.Tests;

// Pins down the CoreGraphics behaviours the ported savers depend on.
// Pixel helpers use user-space (y-up) coordinates: At(ctx, x, y) reads the
// pixel whose user-space centre is (x+0.5, y+0.5).
public class GraphicsTests
{
    const int S = 100;

    static uint At(BitmapContext c, int x, int yUp) => c.Pixels[(S - 1 - yUp) * S + x];
    static bool Inked(BitmapContext c, int x, int yUp) => (At(c, x, yUp) >> 24) > 0x80;
    static byte Red(uint p) => (byte)(p >> 16);
    static byte Blue(uint p) => (byte)p;

    [Fact]
    public void UserSpaceIsYUpWithOriginBottomLeft()
    {
        using var c = new BitmapContext(S, S);
        c.SetFillColor(new CGColor(1, 0, 0));
        c.Fill(new CGRect(0, 0, 10, 10));
        Assert.True(Inked(c, 2, 2));
        // Row 0 of the pixel buffer is the top of the image, as in a CG bitmap context.
        Assert.Equal(0u, c.Pixels[0]);
        Assert.NotEqual(0u, c.Pixels[(S - 1) * S]);
    }

    [Fact]
    public void PixelLayoutIsArgbWhenReadAsUInt()
    {
        using var c = new BitmapContext(S, S);
        c.SetFillColor(new CGColor(1, 0, 0));
        c.Fill(new CGRect(0, 0, S, S));
        Assert.Equal(0xFFFF0000u, c.Pixels[0]);
    }

    [Fact]
    public void CounterclockwiseArcSweepsTowardIncreasingAngle()
    {
        using var c = new BitmapContext(S, S);
        c.SetStrokeColor(CGColor.Black);
        c.SetLineWidth(4);
        c.AddArc(new CGPoint(50, 50), 40, 0, Math.PI / 2, clockwise: false);
        c.StrokePath();
        Assert.True(Inked(c, 78, 78));   // 45°: upper right in y-up space
        Assert.False(Inked(c, 21, 21));  // 225° not drawn
    }

    [Fact]
    public void ClockwiseArcGoesTheLongWay()
    {
        using var c = new BitmapContext(S, S);
        c.SetLineWidth(4);
        c.AddArc(new CGPoint(50, 50), 40, 0, Math.PI / 2, clockwise: true);
        c.StrokePath();
        Assert.False(Inked(c, 78, 78));
        Assert.True(Inked(c, 21, 21));
    }

    [Theory]
    [InlineData(0, Math.PI * 2, false, Math.PI * 2)]
    [InlineData(3, 1, false, Math.PI * 2 - 2)]
    [InlineData(1, 3, true, -(Math.PI * 2 - 2))]
    [InlineData(0.55, Math.PI - 0.55, false, Math.PI - 1.1)]
    public void ArcSweepMatchesCoreGraphics(double start, double end, bool cw, double expected) =>
        Assert.Equal(expected, PathOps.ArcSweep(start, end, cw), 9);

    [Fact]
    public void FullCircleArcIsClosedAllTheWayAround()
    {
        using var c = new BitmapContext(S, S);
        c.AddArc(new CGPoint(50, 50), 30, 0, Math.PI * 2, false);
        c.FillPath();
        Assert.True(Inked(c, 50, 50));
        Assert.True(Inked(c, 50, 25));
        Assert.True(Inked(c, 25, 50));
    }

    [Fact]
    public void SaveRestoreCoversColourAndLineStyle()
    {
        using var c = new BitmapContext(S, S);
        c.SetFillColor(new CGColor(1, 0, 0));
        c.SetLineCap(CGLineCap.Round);
        c.SaveGState();
        c.SetFillColor(new CGColor(0, 0, 1));
        c.SetLineCap(CGLineCap.Butt);
        c.RestoreGState();
        c.Fill(new CGRect(0, 0, S, S));
        Assert.Equal(255, Red(At(c, 5, 5)));
        // Round cap survived: a zero-length round-capped line draws a dot.
        c.SetStrokeColor(new CGColor(0, 0, 1));
        c.SetLineWidth(10);
        c.Move(new CGPoint(50, 50));
        c.AddLine(new CGPoint(50, 50));
        c.StrokePath();
        Assert.Equal(255, Blue(At(c, 50, 50)));
    }

    [Fact]
    public void SaveRestoreCoversTransformAndClip()
    {
        using var c = new BitmapContext(S, S);
        c.SaveGState();
        c.Clip(new CGRect(0, 0, 10, 10));
        c.TranslateBy(1000, 0);
        c.RestoreGState();
        c.Fill(new CGRect(50, 50, 10, 10));
        Assert.True(Inked(c, 55, 55));
    }

    [Fact]
    public void PathClipIntersectsAndClearsPath()
    {
        using var c = new BitmapContext(S, S);
        c.AddEllipse(new CGRect(40, 40, 20, 20));
        c.Clip();
        Assert.True(c.IsPathEmpty);
        c.Fill(new CGRect(0, 0, S, S));
        Assert.True(Inked(c, 50, 50));
        Assert.False(Inked(c, 10, 10));
    }

    [Fact]
    public void OverlappingEllipseAndRectFillWithoutHoleUnderNonzero()
    {
        using var c = new BitmapContext(S, S);
        var p = new CGMutablePath();
        p.AddEllipse(new CGRect(20, 20, 40, 40));
        p.AddRect(new CGRect(35, 50, 10, 40));
        c.AddPath(p);
        c.FillPath();
        Assert.True(Inked(c, 40, 55));  // inside both
    }

    [Fact]
    public void EvenOddFillLeavesOverlapEmpty()
    {
        using var c = new BitmapContext(S, S);
        c.AddRect(new CGRect(10, 10, 60, 60));
        c.AddRect(new CGRect(30, 30, 20, 20));
        c.FillPath(evenOdd: true);
        Assert.True(Inked(c, 15, 15));
        Assert.False(Inked(c, 40, 40));
    }

    [Fact]
    public void RotationIsCounterclockwiseInYUpSpace()
    {
        using var c = new BitmapContext(S, S);
        c.TranslateBy(50, 50);
        c.RotateBy(Math.PI / 2);
        c.Fill(new CGRect(10, -2, 20, 4));  // along +x, rotated to +y
        Assert.True(Inked(c, 50, 70));
        Assert.False(Inked(c, 70, 50));
    }

    [Fact]
    public void ImagesDrawUpright()
    {
        using var src = new BitmapContext(10, 10);
        src.SetFillColor(new CGColor(1, 0, 0));
        src.Fill(new CGRect(0, 5, 10, 5));  // top half red
        using var img = src.MakeImage();
        using var c = new BitmapContext(S, S);
        c.InterpolationQuality = CGInterpolationQuality.None;
        c.Draw(img, new CGRect(0, 0, S, S));
        Assert.True(Inked(c, 50, 90));
        Assert.False(Inked(c, 50, 10));
    }

    [Fact]
    public void ImageFromPixelsRowZeroIsTop()
    {
        var px = new uint[4 * 4];
        for (int i = 0; i < 4; i++) px[i] = 0xFF00FF00;  // top row green
        using var img = CGImage.FromPixels(px, 4, 4);
        using var c = new BitmapContext(S, S);
        c.InterpolationQuality = CGInterpolationQuality.None;
        c.Draw(img, new CGRect(0, 0, S, S));
        Assert.True(Inked(c, 50, 95));
        Assert.False(Inked(c, 50, 5));
    }

    [Fact]
    public void LinearGradientWithoutOptionsOnlyPaintsBetweenEndpoints()
    {
        using var c = new BitmapContext(S, S);
        var g = new CGGradient([new CGColor(1, 0, 0), new CGColor(0, 0, 1)], [0, 1]);
        c.DrawLinearGradient(g, new CGPoint(30, 0), new CGPoint(70, 0), CGGradientDrawingOptions.None);
        Assert.False(Inked(c, 10, 50));
        Assert.True(Inked(c, 50, 50));
        Assert.False(Inked(c, 90, 50));
        c.DrawLinearGradient(g, new CGPoint(30, 0), new CGPoint(70, 0),
            CGGradientDrawingOptions.DrawsBeforeStartLocation | CGGradientDrawingOptions.DrawsAfterEndLocation);
        Assert.Equal(255, Red(At(c, 5, 50)));
        Assert.Equal(255, Blue(At(c, 95, 50)));
    }

    [Fact]
    public void GradientLocationsMayBeReversed()
    {
        // StoddartReef passes [light, dark] at [1, 0]: light belongs at the end.
        using var c = new BitmapContext(S, S);
        var g = new CGGradient([new CGColor(1, 0, 0), new CGColor(0, 0, 1)], [1, 0]);
        c.DrawLinearGradient(g, new CGPoint(0, 0), new CGPoint(0, S), CGGradientDrawingOptions.None);
        Assert.True(Blue(At(c, 50, 2)) > 200);
        Assert.True(Red(At(c, 50, 97)) > 200);
    }

    [Fact]
    public void RadialGradientAfterEndFillsEverything()
    {
        using var c = new BitmapContext(S, S);
        var g = new CGGradient([new CGColor(1, 0, 0), new CGColor(0, 0, 1)], [0, 1]);
        c.DrawRadialGradient(g, new CGPoint(50, 50), 0, new CGPoint(50, 50), 20, CGGradientDrawingOptions.DrawsAfterEndLocation);
        Assert.True(Red(At(c, 50, 50)) > 200);
        Assert.Equal(255, Blue(At(c, 2, 2)));
    }

    [Fact]
    public void AntialiasOffGivesHardEdges()
    {
        using var c = new BitmapContext(S, S);
        c.SetShouldAntialias(false);
        c.FillEllipse(new CGRect(10.3, 10.3, 50, 50));
        foreach (var p in c.Pixels.ToArray())
            Assert.True((p >> 24) is 0 or 0xFF);
    }

    [Fact]
    public void TextDrawsUprightWithLineBoxBottomAtPoint()
    {
        var font = NSFont.BoldSystemFont(40);
        var s = new NSAttributedString("L", font, CGColor.Black);
        using var c = new BitmapContext(S, S);
        s.Draw(c, new CGPoint(10, 20));
        // Collect ink extents in user space.
        int minY = S, maxY = -1, wideRowY = -1, widest = 0;
        for (int y = 0; y < S; y++)
        {
            int count = 0;
            for (int x = 0; x < S; x++) if (Inked(c, x, y)) count++;
            if (count > 0) { minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            if (count > widest) { widest = count; wideRowY = y; }
        }
        // Glyph sits above the point, within the line box.
        Assert.True(minY >= 20, $"ink starts at {minY}");
        Assert.True(maxY <= 20 + s.Size().Height, $"ink ends at {maxY}");
        // Upright "L": its widest row (the foot) is at the bottom of the ink.
        Assert.True(wideRowY - minY < (maxY - minY) / 3, $"foot at {wideRowY}, ink {minY}..{maxY}");
        // Baseline is Descent above the point.
        Assert.InRange(minY, 20 + font.Descent - 2, 20 + font.Descent + 2);
    }

    [Fact]
    public void TextFallsBackForMissingGlyphs()
    {
        var font = NSFont.MonospacedSystemFont(19, NSFontWeight.Bold);
        Assert.True(font.MeasureWidth("≡") > 0);
        Assert.True(NSFont.BoldSystemFont(8).MeasureWidth("K⁺") > NSFont.BoldSystemFont(8).MeasureWidth("K"));
    }

    [Fact]
    public void MenloMapsToConsolasAndUnknownFontsAreNull()
    {
        Assert.NotNull(NSFont.Named("Menlo-Bold", 15));
        Assert.Null(NSFont.Named("NoSuchFont-Bold", 15));
    }

    [Fact]
    public void HueConversionMatchesAppKit()
    {
        var red = NSColor.CalibratedHue(0, 1, 1);
        var green = NSColor.CalibratedHue(1.0 / 3, 1, 1);
        var wrap = NSColor.CalibratedHue(1.0, 1, 1);
        Assert.Equal(new CGColor(1, 0, 0), red);
        Assert.Equal(1, green.Green, 9);
        Assert.Equal(red, wrap);
    }

    [Fact]
    public void RectAccessorsStandardize()
    {
        var r = new CGRect(10, 10, -4, 6);
        Assert.Equal(6, r.MinX);
        Assert.Equal(10, r.MaxX);
        Assert.Equal(new CGRect(8, 12, 0, 2), new CGRect(6, 10, 4, 6).InsetBy(2, 2) with { Width = 0 });
    }
}
