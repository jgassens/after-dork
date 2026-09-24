using System.Runtime.InteropServices;
using AfterDork.Graphics;

namespace AfterDork.Savers;

// Drawing half of the Castaway Chemist port (CastawayChemist.swift L649-2116).

public sealed partial class CastawayChemistView
{
    // Literal loop arrays from the Swift, hoisted so the draw loop doesn't allocate.
    static readonly double[] minusPlus = [-1, 1];
    static readonly double[] plusMinus = [1, -1];
    static readonly double[] weldFractions = [0.34, 0.68];
    static readonly double[] buttonYs = [34, 43, 52];
    static readonly double[] sootEyeXs = [0.5, 7];

    static readonly CGColor[] blockColors =
    [
        new(0.90, 0.55, 0.50, 1),
        new(0.55, 0.72, 0.90, 1),
        new(0.62, 0.85, 0.60, 1),
        new(0.95, 0.85, 0.50, 1),
    ];

    static readonly CGGradient paulingBackdrop = new(
        [new CGColor(0.64, 0.68, 0.74, 1), new CGColor(0.30, 0.33, 0.40, 1)],
        [0, 1]);

    // MARK: - Drawing

    /// One device pixel in scene units — everything renders into a tiny
    /// buffer and gets blown up nearest-neighbor, so this is the chunk size.
    double px = 4;
    BitmapContext? buffer;
    (int w, int h) bufferSize = (0, 0);
    readonly List<CGRect> ditherRects = new();

    bool shakeOn => (boomT >= 0 && boomT < 10) || flying;

    public override void Draw(CGContext screenCtx)
    {
        // The 8-bit magic: render the (clamped) W x H scene at roughly
        // VGA-era resolution with no antialiasing, then blit into bounds
        // with hard pixel edges. Sizing the buffer from W/H — not raw
        // bounds — means a small host (panel preview, picker thumbnail)
        // gets the whole lab scaled down instead of cropped, and covering
        // bounds exactly leaves no unpainted edge strip.
        px = Math.Max(2, Math.Round(H / 340, MidpointRounding.AwayFromZero));
        int pw = (int)Math.Ceiling(W / px);
        int ph = (int)Math.Ceiling(H / px);
        if (buffer is null || bufferSize != (pw, ph))
        {
            buffer?.Dispose();
            // Never cleared between frames, exactly like the Mac buffer: the
            // shake translate leaves last frame's pixels at the edges.
            buffer = new BitmapContext(pw, ph);
            bufferSize = (pw, ph);
        }
        var ctx = buffer;
        ctx.SaveGState();
        ctx.SetShouldAntialias(false);
        ctx.InterpolationQuality = CGInterpolationQuality.None;
        ctx.ScaleBy(1 / px, 1 / px);
        drawScene(ctx);
        ctx.RestoreGState();
        using var img = ctx.MakeImage();
        screenCtx.SaveGState();
        screenCtx.InterpolationQuality = CGInterpolationQuality.None;
        screenCtx.Draw(img, Bounds);
        // Linus hangs OUTSIDE the pixel pass, at full resolution, judging —
        // sharing the lab's camera shake — until chaos knocks him into the
        // pixel world (drawScene draws him while anything is flying or the
        // art is down, so projectiles pass in front of the frame).
        if (!(flying || artAskew))
        {
            screenCtx.ScaleBy(Bounds.Width / W, Bounds.Height / H);
            if (shakeOn)
            {
                screenCtx.TranslateBy(Math.Sin(tick * 2.7) * 4 * u,
                                      Math.Cos(tick * 3.3) * 3 * u);
            }
            drawPauling(screenCtx);
        }
        screenCtx.RestoreGState();
    }

    /// The real photograph, embedded in AfterDork.Core. Null means we fall
    /// back to painted oils.
    static readonly Lazy<CGImage?> paulingPhotoLazy = new(() => CGImage.FromResource("pauling.png"));
    static CGImage? paulingPhoto => paulingPhotoLazy.Value;

    /// Linus Pauling — the actual photograph when available, otherwise
    /// smooth antialiased oils — hanging in defiant contrast to the VGA lab
    /// around him. Gilt frame, nameplate.
    void drawPauling(CGContext ctx) => drawPauling(ctx, ForcePaintedPortrait ? null : paulingPhoto);

    /// <summary>Test hook: draw the painted-oils fallback even though the photo is embedded.</summary>
    internal static bool ForcePaintedPortrait;

    void drawPauling(CGContext ctx, CGImage? photoOrNull)
    {
        double cx = W * 0.355;
        // Knocked clean off the wall by the rampage: face-up on the tile,
        // still crisp, still judging.
        double cy = artAskew ? floorY + 40 * u : H * 0.745;
        double pw = 96 * u, ph = 124 * u;
        ctx.SaveGState();
        ctx.SetShouldAntialias(true);
        if (artAskew)
        {
            ctx.TranslateBy(cx, cy);
            ctx.RotateBy(0.42);
            ctx.TranslateBy(-cx, -cy);
        }
        // Gilt frame, two-tone with an inner bevel
        var outer = new CGRect(cx - pw / 2, cy - ph / 2, pw, ph);
        ctx.SetFillColor(new CGColor(0.55, 0.40, 0.12, 1));
        ctx.Fill(outer);
        ctx.SetFillColor(new CGColor(0.87, 0.70, 0.30, 1));
        ctx.Fill(outer.InsetBy(2.5 * u, 2.5 * u));
        ctx.SetFillColor(new CGColor(0.66, 0.50, 0.18, 1));
        ctx.Fill(outer.InsetBy(5.5 * u, 5.5 * u));
        var inner = outer.InsetBy(8 * u, 8 * u);
        if (photoOrNull is CGImage photo)
        {
            // The man himself, aspect-filled — downscaled ONCE into a 2x
            // cache instead of resampling 1.2 megapixels every frame.
            int key = (int)(inner.Width * 2);
            if ((paulingScaledImg is null || paulingScaledKey != key) && key > 0)
            {
                double iw0 = photo.Width, ih0 = photo.Height;
                double scale0 = Math.Max(key / iw0, inner.Height * 2 / ih0);
                int dw0 = Math.Max((int)(iw0 * scale0), 1), dh0 = Math.Max((int)(ih0 * scale0), 1);
                using (var c = new BitmapContext(dw0, dh0))
                {
                    c.InterpolationQuality = CGInterpolationQuality.High;
                    c.Draw(photo, new CGRect(0, 0, dw0, dh0));
                    var made = c.MakeImage();
                    paulingScaledImg?.Dispose();
                    paulingScaledKey = key;
                    paulingScaledImg = made;
                }
            }
            if (paulingScaledImg is CGImage scaled)
            {
                ctx.SaveGState();
                ctx.Clip(inner);
                double iw = scaled.Width, ih = scaled.Height;
                double scale = Math.Max(inner.Width / iw, inner.Height / ih);
                double dw = iw * scale, dh = ih * scale;
                ctx.Draw(scaled, new CGRect(inner.MidX - dw / 2,
                                            inner.MidY - dh / 2,
                                            dw, dh));
                ctx.RestoreGState();
            }
            drawNameplate(ctx, outer, cx);
            ctx.RestoreGState();
            return;
        }
        // Studio backdrop, softly graded
        ctx.SaveGState();
        ctx.Clip(inner);
        ctx.DrawLinearGradient(paulingBackdrop,
                               new CGPoint(inner.MidX, inner.MaxY),
                               new CGPoint(inner.MidX, inner.MinY),
                               CGGradientDrawingOptions.None);
        ctx.RestoreGState();
        ctx.SaveGState();
        ctx.Clip(inner);
        double hx = inner.MidX;
        double hy = inner.MidY + 10 * u;
        // Suit and shirt
        ctx.SetFillColor(new CGColor(0.15, 0.17, 0.24, 1));
        ctx.FillEllipse(new CGRect(hx - 38 * u, inner.MinY - 26 * u, 76 * u, 58 * u));
        ctx.SetFillColor(new CGColor(0.95, 0.95, 0.93, 1));
        var collar = new CGMutablePath();
        collar.Move(new CGPoint(hx - 9 * u, inner.MinY + 26 * u));
        collar.AddLine(new CGPoint(hx, inner.MinY + 12 * u));
        collar.AddLine(new CGPoint(hx + 9 * u, inner.MinY + 26 * u));
        collar.CloseSubpath();
        ctx.AddPath(collar);
        ctx.FillPath();
        ctx.SetFillColor(new CGColor(0.45, 0.15, 0.15, 1));
        ctx.Fill(new CGRect(hx - 2.5 * u, inner.MinY, 5 * u, 16 * u));
        // Head: long face, the famous forehead; soft side shading
        var skin = new CGColor(0.93, 0.78, 0.66, 1);
        var head = new CGRect(hx - 19 * u, hy - 18 * u, 38 * u, 52 * u);
        ctx.SetFillColor(skin);
        ctx.FillEllipse(head);
        ctx.SaveGState();
        ctx.AddEllipse(head);
        ctx.Clip();
        ctx.SetFillColor(new CGColor(0.78, 0.60, 0.48, 0.55));
        ctx.FillEllipse(head.OffsetBy(7 * u, -3 * u));
        ctx.SetFillColor(skin);
        ctx.FillEllipse(head.OffsetBy(-2 * u, 1 * u).InsetBy(2 * u, 2 * u));
        ctx.RestoreGState();
        // Ears
        ctx.SetFillColor(skin);
        foreach (double sx in minusPlus)
        {
            ctx.FillEllipse(new CGRect(hx + sx * 19 * u - 3.5 * u, hy - 2 * u, 7 * u, 11 * u));
        }
        // White hair: side tufts and a thin sweep over the crown
        var hair = new CGColor(0.94, 0.94, 0.92, 1);
        ctx.SetFillColor(hair);
        foreach (double sx in minusPlus)
        {
            ctx.FillEllipse(new CGRect(hx + sx * 17 * u - 6 * u, hy + 4 * u, 12 * u, 18 * u));
        }
        ctx.SetStrokeColor(hair);
        ctx.SetLineWidth(3.2 * u);
        ctx.AddArc(new CGPoint(hx, hy + 8 * u), 20 * u, 0.55, Math.PI - 0.55, false);
        ctx.StrokePath();
        // Brows, wire glasses, eyes
        ctx.SetStrokeColor(new CGColor(0.88, 0.88, 0.86, 1));
        ctx.SetLineWidth(2.2 * u);
        foreach (double sx in minusPlus)
        {
            ctx.Move(new CGPoint(hx + sx * 4 * u, hy + 15 * u));
            ctx.AddLine(new CGPoint(hx + sx * 13 * u, hy + 16.5 * u));
            ctx.StrokePath();
        }
        ctx.SetStrokeColor(new CGColor(0.25, 0.25, 0.28, 1));
        ctx.SetLineWidth(1.2 * u);
        foreach (double sx in minusPlus)
        {
            ctx.StrokeEllipse(new CGRect(hx + sx * 8.5 * u - 6 * u, hy + 4 * u, 12 * u, 10 * u));
        }
        ctx.Move(new CGPoint(hx - 2.5 * u, hy + 10 * u));
        ctx.AddLine(new CGPoint(hx + 2.5 * u, hy + 10 * u));
        ctx.StrokePath();
        ctx.SetFillColor(new CGColor(0.22, 0.26, 0.34, 1));
        foreach (double sx in minusPlus)
        {
            ctx.FillEllipse(new CGRect(hx + sx * 8.5 * u - 1.8 * u, hy + 7.5 * u, 3.6 * u, 3.6 * u));
        }
        // Nose and the knowing smile of a double laureate
        ctx.SetStrokeColor(new CGColor(0.72, 0.54, 0.42, 1));
        ctx.SetLineWidth(1.4 * u);
        ctx.Move(new CGPoint(hx - 1 * u, hy + 6 * u));
        ctx.AddQuadCurve(new CGPoint(hx + 2.5 * u, hy - 2 * u),
                         new CGPoint(hx + 1 * u, hy + 2 * u));
        ctx.StrokePath();
        ctx.SetStrokeColor(new CGColor(0.55, 0.35, 0.30, 1));
        ctx.SetLineWidth(1.6 * u);
        ctx.Move(new CGPoint(hx - 6 * u, hy - 8 * u));
        ctx.AddQuadCurve(new CGPoint(hx + 7 * u, hy - 7.5 * u),
                         new CGPoint(hx + 0.5 * u, hy - 11 * u));
        ctx.StrokePath();
        ctx.RestoreGState();
        drawNameplate(ctx, outer, cx);
        ctx.RestoreGState();
    }

    int paulingScaledKey;
    CGImage? paulingScaledImg;
    bool nameplateCached;
    double nameplateU;
    NSFont? nameplateFont;
    double nameplateWidth;

    static readonly CGColor nameplateInk = NSColor.CalibratedRed(0.30, 0.20, 0.04, 1);
    const string nameplateText = "L. PAULING";

    void drawNameplate(CGContext ctx, CGRect outer, double cx)
    {
        var plate = new CGRect(cx - 27 * u, outer.MinY + 1.2 * u, 54 * u, 8.6 * u);
        ctx.SetFillColor(new CGColor(0.92, 0.80, 0.42, 1));
        ctx.Fill(plate);
        ctx.SetStrokeColor(new CGColor(0.45, 0.32, 0.08, 1));
        ctx.SetLineWidth(0.8 * u);
        ctx.Stroke(plate);
        // Text draws into the ctx we were HANDED (the pixel buffer or the
        // screen). Cached per text size.
        if (!nameplateCached || nameplateU != u)
        {
            nameplateFont = NSFont.BoldSystemFont(5.4 * u);
            nameplateWidth = nameplateFont.MeasureWidth(nameplateText);
            nameplateU = u;
            nameplateCached = true;
        }
        if (nameplateFont is not null)
        {
            ctx.SaveGState();
            ctx.DrawTextAtBaseline(nameplateText,
                                   new CGPoint(plate.MidX - nameplateWidth / 2, plate.MidY - 1.9 * u),
                                   nameplateFont, nameplateInk);
            ctx.RestoreGState();
        }
    }

    void drawScene(CGContext ctx)
    {
        ctx.SaveGState();
        // The whole lab jolts when the hood goes off — and the entire time
        // something heavy is airborne.
        if (shakeOn)
        {
            ctx.TranslateBy(Math.Sin(tick * 2.7) * 4 * u,
                            Math.Cos(tick * 3.3) * 3 * u);
        }
        drawRoom(ctx);
        // While chaos reigns the portrait lives inside the pixel world, so
        // it shakes, falls, and gets flown past like everything else.
        if (flying || artAskew) drawPauling(ctx);
        // Contact shadows ground everything on the tile
        shadow(ctx, hoodCX, 300 * u);
        shadow(ctx, (W * 0.36 + W * 0.56) / 2, (W * 0.56 - W * 0.36) * 0.94);
        shadow(ctx, deskX, 150 * u);
        shadow(ctx, nmrCX, 160 * u);
        shadow(ctx, nmrCX - 150 * u, 70 * u);
        shadow(ctx, charX, 66 * u);
        drawFumeHood(ctx);
        drawBenchAndRotovap(ctx);
        drawDesk(ctx);
        drawNMR(ctx);
        drawCeilingTubes(ctx);
        if (tubeY >= 0) drawFlyingTube(ctx);
        drawGasRig(ctx);
        drawChar(ctx);
        drawZzz(ctx);
        drawParticles(ctx);
        ctx.RestoreGState();
    }

    void shadow(CGContext ctx, double cx, double w)
    {
        ctx.SetFillColor(new CGColor(0, 0, 0, 0.16));
        ctx.FillEllipse(new CGRect(cx - w / 2, floorY - 7 * u, w, 14 * u));
    }

    /// One band of checkerboard dithering: the honest VGA gradient.
    /// Batched into a single fill so the pattern costs one call, not
    /// hundreds.
    void dither(CGContext ctx, double yBase, int rows, CGColor color)
    {
        ctx.SetFillColor(color);
        var rects = ditherRects;
        rects.Clear();
        for (int r = 0; r < rows; r++)
        {
            double x = r % 2 == 0 ? 0 : px;
            while (x < W)
            {
                rects.Add(new CGRect(x, yBase + r * px, px, px));
                x += 2 * px;
            }
        }
        ctx.Fill(CollectionsMarshal.AsSpan(rects));
    }

    void drawRoom(CGContext ctx)
    {
        // Wall: institutional VGA teal, two tones with a dithered seam
        ctx.SetFillColor(new CGColor(0.45, 0.78, 0.75, 1));
        ctx.Fill(new CGRect(0, 0, W, H));
        ctx.SetFillColor(new CGColor(0.33, 0.62, 0.60, 1));
        ctx.Fill(new CGRect(0, floorY, W, 60 * u));
        dither(ctx, floorY + 60 * u, 3, new CGColor(0.33, 0.62, 0.60, 1));
        // Floor: checkered navy tile, straight off the Sierra island sea
        ctx.SetFillColor(new CGColor(0.16, 0.20, 0.44, 1));
        ctx.Fill(new CGRect(0, 0, W, floorY));
        ctx.SetFillColor(new CGColor(0.26, 0.32, 0.62, 1));
        double tileW = 64 * u;
        double tx = 0;
        bool odd = false;
        while (tx < W)
        {
            if (odd)
            {
                ctx.Fill(new CGRect(tx, 0, tileW, floorY - 8 * u));
            }
            odd = !odd;
            tx += tileW;
        }
        dither(ctx, floorY - 3 * px, 3, new CGColor(0.16, 0.20, 0.44, 1));
        // Ceiling with tile seams and a dithered shadow line
        ctx.SetFillColor(new CGColor(0.92, 0.93, 0.88, 1));
        ctx.Fill(new CGRect(0, ceilY, W, H - ceilY));
        ctx.SetStrokeColor(new CGColor(0.70, 0.72, 0.68, 1));
        ctx.SetLineWidth(1.2 * u);
        double cx = 40 * u;
        while (cx < W)
        {
            ctx.Move(new CGPoint(cx, ceilY));
            ctx.AddLine(new CGPoint(cx, H));
            ctx.StrokePath();
            cx += 120 * u;
        }
        ctx.Move(new CGPoint(0, ceilY));
        ctx.AddLine(new CGPoint(W, ceilY));
        ctx.StrokePath();
        dither(ctx, ceilY - 3 * px, 3, new CGColor(0.92, 0.93, 0.88, 1));
        // Periodic table poster, mandatory in every lab since forever.
        // (pox/poy, NOT px — px is the device-pixel size on self.)
        double pw = 110 * u, ph = 74 * u;
        double pox = W * 0.70 - pw / 2, poy = H * 0.62;
        ctx.SaveGState();
        if (artAskew)
        {
            ctx.TranslateBy(pox + pw / 2, poy + ph / 2);
            ctx.RotateBy(-0.28);
            ctx.TranslateBy(-(pox + pw / 2), -(poy + ph / 2));
        }
        ctx.SetFillColor(new CGColor(0.97, 0.97, 0.94, 1));
        ctx.Fill(new CGRect(pox, poy, pw, ph));
        ctx.SetStrokeColor(new CGColor(0.35, 0.36, 0.40, 1));
        ctx.SetLineWidth(1.4 * u);
        ctx.Stroke(new CGRect(pox, poy, pw, ph));
        double cell = 10.5 * u;
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 9; col++)
            {
                // The table's shape: full top-left and top-right corners,
                // gap in the middle of the upper rows.
                if (row > 2 || col == 0 || col > 5)
                {
                    ctx.SetFillColor(blockColors[(row + col) % 4]);
                    ctx.Fill(new CGRect(pox + 5 * u + col * cell,
                                        poy + ph - 12 * u - row * cell,
                                        cell - 1.5 * u, cell - 1.5 * u));
                }
            }
        }
        ctx.RestoreGState();
        // Wall clock, spinning through the years of the PhD
        var clockC = new CGPoint(W * 0.285, H * 0.72);
        double cr = 22 * u;
        ctx.SaveGState();
        if (artAskew)
        {
            // Rattled off plumb by whatever is flying around
            ctx.TranslateBy(clockC.X, clockC.Y);
            ctx.RotateBy(0.35);
            ctx.TranslateBy(-clockC.X, -clockC.Y);
        }
        ctx.SetFillColor(new CGColor(0.95, 0.95, 0.93, 1));
        ctx.FillEllipse(new CGRect(clockC.X - cr, clockC.Y - cr, 2 * cr, 2 * cr));
        ctx.SetStrokeColor(new CGColor(0.2, 0.2, 0.25, 1));
        ctx.SetLineWidth(2 * u);
        ctx.StrokeEllipse(new CGRect(clockC.X - cr, clockC.Y - cr, 2 * cr, 2 * cr));
        double minA = Math.PI / 2 - tick * 0.05;
        double hourA = Math.PI / 2 - tick * 0.0042;
        ctx.SetLineCap(CGLineCap.Round);
        ctx.SetLineWidth(1.8 * u);
        ctx.Move(clockC);
        ctx.AddLine(new CGPoint(clockC.X + Math.Cos(minA) * cr * 0.78,
                                clockC.Y + Math.Sin(minA) * cr * 0.78));
        ctx.StrokePath();
        ctx.SetLineWidth(2.4 * u);
        ctx.Move(clockC);
        ctx.AddLine(new CGPoint(clockC.X + Math.Cos(hourA) * cr * 0.5,
                                clockC.Y + Math.Sin(hourA) * cr * 0.5));
        ctx.StrokePath();
        ctx.RestoreGState();
    }

    void drawFumeHood(CGContext ctx)
    {
        double halfW = 130 * u;
        double hoodTop = Math.Min(floorY + 540 * u, ceilY - 12 * u);
        double deckY = floorY + 150 * u;
        var frame = new CGColor(0.80, 0.81, 0.84, 1);
        var frameDark = new CGColor(0.52, 0.54, 0.58, 1);
        // Cabinet below the deck
        ctx.SetFillColor(new CGColor(0.58, 0.60, 0.64, 1));
        ctx.Fill(new CGRect(hoodCX - halfW, floorY, 2 * halfW, deckY - floorY));
        ctx.SetStrokeColor(frameDark);
        ctx.SetLineWidth(1.5 * u);
        ctx.Stroke(new CGRect(hoodCX - halfW, floorY, 2 * halfW, deckY - floorY));
        // Hazard diamond on the cabinet, as required by people with clipboards
        var dc = new CGPoint(hoodCX + 60 * u, floorY + 75 * u);
        double dr = 20 * u;
        var diamond = new CGMutablePath();
        diamond.Move(new CGPoint(dc.X, dc.Y + dr));
        diamond.AddLine(new CGPoint(dc.X + dr, dc.Y));
        diamond.AddLine(new CGPoint(dc.X, dc.Y - dr));
        diamond.AddLine(new CGPoint(dc.X - dr, dc.Y));
        diamond.CloseSubpath();
        ctx.AddPath(diamond);
        ctx.SetFillColor(new CGColor(0.95, 0.80, 0.15, 1));
        ctx.FillPath();
        ctx.AddPath(diamond);
        ctx.SetStrokeColor(new CGColor(0.2, 0.18, 0.12, 1));
        ctx.SetLineWidth(1.6 * u);
        ctx.StrokePath();
        ctx.SetFillColor(new CGColor(0.2, 0.18, 0.12, 1));
        ctx.Fill(new CGRect(dc.X - 1.6 * u, dc.Y - 8 * u, 3.2 * u, 11 * u));
        ctx.FillEllipse(new CGRect(dc.X - 1.8 * u, dc.Y - 13 * u, 3.6 * u, 3.6 * u));
        // Interior
        ctx.SetFillColor(new CGColor(0.46, 0.50, 0.52, 1));
        ctx.Fill(new CGRect(hoodCX - halfW, deckY, 2 * halfW, hoodTop - deckY));
        // Detonation flash fills the hood
        if (boomT >= 0 && boomT < 6)
        {
            ctx.SetFillColor(new CGColor(1, 0.95, 0.6, 0.95));
            ctx.Fill(new CGRect(hoodCX - halfW, deckY, 2 * halfW, hoodTop - deckY));
        }
        // Deck
        ctx.SetFillColor(new CGColor(0.25, 0.26, 0.30, 1));
        ctx.Fill(new CGRect(hoodCX - halfW - 6 * u, deckY, 2 * halfW + 12 * u, 8 * u));
        // Reagent bottles at the back
        for (int i = 0; i < capColors.Length; i++)
        {
            var bc = capColors[i];
            double bx = hoodCX - 60 * u + i * 34 * u;
            ctx.SetFillColor(new CGColor(bc.Item1, bc.Item2, bc.Item3, 0.85));
            ctx.Fill(new CGRect(bx, deckY + 8 * u, 14 * u, 26 * u));
            ctx.SetFillColor(frameDark);
            ctx.Fill(new CGRect(bx + 3 * u, deckY + 34 * u, 8 * u, 5 * u));
        }
        // Stir plate and flask, mid-reaction
        ctx.SetFillColor(new CGColor(0.22, 0.23, 0.27, 1));
        ctx.Fill(new CGRect(hoodCX - 88 * u, deckY + 8 * u, 44 * u, 8 * u));
        double fx = hoodCX - 66 * u;
        var flask = new CGMutablePath();
        flask.Move(new CGPoint(fx - 17 * u, deckY + 16 * u));
        flask.AddLine(new CGPoint(fx + 17 * u, deckY + 16 * u));
        flask.AddLine(new CGPoint(fx + 5 * u, deckY + 46 * u));
        flask.AddLine(new CGPoint(fx + 5 * u, deckY + 60 * u));
        flask.AddLine(new CGPoint(fx - 5 * u, deckY + 60 * u));
        flask.AddLine(new CGPoint(fx - 5 * u, deckY + 46 * u));
        flask.CloseSubpath();
        ctx.AddPath(flask);
        ctx.SetFillColor(new CGColor(0.35, 0.85, 0.6, 0.8));
        ctx.FillPath();
        ctx.AddPath(flask);
        ctx.SetStrokeColor(new CGColor(0.9, 0.94, 0.98, 0.9));
        ctx.SetLineWidth(1.4 * u);
        ctx.StrokePath();
        // Bubbles working up out of the mouth
        for (int i = 0; i < 3; i++)
        {
            double by = (tick * 0.9 + i * 26.0) % 78;
            ctx.SetFillColor(new CGColor(1, 1, 1, 0.5 * (1 - by / 78)));
            double br = (2 + by * 0.04) * u;
            ctx.FillEllipse(new CGRect(fx + Math.Sin(by * 0.2 + i) * 4 * u - br,
                                       deckY + 62 * u + by * u - br,
                                       2 * br, 2 * br));
        }
        // Sash: glass pane hanging from the top, rattling after a boom
        double rattle = (boomT >= 0 && boomT < 24) ? Math.Sin(tick * 2.1) * 3 * u : 0;
        double sashBot = deckY + 190 * u + rattle;
        ctx.SetFillColor(new CGColor(0.85, 0.92, 0.98, 0.22));
        ctx.Fill(new CGRect(hoodCX - halfW + 8 * u, sashBot, 2 * halfW - 16 * u, hoodTop - sashBot));
        ctx.SetFillColor(frame);
        ctx.Fill(new CGRect(hoodCX - halfW + 4 * u, sashBot, 2 * halfW - 8 * u, 7 * u));
        // Frame posts and header
        ctx.SetFillColor(frame);
        ctx.Fill(new CGRect(hoodCX - halfW - 10 * u, floorY, 10 * u, hoodTop - floorY + 26 * u));
        ctx.Fill(new CGRect(hoodCX + halfW, floorY, 10 * u, hoodTop - floorY + 26 * u));
        ctx.Fill(new CGRect(hoodCX - halfW - 10 * u, hoodTop, 2 * halfW + 20 * u, 26 * u));
        ctx.SetStrokeColor(frameDark);
        ctx.SetLineWidth(1.2 * u);
        ctx.Stroke(new CGRect(hoodCX - halfW - 10 * u, hoodTop, 2 * halfW + 20 * u, 26 * u));
    }

    void drawBenchAndRotovap(CGContext ctx)
    {
        double benchL = W * 0.36, benchR = W * 0.56;
        // Legs
        ctx.SetFillColor(new CGColor(0.42, 0.36, 0.30, 1));
        ctx.Fill(new CGRect(benchL + 10 * u, floorY, 12 * u, benchY - floorY));
        ctx.Fill(new CGRect(benchR - 22 * u, floorY, 12 * u, benchY - floorY));
        // Top, with a darker front edge for depth
        ctx.SetFillColor(new CGColor(0.16, 0.16, 0.19, 1));
        ctx.Fill(new CGRect(benchL, benchY, benchR - benchL, 12 * u));
        ctx.SetFillColor(new CGColor(0.08, 0.08, 0.10, 1));
        ctx.Fill(new CGRect(benchL, benchY, benchR - benchL, 4 * u));

        double deck = benchY + 12 * u;

        // — The rotovap, drawn to spec: stand tower with control head,
        // coil condenser with vacuum tap, receiving flask hanging beneath,
        // angled rotary drive, evaporating flask dipping into a proper
        // cylindrical bath pot. —
        double towerX = bathCX - 85 * u;
        var cream = new CGColor(0.92, 0.92, 0.88, 1);
        var creamEdge = new CGColor(0.58, 0.59, 0.56, 1);
        var glass = new CGColor(0.85, 0.90, 0.96, 0.9);
        // Unsaved: round caps carry through the rest of drawScene, as on the Mac.
        ctx.SetLineCap(CGLineCap.Round);
        // Stand tower with foot plate
        ctx.SetFillColor(creamEdge);
        ctx.Fill(new CGRect(towerX - 24 * u, deck, 48 * u, 5 * u));
        ctx.SetFillColor(cream);
        ctx.Fill(new CGRect(towerX - 13 * u, deck, 26 * u, 168 * u));
        ctx.SetStrokeColor(creamEdge);
        ctx.SetLineWidth(1.2 * u);
        ctx.Stroke(new CGRect(towerX - 13 * u, deck, 26 * u, 168 * u));
        // Control head: blue display, knob
        ctx.SetFillColor(new CGColor(0.78, 0.80, 0.82, 1));
        ctx.Fill(new CGRect(towerX - 24 * u, deck + 168 * u, 48 * u, 30 * u));
        ctx.SetFillColor(new CGColor(0.20, 0.40, 0.95, 1));
        ctx.Fill(new CGRect(towerX - 17 * u, deck + 180 * u, 18 * u, 11 * u));
        ctx.SetFillColor(new CGColor(0.12, 0.12, 0.15, 1));
        ctx.FillEllipse(new CGRect(towerX + 7 * u, deck + 178 * u, 10 * u, 10 * u));

        var drive = new CGPoint(towerX + 4 * u, deck + 138 * u);
        // Condenser column rising up-left, spiral coil inside
        var condTop = new CGPoint(drive.X - 44 * u, drive.Y + 126 * u);
        ctx.SetStrokeColor(new CGColor(0.80, 0.88, 0.95, 0.45));
        ctx.SetLineWidth(17 * u);
        ctx.Move(drive); ctx.AddLine(condTop); ctx.StrokePath();
        double ax = condTop.X - drive.X, ay = condTop.Y - drive.Y;
        double alen = Math.Max(double.Hypot(ax, ay), 0.001);
        double pxu = -ay / alen, pyu = ax / alen;
        ctx.SetStrokeColor(new CGColor(0.55, 0.75, 0.95, 0.95));
        ctx.SetLineWidth(2.2 * u);
        for (int i = 1; i <= 9; i++)
        {
            double f = i / 10.5;
            double ccx = drive.X + ax * f, ccy = drive.Y + ay * f;
            ctx.Move(new CGPoint(ccx - pxu * 7 * u - ax / alen * 3 * u,
                                 ccy - pyu * 7 * u - ay / alen * 3 * u));
            ctx.AddLine(new CGPoint(ccx + pxu * 7 * u + ax / alen * 3 * u,
                                    ccy + pyu * 7 * u + ay / alen * 3 * u));
            ctx.StrokePath();
        }
        // Vacuum tap with its green plug
        ctx.SetStrokeColor(glass);
        ctx.SetLineWidth(4 * u);
        ctx.Move(condTop);
        ctx.AddLine(new CGPoint(condTop.X - 16 * u, condTop.Y + 6 * u));
        ctx.StrokePath();
        ctx.SetFillColor(new CGColor(0.20, 0.70, 0.35, 1));
        ctx.FillEllipse(new CGRect(condTop.X - 22 * u, condTop.Y + 2 * u, 9 * u, 9 * u));

        // Receiving flask hanging beneath the condenser on a metal clip
        var recv = new CGPoint(drive.X - 42 * u, drive.Y - 50 * u);
        ctx.SetStrokeColor(glass);
        ctx.SetLineWidth(5 * u);
        ctx.Move(new CGPoint(drive.X - 12 * u, drive.Y - 4 * u));
        ctx.AddLine(recv);
        ctx.StrokePath();
        ctx.SetStrokeColor(new CGColor(0.55, 0.58, 0.63, 1));
        ctx.SetLineWidth(3 * u);
        ctx.Move(new CGPoint(drive.X - 24 * u, drive.Y - 20 * u));
        ctx.AddLine(new CGPoint(drive.X - 32 * u, drive.Y - 32 * u));
        ctx.StrokePath();
        double rr = 19 * u;
        ctx.SetFillColor(new CGColor(0.85, 0.90, 1.0, 0.35));
        ctx.FillEllipse(new CGRect(recv.X - rr, recv.Y - rr, 2 * rr, 2 * rr));
        ctx.SaveGState();
        ctx.AddEllipse(new CGRect(recv.X - rr, recv.Y - rr, 2 * rr, 2 * rr));
        ctx.Clip();
        ctx.SetFillColor(new CGColor(0.98, 0.85, 0.45, 0.85));
        ctx.Fill(new CGRect(recv.X - rr, recv.Y - rr, 2 * rr, rr * 0.55));
        ctx.RestoreGState();
        ctx.SetStrokeColor(new CGColor(0.9, 0.94, 0.99, 0.95));
        ctx.SetLineWidth(1.6 * u);
        ctx.StrokeEllipse(new CGRect(recv.X - rr, recv.Y - rr, 2 * rr, 2 * rr));

        // Rotary drive block, angled like it means it, collars both ends
        ctx.SaveGState();
        ctx.TranslateBy(drive.X, drive.Y);
        ctx.RotateBy(-0.62);
        ctx.SetFillColor(new CGColor(0.25, 0.26, 0.30, 1));
        ctx.Fill(new CGRect(-20 * u, -12 * u, 40 * u, 24 * u));
        ctx.SetFillColor(new CGColor(0.10, 0.10, 0.12, 1));
        ctx.Fill(new CGRect(14 * u, -9 * u, 10 * u, 18 * u));
        ctx.Fill(new CGRect(-24 * u, -9 * u, 10 * u, 18 * u));
        ctx.RestoreGState();

        // Evaporating flask on the vapor duct — or bobbing in the drink —
        // with a red Keck clip at the joint
        double potX = bathCX + 34 * u;
        double potW = 98 * u;
        var attachC = new CGPoint(potX - 12 * u, deck + 62 * u);
        var fc = attachC;
        var dropC = new CGPoint(potX + 2 * u, deck + 38 * u + Math.Sin(bobPhase) * 2 * u);
        if (!rotoAttached)
        {
            // Ease into the bath on the way down, and ease back out for the
            // last dozen frames so reattachment isn't a one-frame teleport.
            double f = Math.Min(rotoDropT / 12.0, 1);
            if (rotoDropT > 268) f = Math.Max(0, (280 - rotoDropT) / 12.0);
            fc = new CGPoint(attachC.X + (dropC.X - attachC.X) * f,
                             attachC.Y + (dropC.Y - attachC.Y) * f * f);
        }
        ctx.SetStrokeColor(glass);
        ctx.SetLineWidth(6 * u);
        ctx.Move(drive);
        ctx.AddLine(rotoAttached ? attachC : new CGPoint(
            drive.X + (attachC.X - drive.X) * 0.55,
            drive.Y + (attachC.Y - drive.Y) * 0.55));
        ctx.StrokePath();
        ctx.SetStrokeColor(new CGColor(0.85, 0.20, 0.18, 1));
        ctx.SetLineWidth(2.6 * u);
        const double clipF = 0.42;
        ctx.Move(new CGPoint(drive.X + (attachC.X - drive.X) * clipF - 4 * u,
                             drive.Y + (attachC.Y - drive.Y) * clipF - 5 * u));
        ctx.AddLine(new CGPoint(drive.X + (attachC.X - drive.X) * clipF + 4 * u,
                                drive.Y + (attachC.Y - drive.Y) * clipF + 5 * u));
        ctx.StrokePath();
        double fr = 24 * u;
        ctx.SetFillColor(new CGColor(0.85, 0.90, 1.0, 0.35));
        ctx.FillEllipse(new CGRect(fc.X - fr, fc.Y - fr, 2 * fr, 2 * fr));
        ctx.SaveGState();
        ctx.AddEllipse(new CGRect(fc.X - fr, fc.Y - fr, 2 * fr, 2 * fr));
        ctx.Clip();
        ctx.SetFillColor(new CGColor(0.95, 0.75, 0.25, 0.9));
        ctx.Fill(new CGRect(fc.X - fr, fc.Y - fr, 2 * fr, fr * 0.8));
        ctx.RestoreGState();
        ctx.SetStrokeColor(new CGColor(0.9, 0.94, 0.99, 0.95));
        ctx.SetLineWidth(1.8 * u);
        ctx.StrokeEllipse(new CGRect(fc.X - fr, fc.Y - fr, 2 * fr, 2 * fr));
        if (rotoAttached)
        {
            double a = tick * 0.4;
            ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.75));
            ctx.SetLineWidth(2.2 * u);
            ctx.AddArc(fc, fr * 0.7, a, a + 0.9, false);
            ctx.StrokePath();
        }

        // The bath pot, drawn over the flask so it truly dips inside:
        // cream cylinder, silver rim, hazard sticker
        ctx.SetFillColor(cream);
        ctx.Fill(new CGRect(potX - potW / 2, deck, potW, 48 * u));
        ctx.SetStrokeColor(creamEdge);
        ctx.SetLineWidth(1.2 * u);
        ctx.Stroke(new CGRect(potX - potW / 2, deck, potW, 48 * u));
        ctx.SetFillColor(new CGColor(0.72, 0.74, 0.78, 1));
        ctx.Fill(new CGRect(potX - potW / 2 - 2 * u, deck + 48 * u, potW + 4 * u, 9 * u));
        ctx.SetFillColor(new CGColor(0.92, 0.94, 0.97, 1));
        ctx.Fill(new CGRect(potX - potW / 2 - 2 * u, deck + 55 * u, potW + 4 * u, 2 * u));
        ctx.SetFillColor(new CGColor(0.95, 0.80, 0.15, 1));
        var hd = new CGPoint(potX + potW * 0.28, deck + 18 * u);
        double hdr = 8 * u;
        var hdd = new CGMutablePath();
        hdd.Move(new CGPoint(hd.X, hd.Y + hdr));
        hdd.AddLine(new CGPoint(hd.X + hdr, hd.Y));
        hdd.AddLine(new CGPoint(hd.X, hd.Y - hdr));
        hdd.AddLine(new CGPoint(hd.X - hdr, hd.Y));
        hdd.CloseSubpath();
        ctx.AddPath(hdd);
        ctx.FillPath();
    }

    /// Writing desk with a laptop and the stool where dissertations go to
    /// die. The screen glows while anyone types, and falls asleep shortly
    /// after they do.
    void drawDesk(CGContext ctx)
    {
        double topY = floorY + 122 * u;
        double halfW = 90 * u;
        var wood = new CGColor(0.50, 0.34, 0.20, 1);
        ctx.SetFillColor(wood);
        ctx.Fill(new CGRect(deskX - halfW + 6 * u, floorY, 10 * u, topY - floorY));
        ctx.Fill(new CGRect(deskX + halfW - 16 * u, floorY, 10 * u, topY - floorY));
        ctx.SetFillColor(new CGColor(0.64, 0.45, 0.28, 1));
        ctx.Fill(new CGRect(deskX - halfW, topY, 2 * halfW, 10 * u));
        ctx.SetFillColor(new CGColor(0.38, 0.26, 0.15, 1));
        ctx.Fill(new CGRect(deskX - halfW, topY, 2 * halfW, 3 * u));
        // A proper chair: seat, tilted backrest behind the sitter, legs
        double chairX = stationX(Station.desk);
        ctx.SetFillColor(wood);
        ctx.Fill(new CGRect(chairX - 18 * u, floorY, 8 * u, 60 * u));
        ctx.Fill(new CGRect(chairX + 12 * u, floorY, 8 * u, 60 * u));
        ctx.SetFillColor(new CGColor(0.64, 0.45, 0.28, 1));
        ctx.Fill(new CGRect(chairX - 24 * u, floorY + 60 * u, 48 * u, 8 * u));
        ctx.SetLineCap(CGLineCap.Round);
        ctx.SetStrokeColor(new CGColor(0.64, 0.45, 0.28, 1));
        ctx.SetLineWidth(9 * u);
        ctx.Move(new CGPoint(chairX - 36 * u, floorY + 62 * u));
        ctx.AddLine(new CGPoint(chairX - 47 * u, floorY + 158 * u));
        ctx.StrokePath();
        ctx.SetStrokeColor(new CGColor(0.38, 0.26, 0.15, 1));
        ctx.SetLineWidth(2 * u);
        ctx.Move(new CGPoint(chairX - 36 * u, floorY + 62 * u));
        ctx.AddLine(new CGPoint(chairX - 47 * u, floorY + 158 * u));
        ctx.StrokePath();
        // Laptop: base near the typist, screen leaning away
        double lapX = deskX + 4 * u;
        ctx.SetFillColor(new CGColor(0.30, 0.31, 0.35, 1));
        ctx.Fill(new CGRect(lapX - 26 * u, topY + 10 * u, 48 * u, 5 * u));
        bool asleep = state == CharState.sleeping && sleepT > 220;
        bool inUse = (state == CharState.typing || state == CharState.sleeping) && !asleep;
        ctx.SetStrokeColor(new CGColor(0.22, 0.23, 0.27, 1));
        ctx.SetLineWidth(5 * u);
        ctx.SetLineCap(CGLineCap.Round);
        ctx.Move(new CGPoint(lapX + 17 * u, topY + 13 * u));
        ctx.AddLine(new CGPoint(lapX + 26 * u, topY + 54 * u));
        ctx.StrokePath();
        // Screen glow toward the typist
        ctx.SetStrokeColor(inUse
            ? new CGColor(0.55, 0.80, 1.0, 0.85 + 0.15 * Math.Sin(tick * 0.7))
            : new CGColor(0.10, 0.11, 0.14, 1));
        ctx.SetLineWidth(3 * u);
        ctx.Move(new CGPoint(lapX + 15 * u, topY + 14 * u));
        ctx.AddLine(new CGPoint(lapX + 23 * u, topY + 52 * u));
        ctx.StrokePath();
    }

    /// The gas cylinder (parked, tipping, or rocketing), its dolly, and
    /// the transfer hose wherever it currently leads.
    void drawGasRig(CGContext ctx)
    {
        CGPoint valveTip;
        if (flying && !flyIsNMR)
        {
            // Tank on the loose: drawn at its offset, nose to the wind
            var p = new CGPoint(tankHome.X + flyOff.Dx, tankHome.Y + flyOff.Dy);
            ctx.SaveGState();
            ctx.TranslateBy(p.X, p.Y);
            ctx.RotateBy(flyAngle);
            drawTankBody(ctx, centered: true);
            ctx.RestoreGState();
            return;
        }
        if (cylVisible)
        {
            ctx.SaveGState();
            ctx.TranslateBy(cylX, floorY);
            ctx.RotateBy(cylTilt);
            drawTankBody(ctx, centered: false);
            ctx.RestoreGState();
            if (onDolly)
            {
                // Two-wheeled hand truck under the tilted tank
                var wheelC = new CGPoint(cylX + 22 * u, floorY + 9 * u);
                ctx.SetFillColor(new CGColor(0.12, 0.12, 0.14, 1));
                ctx.FillEllipse(new CGRect(wheelC.X - 10 * u, wheelC.Y - 10 * u, 20 * u, 20 * u));
                ctx.SetStrokeColor(new CGColor(0.45, 0.47, 0.52, 1));
                ctx.SetLineWidth(4 * u);
                ctx.SetLineCap(CGLineCap.Round);
                ctx.Move(new CGPoint(cylX + 26 * u, floorY + 2 * u));
                ctx.AddLine(new CGPoint(cylX - 40 * u, floorY + 150 * u));
                ctx.StrokePath();
            }
            double tilt = cylTilt;
            valveTip = new CGPoint(cylX - Math.Sin(tilt) * 262 * u,
                                   floorY + Math.Cos(tilt) * 262 * u);
        }
        else
        {
            return;
        }
        // The hose, sagging like its owner's optimism
        if (hoseToHand || hoseToPort)
        {
            var end = hoseToPort
                ? new CGPoint(nmrCX + 34 * u, magnetTopY + 32 * u)
                : new CGPoint(charX + facing * 16 * u, floorY + charYOff + 150 * u);
            ctx.SetStrokeColor(new CGColor(0.30, 0.32, 0.36, 1));
            ctx.SetLineWidth(3.5 * u);
            ctx.SetLineCap(CGLineCap.Round);
            ctx.Move(valveTip);
            ctx.AddQuadCurve(end, new CGPoint(
                (valveTip.X + end.X) / 2,
                Math.Min(valveTip.Y, end.Y) - 55 * u));
            ctx.StrokePath();
        }
    }

    /// The cylinder itself: tall as the chemist, shoulder, valve, band.
    void drawTankBody(CGContext ctx, bool centered)
    {
        double h = 260 * u, w = 54 * u;
        double y0 = centered ? -h / 2 : 0;
        var steel = new CGColor(0.42, 0.55, 0.52, 1);
        var steelDark = new CGColor(0.27, 0.37, 0.35, 1);
        ctx.SetFillColor(steel);
        ctx.AddPath(CGPath.RoundedRect(new CGRect(-w / 2, y0, w, h), w * 0.45, 26 * u));
        ctx.FillPath();
        ctx.SetFillColor(steelDark);
        ctx.Fill(new CGRect(-w / 2, y0 + h * 0.62, w, 10 * u));
        ctx.SetStrokeColor(steelDark);
        ctx.SetLineWidth(1.6 * u);
        ctx.AddPath(CGPath.RoundedRect(new CGRect(-w / 2, y0, w, h), w * 0.45, 26 * u));
        ctx.StrokePath();
        // Highlight
        ctx.SetFillColor(new CGColor(1, 1, 1, 0.35));
        ctx.Fill(new CGRect(-w * 0.30, y0 + 12 * u, w * 0.16, h - 40 * u));
        // Valve stem and handwheel
        ctx.SetFillColor(new CGColor(0.55, 0.57, 0.62, 1));
        ctx.Fill(new CGRect(-4 * u, y0 + h, 8 * u, 12 * u));
        ctx.Fill(new CGRect(-12 * u, y0 + h + 10 * u, 24 * u, 5 * u));
    }

    /// Cartoon sleep: fat pixel Z's rising, drifting, dissolving.
    void drawZzz(CGContext ctx)
    {
        foreach (var z in zzz)
        {
            double f = z.age / 130.0;
            double size = (8 + 10 * f) * u;
            ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.95 * (1 - f)));
            ctx.SetLineWidth(2.8 * u);
            ctx.SetLineCap(CGLineCap.Round);
            double x = z.x + Math.Sin(f * 6) * 8 * u + f * 26 * u;
            double y = z.y + f * 95 * u;
            ctx.Move(new CGPoint(x - size / 2, y + size / 2));
            ctx.AddLine(new CGPoint(x + size / 2, y + size / 2));
            ctx.AddLine(new CGPoint(x - size / 2, y - size / 2));
            ctx.AddLine(new CGPoint(x + size / 2, y - size / 2));
            ctx.StrokePath();
        }
    }

    void drawNMR(CGContext ctx)
    {
        double halfW = 60 * u;
        double bodyBot = floorY + 46 * u;
        var steel = new CGColor(0.35, 0.37, 0.42, 1);
        var rim = new CGColor(0.68, 0.70, 0.76, 1);
        // When the magnet itself is airborne, the whole assembly rides its
        // offset (console and ladder stay behind, sensibly declining to go).
        bool magnetFlying = flying && flyIsNMR;
        ctx.SaveGState();
        if (magnetFlying)
        {
            // Wobble fades with the remaining offset so touchdown is smooth.
            double wob = Math.Min(1, double.Hypot(flyOff.Dx, flyOff.Dy) / (120 * u));
            ctx.TranslateBy(nmrHome.X + flyOff.Dx, nmrHome.Y + flyOff.Dy);
            ctx.RotateBy(Math.Sin(tick * 0.31) * 0.22 * wob);
            ctx.TranslateBy(-nmrHome.X, -nmrHome.Y);
        }
        // Tall stilt legs with feet: the can floats above the floor
        ctx.SetFillColor(steel);
        for (int k = 0; k < 3; k++)
        {
            double lx = k switch { 0 => nmrCX - 46 * u, 1 => nmrCX - 5 * u, _ => nmrCX + 36 * u };
            ctx.Fill(new CGRect(lx, floorY, 10 * u, bodyBot - floorY));
            ctx.Fill(new CGRect(lx - 4 * u, floorY, 18 * u, 5 * u));
        }
        // Magnet can: cream vacuum vessel with dished bottom and top rims
        var body = new CGRect(nmrCX - halfW, bodyBot, 2 * halfW, magnetTopY - bodyBot);
        ctx.SetFillColor(new CGColor(0.93, 0.93, 0.90, 1));
        ctx.AddPath(CGPath.RoundedRect(body, 16 * u, 16 * u));
        ctx.FillPath();
        ctx.SetStrokeColor(new CGColor(0.55, 0.57, 0.63, 1));
        ctx.SetLineWidth(1.6 * u);
        ctx.AddPath(CGPath.RoundedRect(body, 16 * u, 16 * u));
        ctx.StrokePath();
        // Weld seams
        foreach (double f in weldFractions)
        {
            double sy = bodyBot + (magnetTopY - bodyBot) * f;
            ctx.Move(new CGPoint(nmrCX - halfW + 4 * u, sy));
            ctx.AddLine(new CGPoint(nmrCX + halfW - 4 * u, sy));
            ctx.StrokePath();
        }
        // Cylinder shading: bright band left of center, dusk on the right
        ctx.SetFillColor(new CGColor(1, 1, 1, 0.55));
        ctx.Fill(new CGRect(nmrCX - halfW * 0.62, bodyBot + 10 * u,
                            halfW * 0.28, magnetTopY - bodyBot - 20 * u));
        ctx.SetFillColor(new CGColor(0, 0, 0, 0.07));
        ctx.Fill(new CGRect(nmrCX + halfW * 0.38, bodyBot + 6 * u,
                            halfW * 0.55, magnetTopY - bodyBot - 12 * u));
        // Top plate, then the three-tower turret crown every NMR wears:
        // tall sample neck in the middle, squat helium and nitrogen ports
        // flanking it.
        ctx.SetFillColor(rim);
        ctx.Fill(new CGRect(nmrCX - halfW + 6 * u, magnetTopY - 3 * u, 2 * halfW - 12 * u, 8 * u));
        ctx.SetFillColor(rim);
        ctx.Fill(new CGRect(nmrCX - 9 * u, magnetTopY + 5 * u, 18 * u, 42 * u));
        ctx.Fill(new CGRect(nmrCX - 13 * u, magnetTopY + 47 * u, 26 * u, 7 * u));
        foreach (double sx in minusPlus)
        {
            ctx.Fill(new CGRect(nmrCX + sx * 34 * u - 6 * u, magnetTopY + 5 * u, 12 * u, 20 * u));
            ctx.Fill(new CGRect(nmrCX + sx * 34 * u - 9 * u, magnetTopY + 25 * u, 18 * u, 6 * u));
        }
        // Status lights: green blink normally, angry red during a quench
        bool blink = (tick / 20) % 2 == 0;
        bool ok = quenchT < 0;
        ctx.SetFillColor(ok
            ? new CGColor(0.3, 0.9, 0.4, blink ? 1 : 0.3)
            : new CGColor(0.95, 0.2, 0.15, blink ? 1 : 0.4));
        ctx.FillEllipse(new CGRect(nmrCX - 24 * u, bodyBot + 30 * u, 7 * u, 7 * u));
        ctx.FillEllipse(new CGRect(nmrCX + 17 * u, bodyBot + 30 * u, 7 * u, 7 * u));
        ctx.RestoreGState();
        // Ladder for sample changes, leaning ambitions of a short person —
        // drawn OUTSIDE the flight transform: it stands on the floor and,
        // unlike the magnet, was never certified for flight.
        double lax = nmrCX + 96 * u;
        ctx.SetStrokeColor(new CGColor(0.80, 0.62, 0.15, 1));
        ctx.SetLineWidth(5 * u);
        ctx.SetLineCap(CGLineCap.Round);
        ctx.Move(new CGPoint(lax - 26 * u, floorY));
        ctx.AddLine(new CGPoint(lax - 4 * u, floorY + 190 * u));
        ctx.StrokePath();
        ctx.Move(new CGPoint(lax + 28 * u, floorY));
        ctx.AddLine(new CGPoint(lax + 2 * u, floorY + 190 * u));
        ctx.StrokePath();
        ctx.SetLineWidth(3.5 * u);
        for (int i = 1; i <= 4; i++)
        {
            double fy = floorY + i * 42 * u;
            double f = i / 4.6;
            ctx.Move(new CGPoint(lax - 26 * u + 22 * u * f, fy));
            ctx.AddLine(new CGPoint(lax + 28 * u - 26 * u * f, fy));
            ctx.StrokePath();
        }
        // Console the chemist stands at, monitor up at chest height
        double conX = nmrCX - 150 * u;
        ctx.SetFillColor(new CGColor(0.40, 0.42, 0.47, 1));
        ctx.Fill(new CGRect(conX - 20 * u, floorY, 40 * u, 150 * u));
        ctx.SetFillColor(new CGColor(0.18, 0.19, 0.23, 1));
        ctx.Fill(new CGRect(conX - 30 * u, floorY + 150 * u, 60 * u, 48 * u));
        bool screenOn = (tick / 45) % 2 == 0;
        ctx.SetFillColor(screenOn
            ? new CGColor(0.25, 0.85, 0.45, 1)
            : new CGColor(0.95, 0.70, 0.2, 1));
        ctx.Fill(new CGRect(conX - 25 * u, floorY + 156 * u, 50 * u, 36 * u));
        // A trace wiggling across the screen, like it's acquiring
        ctx.SetStrokeColor(new CGColor(0.05, 0.15, 0.08, 0.8));
        ctx.SetLineWidth(1.4 * u);
        ctx.Move(new CGPoint(conX - 22 * u, floorY + 172 * u));
        for (int i = 1; i <= 10; i++)
        {
            double sx = conX - 22 * u + i * 4.4 * u;
            double sy = floorY + 172 * u
                + Math.Sin(i * 1.7 + tick * 0.15) * 7 * u;
            ctx.AddLine(new CGPoint(sx, sy));
        }
        ctx.StrokePath();
    }

    void drawCeilingTubes(CGContext ctx)
    {
        foreach (var t in ceilingTubes)
        {
            var @base = new CGPoint(t.x, ceilY);
            var tip = new CGPoint(t.x + t.tilt * 16 * u, ceilY - 15 * u);
            ctx.SetStrokeColor(new CGColor(0.88, 0.92, 0.98, 0.95));
            ctx.SetLineWidth(3 * u);
            ctx.SetLineCap(CGLineCap.Round);
            ctx.Move(@base);
            ctx.AddLine(tip);
            ctx.StrokePath();
            var c = capColors[t.cap];
            ctx.SetFillColor(new CGColor(c.Item1, c.Item2, c.Item3, 1));
            ctx.FillEllipse(new CGRect(tip.X - 3 * u, tip.Y - 3 * u, 6 * u, 6 * u));
        }
    }

    void drawFlyingTube(CGContext ctx)
    {
        ctx.SetStrokeColor(new CGColor(0.9, 0.94, 1.0, 0.95));
        ctx.SetLineWidth(3 * u);
        ctx.SetLineCap(CGLineCap.Round);
        ctx.Move(new CGPoint(tubeX, tubeY));
        ctx.AddLine(new CGPoint(tubeX, tubeY + 16 * u));
        ctx.StrokePath();
        // Motion streaks
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.35));
        ctx.SetLineWidth(1.4 * u);
        foreach (double sgn in minusPlus)
        {
            double dx = sgn * 5 * u;
            ctx.Move(new CGPoint(tubeX + dx, tubeY - 18 * u));
            ctx.AddLine(new CGPoint(tubeX + dx, tubeY - 4 * u));
            ctx.StrokePath();
        }
    }

    /// The grad student — properly tall, so the bench hits at the waist.
    /// Cartoon-inked: fills with dark outlines, knee-length coat, shoes,
    /// ear, nose, a mouth that goes "o" in a crisis. Origin at the feet;
    /// +x is the way they're facing.
    void drawChar(CGContext ctx)
    {
        double s = u * 2.9;
        ctx.SaveGState();
        ctx.TranslateBy(charX, floorY + charYOff);
        ctx.ScaleBy(facing, 1);
        if (fallTilt > 0) ctx.RotateBy(fallTilt);
        bool onLadder = refill == RefillPhase.climbing || refill == RefillPhase.hooking
            || refill == RefillPhase.descending || refill == RefillPhase.tankTip;

        bool moving = state == CharState.walking || state == CharState.fleeing;
        double swing = moving ? Math.Sin(walkPhase) * (state == CharState.fleeing ? 13 : 9) : 0;
        // At the hood they work the way you actually work a hood: back to
        // the room, arms in past the sash.
        bool backTurned = state == CharState.working && station == Station.hood;

        var skin = new CGColor(0.96, 0.80, 0.62, 1);
        var skinEdge = new CGColor(0.70, 0.52, 0.36, 1);
        var coat = new CGColor(0.96, 0.96, 0.99, 1);
        var coatShade = new CGColor(0.80, 0.82, 0.88, 1);
        var ink = new CGColor(0.16, 0.15, 0.20, 1);
        var pants = new CGColor(0.28, 0.33, 0.46, 1);
        var shirt = new CGColor(0.24, 0.62, 0.60, 1);

        ctx.SetLineCap(CGLineCap.Round);
        ctx.SetLineJoin(CGLineJoin.Round);

        bool seated = state == CharState.typing || state == CharState.sleeping;
        if (seated)
        {
            // In the chair: thighs level with the seat, shins down
            ctx.SetStrokeColor(pants);
            ctx.SetLineWidth(8 * s);
            foreach (double lr in plusMinus)
            {
                double off = lr * 1.6 * s;
                ctx.Move(new CGPoint(0, 24 * s));
                ctx.AddLine(new CGPoint(12 * s + off, 23 * s));
                ctx.AddLine(new CGPoint(13.5 * s + off, 5 * s));
                ctx.StrokePath();
                ctx.SetFillColor(ink);
                ctx.FillEllipse(new CGRect(10 * s + off, 0, 13 * s, 6.5 * s));
            }
            // Torso drops to the seat; the coat folds at the lap instead
            // of pooling on the floor.
            ctx.TranslateBy(0, -21.5 * s);
            ctx.Clip(new CGRect(-60 * s, 39 * s, 120 * s, 200 * s));
        }
        else
        {
            // Legs with shoes; shins show below the knee-length coat
            foreach (double lr in plusMinus)
            {
                double fx = (moving ? swing * lr : 5 * lr) * s;
                ctx.SetStrokeColor(pants);
                ctx.SetLineWidth(8 * s);
                ctx.Move(new CGPoint(0, 45 * s));
                ctx.AddLine(new CGPoint(fx, 6 * s));
                ctx.StrokePath();
                ctx.SetFillColor(ink);
                ctx.FillEllipse(new CGRect(fx - 4 * s, 0, 13 * s, 6.5 * s));
            }
        }

        // Knee-length coat, flared at the hem, back edge in shadow
        var body = new CGMutablePath();
        body.Move(new CGPoint(-15 * s, 24 * s));
        body.AddQuadCurve(new CGPoint(-11 * s, 68 * s), new CGPoint(-14 * s, 48 * s));
        body.AddQuadCurve(new CGPoint(0, 76 * s), new CGPoint(-9 * s, 75 * s));
        body.AddQuadCurve(new CGPoint(11 * s, 68 * s), new CGPoint(9 * s, 75 * s));
        body.AddQuadCurve(new CGPoint(15 * s, 24 * s), new CGPoint(14 * s, 48 * s));
        body.CloseSubpath();
        ctx.AddPath(body);
        ctx.SetFillColor(coat);
        ctx.FillPath();
        ctx.SaveGState();
        ctx.AddPath(body);
        ctx.Clip();
        ctx.SetFillColor(coatShade);
        ctx.Fill(new CGRect(-16 * s, 22 * s, 5.5 * s, 56 * s));
        ctx.RestoreGState();
        ctx.AddPath(body);
        ctx.SetStrokeColor(ink);
        ctx.SetLineWidth(1.5 * s);
        ctx.StrokePath();
        if (backTurned)
        {
            // Just the center back seam and yoke — coats are boring from
            // behind, that's how you know it's behind.
            ctx.SetStrokeColor(coatShade);
            ctx.SetLineWidth(1.1 * s);
            ctx.Move(new CGPoint(0, 26 * s));
            ctx.AddLine(new CGPoint(0, 70 * s));
            ctx.StrokePath();
            ctx.Move(new CGPoint(-11 * s, 64 * s));
            ctx.AddLine(new CGPoint(11 * s, 64 * s));
            ctx.StrokePath();
        }
        else
        {
            // Shirt in the collar V, placket, buttons, pen pocket
            var vee = new CGMutablePath();
            vee.Move(new CGPoint(-4 * s, 73 * s));
            vee.AddLine(new CGPoint(2 * s, 61 * s));
            vee.AddLine(new CGPoint(8 * s, 72 * s));
            vee.CloseSubpath();
            ctx.AddPath(vee);
            ctx.SetFillColor(shirt);
            ctx.FillPath();
            ctx.SetStrokeColor(coatShade);
            ctx.SetLineWidth(1.1 * s);
            ctx.Move(new CGPoint(2 * s, 61 * s));
            ctx.AddLine(new CGPoint(2 * s, 27 * s));
            ctx.StrokePath();
            ctx.SetFillColor(ink);
            foreach (double by in buttonYs)
            {
                ctx.FillEllipse(new CGRect(3.4 * s, by * s, 1.9 * s, 1.9 * s));
            }
            ctx.SetStrokeColor(coatShade);
            ctx.Stroke(new CGRect(-10 * s, 52 * s, 6.5 * s, 7 * s));
            ctx.SetStrokeColor(new CGColor(0.85, 0.25, 0.20, 1));
            ctx.SetLineWidth(1.3 * s);
            ctx.Move(new CGPoint(-8 * s, 57 * s));
            ctx.AddLine(new CGPoint(-8 * s, 62 * s));
            ctx.StrokePath();
        }

        // Arms: inked sleeves ending in hands, posed by mood; each hangs
        // from the shoulder on its own side so they don't cross the coat.
        void arm(CGPoint end)
        {
            var sh = new CGPoint(end.X >= 0 ? 8 * s : -8 * s, 68 * s);
            ctx.SetStrokeColor(ink);
            ctx.SetLineWidth(8.6 * s);
            ctx.Move(sh);
            ctx.AddLine(end);
            ctx.StrokePath();
            ctx.SetStrokeColor(coat);
            ctx.SetLineWidth(6.2 * s);
            ctx.Move(sh);
            ctx.AddLine(end);
            ctx.StrokePath();
            ctx.SetFillColor(skin);
            ctx.FillEllipse(new CGRect(end.X - 3.5 * s, end.Y - 3.5 * s, 7 * s, 7 * s));
            ctx.SetStrokeColor(skinEdge);
            ctx.SetLineWidth(0.9 * s);
            ctx.StrokeEllipse(new CGRect(end.X - 3.5 * s, end.Y - 3.5 * s, 7 * s, 7 * s));
        }
        if (onLadder)
        {
            // Gripping rungs, one hand busy with hose or port
            arm(new CGPoint(14 * s, (86 + Math.Sin(climbH * 22) * 5) * s));
            arm(new CGPoint(10 * s, (72 + Math.Cos(climbH * 22) * 5) * s));
        }
        else if (backTurned)
        {
            // Reaching in past the sash — or, on a schedule known only to
            // them, tending an itch through the lab coat.
            bool scratching = (tick / 140) % 4 == 0;
            if (scratching)
            {
                arm(new CGPoint(10 * s, (66 + Math.Sin(tick * 0.22) * 3) * s));
                arm(new CGPoint((-2 + Math.Sin(tick * 0.4) * 2.8) * s, 41 * s));
            }
            else
            {
                arm(new CGPoint(-12 * s, (68 + Math.Sin(tick * 0.24) * 4) * s));
                arm(new CGPoint(12 * s, (66 + Math.Cos(tick * 0.24) * 4) * s));
            }
        }
        else
        {
            switch (state)
            {
                case CharState.working:
                    arm(new CGPoint(-9 * s, 46 * s));
                    arm(new CGPoint(25 * s, (60 + Math.Sin(tick * 0.25) * 5) * s));
                    break;
                case CharState.reacting:
                    arm(new CGPoint(-16 * s, 104 * s));
                    arm(new CGPoint(20 * s, 104 * s));
                    break;
                case CharState.fleeing:
                    arm(new CGPoint((15 + swing * 0.4) * s, 66 * s));
                    arm(new CGPoint((-14 - swing * 0.4) * s, 60 * s));
                    break;
                case CharState.staring:
                    arm(new CGPoint(-9 * s, 46 * s));
                    arm(new CGPoint(12 * s, 100 * s));  // hand shading the eyes
                    break;
                case CharState.typing:
                {
                    // Typing slows as the sandman closes in
                    double prog = Math.Min(1, (double)typingT / Math.Max(sleepAt, 1));
                    double rate = 0.9 - 0.62 * prog;
                    double amp = (2.4 - 1.6 * prog) * s;
                    arm(new CGPoint(13 * s, 62 * s + Math.Sin(tick * rate) * amp));
                    arm(new CGPoint(18 * s, 61 * s + Math.Cos(tick * rate) * amp));
                    break;
                }
                case CharState.sleeping:
                    arm(new CGPoint(14 * s, 60 * s));
                    arm(new CGPoint(18 * s, 59 * s));
                    break;
                case CharState.walking:
                case CharState.idling:
                {
                    // No absent-minded head-scratching while mid-fall or prone.
                    bool scratch = state == CharState.idling && fallTilt <= 0
                        && refill == RefillPhase.none && (tick / 90) % 3 == 0;
                    arm(new CGPoint((-9 - swing * 0.3) * s, 44 * s));
                    arm(scratch
                        ? new CGPoint(8 * s, (105 + Math.Sin(tick * 0.5) * 2) * s)
                        : new CGPoint((9 + swing * 0.3) * s, 44 * s));
                    break;
                }
            }
        }

        // Neck and head, drooping toward the keyboard as sleep wins
        ctx.SetFillColor(skin);
        ctx.Fill(new CGRect(-1 * s, 72 * s, 6 * s, 10 * s));
        var headC = new CGPoint(3 * s, 93 * s);
        if (state == CharState.typing)
        {
            double prog = Math.Min(1, (double)typingT / Math.Max(sleepAt, 1));
            headC = headC with { X = headC.X + prog * 4 * s };
            headC = headC with
            {
                Y = headC.Y - (prog * prog * 8 * s
                    + Math.Max(0, Math.Sin(tick * 0.05)) * prog * 3 * s)
            };
        }
        else if (state == CharState.sleeping)
        {
            headC = headC with { X = headC.X + 6 * s };
            headC = headC with { Y = headC.Y - (13 * s - Math.Sin(tick * 0.06) * 1.5 * s) };
        }
        double hr = 11 * s;
        ctx.SetFillColor(skin);
        ctx.FillEllipse(new CGRect(headC.X - hr, headC.Y - hr, 2 * hr, 2 * hr));
        ctx.SetStrokeColor(skinEdge);
        ctx.SetLineWidth(1.1 * s);
        ctx.StrokeEllipse(new CGRect(headC.X - hr, headC.Y - hr, 2 * hr, 2 * hr));
        if (backTurned)
        {
            // Back of the head: ears at both edges, a wall of hair, the
            // goggle strap, and the bun dead center.
            ctx.SetFillColor(skin);
            foreach (double sx in minusPlus)
            {
                ctx.FillEllipse(new CGRect(headC.X + sx * hr - 2.3 * s, headC.Y - 2.4 * s,
                                           4.6 * s, 5.6 * s));
            }
            var hairB = new CGColor(0.35, 0.23, 0.13, 1);
            ctx.SetFillColor(hairB);
            ctx.FillEllipse(new CGRect(headC.X - 10 * s, headC.Y - 7 * s, 20 * s, 18.5 * s));
            ctx.SetStrokeColor(ink);
            ctx.SetLineWidth(1.8 * s);
            ctx.Move(new CGPoint(headC.X - 10.5 * s, headC.Y + 1.5 * s));
            ctx.AddLine(new CGPoint(headC.X + 10.5 * s, headC.Y + 1.5 * s));
            ctx.StrokePath();
            ctx.SetFillColor(hairB);
            ctx.FillEllipse(new CGRect(headC.X - 4.5 * s, headC.Y - 2 * s, 9 * s, 9 * s));
            ctx.SetStrokeColor(new CGColor(0.22, 0.14, 0.07, 1));
            ctx.SetLineWidth(1 * s);
            ctx.StrokeEllipse(new CGRect(headC.X - 4.5 * s, headC.Y - 2 * s, 9 * s, 9 * s));
        }
        else
        {
            // Ear at the back, nose on the leading edge
            ctx.SetFillColor(skin);
            ctx.FillEllipse(new CGRect(headC.X - hr - 1.6 * s, headC.Y - 2.4 * s, 4.6 * s, 5.6 * s));
            ctx.SetStrokeColor(skinEdge);
            ctx.StrokeEllipse(new CGRect(headC.X - hr - 1.6 * s, headC.Y - 2.4 * s, 4.6 * s, 5.6 * s));
            ctx.SetFillColor(skin);
            ctx.FillEllipse(new CGRect(headC.X + hr - 1.6 * s, headC.Y - 1.4 * s, 4.6 * s, 4.2 * s));
            ctx.SetStrokeColor(skinEdge);
            ctx.StrokeEllipse(new CGRect(headC.X + hr - 1.6 * s, headC.Y - 1.4 * s, 4.6 * s, 4.2 * s));
            // Mouth: a small smile, or an "o" of horror
            var ink90 = new CGColor(0.16, 0.15, 0.20, 0.9);
            if (state == CharState.reacting || state == CharState.fleeing)
            {
                ctx.SetFillColor(ink90);
                ctx.FillEllipse(new CGRect(headC.X + 4.5 * s, headC.Y - 7 * s, 3.6 * s, 4.6 * s));
            }
            else
            {
                ctx.SetStrokeColor(ink90);
                ctx.SetLineWidth(1.1 * s);
                ctx.Move(new CGPoint(headC.X + 3.5 * s, headC.Y - 5 * s));
                ctx.AddQuadCurve(new CGPoint(headC.X + 8.5 * s, headC.Y - 3.2 * s),
                                 new CGPoint(headC.X + 6.5 * s, headC.Y - 5.8 * s));
                ctx.StrokePath();
            }
            // Neutral shag with a little bun at the back: could be anyone in
            // this lab, which is the point.
            var hair = new CGColor(0.35, 0.23, 0.13, 1);
            var cap = new CGMutablePath();
            cap.AddArc(headC, hr + 1.1 * s, 0.5, Math.PI + 1.0, false);
            cap.CloseSubpath();
            ctx.AddPath(cap);
            ctx.SetFillColor(hair);
            ctx.FillPath();
            var fringe = new CGMutablePath();
            fringe.Move(new CGPoint(headC.X + 9.5 * s, headC.Y + 6.5 * s));
            fringe.AddLine(new CGPoint(headC.X + 11 * s, headC.Y + 2.5 * s));
            fringe.AddLine(new CGPoint(headC.X + 5.5 * s, headC.Y + 5 * s));
            fringe.CloseSubpath();
            ctx.AddPath(fringe);
            ctx.FillPath();
            ctx.SetFillColor(hair);
            ctx.FillEllipse(new CGRect(headC.X - hr - 5.5 * s, headC.Y + 2 * s, 9 * s, 9 * s));
            // Goggle strap and lens, with a glint
            ctx.SetStrokeColor(ink);
            ctx.SetLineWidth(1.8 * s);
            ctx.Move(new CGPoint(headC.X - 9.5 * s, headC.Y + 2.5 * s));
            ctx.AddLine(new CGPoint(headC.X + 6 * s, headC.Y + 2.5 * s));
            ctx.StrokePath();
            double lensY = state == CharState.staring ? headC.Y + 6 * s : headC.Y + 2.5 * s;
            var lens = new CGRect(headC.X + 4.5 * s - 4.8 * s, lensY - 4.8 * s, 9.6 * s, 9.6 * s);
            ctx.SetFillColor(new CGColor(0.75, 0.88, 0.98, 0.95));
            ctx.FillEllipse(lens);
            ctx.SetStrokeColor(ink);
            ctx.SetLineWidth(1.5 * s);
            ctx.StrokeEllipse(lens);
            if (state == CharState.sleeping)
            {
                // Eyes shut behind the lens
                ctx.SetStrokeColor(new CGColor(0.1, 0.1, 0.15, 1));
                ctx.SetLineWidth(1.3 * s);
                ctx.Move(new CGPoint(headC.X + 2.5 * s, lensY));
                ctx.AddLine(new CGPoint(headC.X + 7 * s, lensY));
                ctx.StrokePath();
            }
            else
            {
                ctx.SetFillColor(new CGColor(0.1, 0.1, 0.15, 1));
                ctx.FillEllipse(new CGRect(headC.X + 5.5 * s, lensY - 1.2 * s, 2.8 * s, 2.8 * s));
            }
            ctx.SetFillColor(new CGColor(1, 1, 1, 0.8));
            ctx.FillEllipse(new CGRect(lens.MinX + 1.6 * s, lens.MaxY - 3.6 * s, 2.2 * s, 2.2 * s));
        }

        // The cartoon soot face: everything blackened, hair frazzled into
        // spikes, wide blinking eyes, smoke curling off the head.
        if (sootT >= 0)
        {
            ctx.SetFillColor(new CGColor(0.14, 0.13, 0.15, 0.9));
            ctx.FillEllipse(new CGRect(headC.X - hr - 1.5 * s, headC.Y - hr - 1.5 * s,
                                       2 * hr + 3 * s, 2 * hr + 3 * s));
            ctx.SetStrokeColor(ink);
            ctx.SetLineWidth(1.7 * s);
            for (int i = 0; i < 6; i++)
            {
                double a = 0.5 + i * 0.42;
                ctx.Move(new CGPoint(headC.X + Math.Cos(a) * (hr + 1 * s),
                                     headC.Y + Math.Sin(a) * (hr + 1 * s)));
                ctx.AddLine(new CGPoint(headC.X + Math.Cos(a) * (hr + 6.5 * s),
                                        headC.Y + Math.Sin(a) * (hr + 6.5 * s)));
            }
            ctx.StrokePath();
            // Stunned eyes, blinking through the grime
            if ((tick / 14) % 5 != 0)
            {
                foreach (double ex in sootEyeXs)
                {
                    var er = new CGRect(headC.X + ex * s - 2.9 * s, headC.Y + 1.5 * s - 3.4 * s,
                                        5.8 * s, 6.8 * s);
                    ctx.SetFillColor(new CGColor(1, 1, 0.96, 1));
                    ctx.FillEllipse(er);
                    ctx.SetFillColor(new CGColor(0.1, 0.1, 0.12, 1));
                    ctx.FillEllipse(new CGRect(er.MidX - 1.2 * s, er.MidY - 1.4 * s, 2.4 * s, 2.8 * s));
                }
            }
            // Smoke wisps off the scalp
            for (int k = 0; k < 2; k++)
            {
                double f = (tick * 0.02 + k * 0.5) % 1;
                double wr = (1.6 + 2.4 * f) * s;
                ctx.SetFillColor(new CGColor(0.55, 0.55, 0.58, 0.6 * (1 - f)));
                ctx.FillEllipse(new CGRect(
                    headC.X - 3 * s + k * 6 * s + Math.Sin(f * 8 + k * 3) * 2 * s - wr,
                    headC.Y + hr + 2 * s + f * 16 * s - wr,
                    2 * wr, 2 * wr));
            }
        }
        ctx.RestoreGState();
    }

    void drawParticles(CGContext ctx)
    {
        foreach (var p in particles)
        {
            double fade = (double)p.life / p.maxLife;
            ctx.SetFillColor(new CGColor(p.red, p.green, p.blue, (p.buoyant ? 0.55 : 0.9) * fade));
            ctx.FillEllipse(new CGRect(p.x - p.r, p.y - p.r, 2 * p.r, 2 * p.r));
        }
    }
}
