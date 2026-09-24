using AfterDork.Graphics;

namespace AfterDork.Savers;

// Stoddart Reef — After Dark's "Fish!" aquarium, restocked with mechanically
// interlocked molecules: shuttling bistable rotaxanes, a [2]catenane,
// Borromean rings, a crown-ether jellyfish (K+ included), a ferrocene crab
// scuttling on the sand, PEG seaweed, and a MOF crystal for a castle.
// Port of StoddartReef/StoddartReef.swift.

public sealed class StoddartReefView : SaverView
{
    static double Rnd(double lo, double hi) => Rng.Range(lo, hi);

    enum SwimmerKind
    {
        Rotaxane, Catenane, Borromean, Jellyfish, Trefoil, DaisyChain, Cucurbituril
    }

    struct Swimmer
    {
        public SwimmerKind Kind;
        public double X, Y;
        public double Dir;          // +1 swims right, -1 swims left
        public double Speed;
        public double Phase;
        public double PhaseRate;
        public double Scale;
    }

    struct Bubble
    {
        public double X, Y;
        public double R;
        public double Speed;
        public double Wobble;
    }

    struct Weed
    {
        public double X;
        public int Segments;
        public double Phase;
    }

    Swimmer[] swimmers = [];
    Bubble[] bubbles = [];
    Weed[] weeds = [];
    double crabX = 200;
    double crabDir = 1;
    double crabPhase = 0;
    double t = 0;
    const double sandH = 46;

    static readonly CGColor boxBlue = new(0.25, 0.45, 0.92, 1);
    static readonly CGColor crownRed = new(0.95, 0.45, 0.35, 1);

    static readonly CGGradient waterGradient = new(
        [new CGColor(0.03, 0.20, 0.32, 1), new CGColor(0.005, 0.05, 0.11, 1)], [1, 0]);

    CGLayer? backdrop;
    /// <summary>Tests turn this off to compare against drawing the backdrop directly.</summary>
    internal static bool UseBackdropLayer = true;

    // Per-frame scratch, reused instead of reallocated.
    int[] drawOrder = [];
    readonly CGPoint[] weedPts = new CGPoint[16];
    readonly CGPoint[] oxygenBuf = new CGPoint[6];
    readonly CGPoint[] borroCenters = new CGPoint[3];

    public StoddartReefView(CGRect frame, bool isPreview) : base(frame, isPreview)
    {
        AnimationTimeInterval = 1.0 / 30.0;
        Setup();
    }

    void Setup()
    {
        double w = Math.Max(Bounds.Width, 800), h = Math.Max(Bounds.Height, 500);
        SwimmerKind[] census = [SwimmerKind.Rotaxane, SwimmerKind.Catenane, SwimmerKind.Jellyfish, SwimmerKind.Borromean,
                                SwimmerKind.DaisyChain, SwimmerKind.Cucurbituril, SwimmerKind.Trefoil, SwimmerKind.Rotaxane];
        int pop = Math.Max(3, Math.Min(20, (int)Settings.Value("StoddartReef", "population", 12)));
        if (IsPreview) pop = Math.Min(pop, 6);
        swimmers = new Swimmer[pop];
        for (int i = 0; i < pop; i++)
        {
            var kind = census[i % census.Length];
            swimmers[i] = new Swimmer
            {
                Kind = kind,
                X = Rnd(0, w),
                Y = Rnd(h * 0.25, h * 0.85),
                Dir = Rng.Bool() ? 1 : -1,
                Speed = kind == SwimmerKind.Jellyfish ? Rnd(0.25, 0.5) : Rnd(0.8, 1.9),
                Phase = Rnd(0, 6.28),
                PhaseRate = Rnd(0.03, 0.06),
                Scale = Rnd(0.6, 1.15),
            };
        }
        drawOrder = new int[pop];
        int bubbleCount = Settings.Flag("StoddartReef", "bubbles", true)
            ? (IsPreview ? 6 : 14) : 0;
        bubbles = new Bubble[bubbleCount];
        for (int i = 0; i < bubbleCount; i++)
        {
            bubbles[i] = new Bubble
            {
                X = Rnd(0, w), Y = Rnd(0, h), R = Rnd(2, 5),
                Speed = Rnd(0.8, 2.0), Wobble = Rnd(0, 6.28),
            };
        }
        int weedCount = IsPreview ? 3 : 5;
        weeds = new Weed[weedCount];
        for (int i = 0; i < weedCount; i++)
        {
            weeds[i] = new Weed
            {
                X = w * (0.08 + 0.85 * i / Math.Max(weedCount - 1, 1)) + Rnd(-30, 30),
                Segments = Rng.IntInclusive(9, 15),
                Phase = Rnd(0, 6.28),
            };
        }
        crabX = Rnd(w * 0.15, w * 0.5);
    }

    public override void AnimateOneFrame()
    {
        t += 1.0 / 30.0;
        double w = Math.Max(Bounds.Width, 800), h = Math.Max(Bounds.Height, 500);
        for (int i = 0; i < swimmers.Length; i++)
        {
            swimmers[i].Phase += swimmers[i].PhaseRate * (swimmers[i].Kind == SwimmerKind.Jellyfish ? 2 : 1);
            var s = swimmers[i];  // snapshot: the off-screen checks use the old x
            swimmers[i].X += s.Dir * s.Speed * (0.5 + s.Scale * 0.7);
            double m = 260 * s.Scale;
            if (s.Dir > 0 && s.X > w + m) Respawn(ref swimmers[i], fromLeft: true, w, h);
            if (s.Dir < 0 && s.X < -m) Respawn(ref swimmers[i], fromLeft: false, w, h);
        }
        for (int i = 0; i < bubbles.Length; i++)
        {
            bubbles[i].Y += bubbles[i].Speed;
            bubbles[i].Wobble += 0.08;
            if (bubbles[i].Y > h + 10)
            {
                bubbles[i].Y = sandH - 5;
                bubbles[i].X = Rnd(0, w);
            }
        }
        // Crab scuttles, pausing now and then
        crabPhase += 0.14;
        if (Math.Sin(t * 0.31) > -0.25)
        {
            crabX += crabDir * 0.7;
            if (crabX < w * 0.06) crabDir = 1;
            if (crabX > w * 0.62) crabDir = -1;
        }
        NeedsDisplay = true;
    }

    static void Respawn(ref Swimmer s, bool fromLeft, double w, double h)
    {
        s.Dir = fromLeft ? 1 : -1;
        s.X = fromLeft ? -240 * s.Scale : w + 240 * s.Scale;
        s.Y = Rnd(h * 0.25, h * 0.85);
        s.Speed = s.Kind == SwimmerKind.Jellyfish ? Rnd(0.25, 0.5) : Rnd(0.8, 1.9);
        s.Scale = Rnd(0.6, 1.15);
    }

    // MARK: - Drawing

    public override void Draw(CGContext ctx)
    {
        // Water, sand and castle never change and sit under everything else,
        // so they are drawn once into a CGLayer at device resolution and
        // copied pixel-for-pixel each frame (a full-screen gradient plus
        // hundreds of speckles is costly on the CPU at 4K).
        if (!UseBackdropLayer)
        {
            DrawWater(ctx);
            DrawSand(ctx);
            DrawCastle(ctx);
        }
        else if (backdrop is null || !backdrop.IsCompatible(ctx))
        {
            backdrop?.Dispose();
            backdrop = new CGLayer(ctx, Bounds.Size);
            var bc = backdrop.Context;
            DrawWater(bc);
            DrawSand(bc);
            DrawCastle(bc);
        }
        if (UseBackdropLayer) ctx.Draw(backdrop!, CGPoint.Zero);
        foreach (var weed in weeds) DrawWeed(ctx, weed);
        DrawCrab(ctx);
        // swimmers.sorted(by: scale <) — a stable insertion sort into a reused
        // index buffer, so equal scales never swap order between frames.
        int n = swimmers.Length;
        for (int i = 0; i < n; i++)
        {
            int j = i;
            while (j > 0 && swimmers[drawOrder[j - 1]].Scale > swimmers[i].Scale)
            {
                drawOrder[j] = drawOrder[j - 1];
                j--;
            }
            drawOrder[j] = i;
        }
        for (int i = 0; i < n; i++) DrawSwimmer(ctx, swimmers[drawOrder[i]]);
        DrawBubbles(ctx);
    }

    void DrawWater(CGContext ctx)
    {
        ctx.DrawLinearGradient(waterGradient,
                               new CGPoint(0, 0),
                               new CGPoint(0, Bounds.Height),
                               CGGradientDrawingOptions.None);
    }

    void DrawSand(CGContext ctx)
    {
        ctx.SetFillColor(new CGColor(0.36, 0.33, 0.24, 1));
        ctx.Fill(new CGRect(0, 0, Bounds.Width, sandH));
        // Speckles, deterministic so they don't shimmer
        ctx.SetFillColor(new CGColor(0.45, 0.42, 0.30, 1));
        ulong seed = 0x5EED;
        int count = (int)(Bounds.Width / 6);
        for (int i = 0; i < count; i++)
        {
            seed = unchecked(seed * 6364136223846793005UL + 1442695040888963407UL);
            double sx = (double)(seed % 10000) / 10000 * Bounds.Width;
            double sy = (double)((seed >> 16) % 1000) / 1000 * (sandH - 6);
            ctx.Fill(new CGRect(sx, sy, 2.5, 2.5));
        }
    }

    static readonly (double dx, double dy)[] cubeCorners =
    [
        (0, 0), (74, 0), (0, 74), (74, 74),
    ];

    /// <summary>The aquarium castle, except it's a MOF crystal: a cubic cage on the sand.</summary>
    void DrawCastle(CGContext ctx)
    {
        double baseX = Bounds.Width * 0.74, baseY = sandH - 4;
        const double size = 74, off = 20;
        var tan = new CGColor(0.55, 0.52, 0.40, 1);
        var dimTan = new CGColor(0.38, 0.36, 0.29, 1);
        void FrameSquare(double x, double y, CGColor color, double w)
        {
            ctx.SetStrokeColor(color);
            ctx.SetLineWidth(w);
            ctx.Stroke(new CGRect(x, y, size, size));
        }
        FrameSquare(baseX + off, baseY + off * 0.7, dimTan, 2.2);     // back face
        foreach (var (dx, dy) in cubeCorners)
        {
            ctx.SetStrokeColor(dimTan);
            ctx.SetLineWidth(2.2);
            ctx.Move(new CGPoint(baseX + dx, baseY + dy));
            ctx.AddLine(new CGPoint(baseX + dx + off, baseY + dy + off * 0.7));
            ctx.StrokePath();
        }
        FrameSquare(baseX, baseY, tan, 3);                             // front face
        // Node balls on the front corners
        ctx.SetFillColor(tan);
        foreach (var (dx, dy) in cubeCorners)
        {
            ctx.FillEllipse(new CGRect(baseX + dx - 5, baseY + dy - 5, 10, 10));
        }
        // Castle door, as is traditional
        ctx.SetFillColor(new CGColor(0.02, 0.05, 0.09, 1));
        ctx.Fill(new CGRect(baseX + size / 2 - 9, baseY, 18, 22));
        ctx.FillEllipse(new CGRect(baseX + size / 2 - 9, baseY + 13, 18, 18));
    }

    static readonly CGColor oxygenRed = NSColor.CalibratedRed(0.95, 0.4, 0.32, 1);
    static readonly CGColor labelWhite = NSColor.CalibratedWhite(1, 0.95);

    /// <summary>PEG seaweed: a skeletal glycol chain — vertices zig-zagging across the
    /// swaying spine like a drawn PEG, ether oxygens every third vertex
    /// (O-C-C repeat), a terminal OH at the tip.</summary>
    void DrawWeed(CGContext ctx, Weed weed)
    {
        var pts = weedPts;
        int count = weed.Segments + 1;
        for (int k = 0; k <= weed.Segments; k++)
        {
            double sway = Math.Sin(t * 1.1 + weed.Phase + k * 0.45) * k * 1.1;
            double zig = k % 2 == 0 ? -4 : 4;
            pts[k] = new CGPoint(weed.X + sway + zig, sandH - 6 + k * 10.0);
        }
        ctx.SetStrokeColor(new CGColor(0.22, 0.65, 0.40, 0.95));
        ctx.SetLineWidth(2.6);
        ctx.SetLineJoin(CGLineJoin.Round);
        ctx.Move(pts[0]);
        for (int k = 1; k < count; k++) ctx.AddLine(pts[k]);
        ctx.StrokePath();
        ctx.SetFillColor(new CGColor(0.95, 0.4, 0.32, 1));
        for (int k = 0; k < count; k++)
        {
            if (k % 3 != 2) continue;
            var p = pts[k];
            ctx.FillEllipse(new CGRect(p.X - 2.5, p.Y - 2.5, 5, 5));
        }
        var tip = pts[count - 1];
        DrawTinyLabel(ctx, "OH", new CGPoint(tip.X, tip.Y + 8), size: 8,
                      color: oxygenRed, halo: false);
    }

    /// <summary>Ferrocene crab: Cp-ring sandwich with an iron heart, scuttling sideways.</summary>
    void DrawCrab(CGContext ctx)
    {
        ctx.SaveGState();
        ctx.TranslateBy(crabX, sandH + 16);
        var cp = new CGColor(0.85, 0.62, 0.25, 1);
        // Legs first, alternating like a proper crab
        ctx.SetStrokeColor(cp);
        ctx.SetLineWidth(2);
        for (int i = 0; i < 3; i++)
        {
            double lift = Math.Sin(crabPhase + i * 2.1) * 3;
            for (int q = 0; q < 2; q++)
            {
                double side = q == 0 ? -1 : 1;
                ctx.Move(new CGPoint(side * 10, -8));
                ctx.AddLine(new CGPoint(side * (20 + i * 6.0),
                                        -14 + (i == 1 ? lift : -lift)));
                ctx.StrokePath();
            }
        }
        // Cp rings above and below the iron
        for (int q = 0; q < 2; q++)
        {
            double dy = q == 0 ? 9 : -9;
            ctx.SetFillColor(new CGColor(0.12, 0.1, 0.05, 1));
            ctx.FillEllipse(new CGRect(-17, dy - 4.5, 34, 9));
            ctx.SetStrokeColor(cp);
            ctx.SetLineWidth(2.2);
            ctx.StrokeEllipse(new CGRect(-17, dy - 4.5, 34, 9));
        }
        ctx.SetFillColor(new CGColor(0.9, 0.42, 0.15, 1));
        ctx.FillEllipse(new CGRect(-6.5, -6.5, 13, 13));
        DrawTinyLabel(ctx, "Fe", CGPoint.Zero, size: 8, color: labelWhite, halo: false);
        ctx.RestoreGState();
    }

    void DrawBubbles(CGContext ctx)
    {
        foreach (var b in bubbles)
        {
            double x = b.X + Math.Sin(b.Wobble) * 4;
            ctx.SetStrokeColor(new CGColor(0.8, 0.92, 1.0, 0.5));
            ctx.SetLineWidth(1.2);
            ctx.StrokeEllipse(new CGRect(x - b.R, b.Y - b.R, 2 * b.R, 2 * b.R));
            ctx.SetFillColor(new CGColor(1, 1, 1, 0.35));
            ctx.FillEllipse(new CGRect(x - b.R * 0.3, b.Y + b.R * 0.2, b.R * 0.5, b.R * 0.5));
        }
    }

    static void DrawTinyLabel(CGContext ctx, string s, CGPoint p, double size, CGColor color, bool halo)
    {
        var str = new NSAttributedString(s, NSFont.BoldSystemFont(size), color);
        var sz = str.Size();
        if (halo)
        {
            ctx.SetFillColor(new CGColor(0, 0, 0, 0.7));
            ctx.FillEllipse(new CGRect(p.X - sz.Width / 2 - 1.5,
                                       p.Y - sz.Height / 2 - 0.5,
                                       sz.Width + 3, sz.Height + 1));
        }
        str.Draw(ctx, new CGPoint(p.X - sz.Width / 2, p.Y - sz.Height / 2));
    }

    // MARK: - Swimmers

    /// <summary>18-crown-6 skeletal ring: 18 vertices (O-CH2-CH2 repeating), the six
    /// oxygens tucked into notches at 0.82R so each ethylene bridge bulges
    /// outward — the classic crown scallop. Leaves the path in the context
    /// (caller strokes it) and returns the oxygen positions (in a reused buffer).</summary>
    CGPoint[] AddCrownPath(CGContext ctx, double radius, double rotation)
    {
        var oxygens = oxygenBuf;
        int no = 0;
        ctx.BeginPath();
        for (int i = 0; i < 18; i++)
        {
            double a = rotation + i * Math.PI / 9;
            double r = i % 3 == 0 ? radius * 0.82 : radius;
            var p = new CGPoint(r * Math.Cos(a), r * Math.Sin(a));
            if (i % 3 == 0) oxygens[no++] = p;
            if (i == 0) ctx.Move(p); else ctx.AddLine(p);
        }
        ctx.ClosePath();
        return oxygens;
    }

    void DrawSwimmer(CGContext ctx, in Swimmer s)
    {
        ctx.SaveGState();
        double bob = Math.Sin(s.Phase * 1.3) * 7;
        ctx.TranslateBy(s.X, s.Y + bob);
        ctx.ScaleBy(s.Scale, s.Scale);
        switch (s.Kind)
        {
            case SwimmerKind.Rotaxane: DrawRotaxane(ctx, s); break;
            case SwimmerKind.Catenane: DrawCatenane(ctx, s); break;
            case SwimmerKind.Borromean: DrawBorromean(ctx, s); break;
            case SwimmerKind.Jellyfish: DrawJellyfish(ctx, s); break;
            case SwimmerKind.Trefoil: DrawTrefoil(ctx, s); break;
            case SwimmerKind.DaisyChain: DrawDaisyChain(ctx, s); break;
            case SwimmerKind.Cucurbituril: DrawCucurbituril(ctx, s); break;
        }
        ctx.RestoreGState();
    }

    /// <summary>Bistable [2]rotaxane: dumbbell axle, green and red stations, bulky
    /// stoppers, and the blue box shuttling between stations.</summary>
    static void DrawRotaxane(CGContext ctx, in Swimmer s)
    {
        ctx.SaveGState();
        ctx.ScaleBy(s.Dir, 1);
        // Axle
        ctx.SetStrokeColor(new CGColor(0.8, 0.84, 0.9, 0.95));
        ctx.SetLineWidth(3);
        ctx.SetLineCap(CGLineCap.Round);
        ctx.Move(new CGPoint(-74, 0));
        ctx.AddLine(new CGPoint(74, 0));
        ctx.StrokePath();
        // Stations: TTF green, DNP red
        ctx.SetLineWidth(6.5);
        ctx.SetStrokeColor(new CGColor(0.25, 0.78, 0.4, 1));
        ctx.Move(new CGPoint(-42, 0)); ctx.AddLine(new CGPoint(-16, 0));
        ctx.StrokePath();
        ctx.SetStrokeColor(new CGColor(0.92, 0.45, 0.25, 1));
        ctx.Move(new CGPoint(16, 0)); ctx.AddLine(new CGPoint(42, 0));
        ctx.StrokePath();
        // Bulky stoppers
        var gray = new CGColor(0.62, 0.66, 0.74, 1);
        for (int q = 0; q < 2; q++)
        {
            double sx = q == 0 ? -1 : 1;
            ctx.SetFillColor(gray);
            ctx.FillEllipse(new CGRect(sx * 74 - 8, -8, 16, 16));
            for (int k = 0; k < 3; k++)
            {
                double dy = k == 0 ? 11 : k == 1 ? 0 : -11;
                ctx.FillEllipse(new CGRect(sx * 74 + sx * 9 - 6, dy - 6, 12, 12));
            }
        }
        // The blue box, dwelling at one station then shuttling to the other
        double p = Math.Max(-1, Math.Min(1, 1.8 * Math.Sin(s.Phase)));
        double rx = 29 * p;
        var ring = new CGRect(rx - 13, -24, 26, 48);
        ctx.SetStrokeColor(boxBlue);
        ctx.SetLineWidth(7.5);
        ctx.Stroke(ring.InsetBy(3.75, 3.75));
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.4));
        ctx.SetLineWidth(1.6);
        ctx.Stroke(ring.InsetBy(1.5, 1.5));
        ctx.RestoreGState();
    }

    /// <summary>[2]Catenane: the blue box interlocked with a crown ether, both slowly
    /// circumrotating; over-under fixed at the top crossing.</summary>
    void DrawCatenane(CGContext ctx, in Swimmer s)
    {
        double rot = s.Phase * 0.4;
        void DrawBox()
        {
            ctx.SaveGState();
            ctx.TranslateBy(-15, 0);
            ctx.RotateBy(rot);
            ctx.SetStrokeColor(boxBlue);
            ctx.SetLineWidth(7);
            ctx.Stroke(new CGRect(-22, -22, 44, 44).InsetBy(3.5, 3.5));
            ctx.RestoreGState();
        }
        void DrawCrown()
        {
            ctx.SaveGState();
            ctx.TranslateBy(15, 0);
            ctx.SetStrokeColor(crownRed);
            ctx.SetLineWidth(6);
            ctx.SetLineJoin(CGLineJoin.Round);
            var oxygens = AddCrownPath(ctx, radius: 25, rotation: -rot);
            ctx.StrokePath();
            ctx.SetFillColor(new CGColor(1, 0.85, 0.8, 1));
            foreach (var p in oxygens)
            {
                ctx.FillEllipse(new CGRect(p.X - 3, p.Y - 3, 6, 6));
            }
            ctx.RestoreGState();
        }
        DrawBox();
        DrawCrown();
        // Re-draw the box inside a clip at the upper crossing: interlocked.
        ctx.SaveGState();
        ctx.AddEllipse(new CGRect(0 - 12, 14, 24, 24));
        ctx.Clip();
        DrawBox();
        ctx.RestoreGState();
    }

    static readonly CGColor[] borroColors =
    [
        new(0.92, 0.42, 0.35, 1),
        new(0.35, 0.8, 0.45, 1),
        boxBlue,
    ];
    static readonly (int over, int under)[] borroCrossings = [(0, 2), (1, 0), (2, 1)];

    /// <summary>Borromean rings: no two rings are linked, but the three are.</summary>
    void DrawBorromean(CGContext ctx, in Swimmer s)
    {
        ctx.SaveGState();
        ctx.RotateBy(s.Phase * 0.25);
        var colors = borroColors;
        const double d = 14, r = 24;
        var centers = borroCenters;
        for (int i = 0; i < 3; i++)
        {
            double a = i * 2 * Math.PI / 3 + Math.PI / 2;
            centers[i] = new CGPoint(d * Math.Cos(a), d * Math.Sin(a));
        }
        void Ring(int i)
        {
            ctx.SetStrokeColor(colors[i]);
            ctx.SetLineWidth(5.5);
            ctx.StrokeEllipse(new CGRect(centers[i].X - r, centers[i].Y - r, 2 * r, 2 * r));
        }
        Ring(0); Ring(1); Ring(2);
        // Fix one crossing per pair (0 over 2, 1 over 0, 2 over 1) so the
        // weave alternates and the topology reads Borromean.
        foreach (var (over, under) in borroCrossings)
        {
            CGPoint ci = centers[over], cj = centers[under];
            var mid = new CGPoint((ci.X + cj.X) / 2, (ci.Y + cj.Y) / 2);
            double dx = cj.X - ci.X, dy = cj.Y - ci.Y;
            double dist = Math.Max(double.Hypot(dx, dy), 0.001);
            double h = Math.Sqrt(r * r - dist * dist / 4);
            var cross = new CGPoint(mid.X - dy / dist * h, mid.Y + dx / dist * h);
            ctx.SaveGState();
            ctx.AddEllipse(new CGRect(cross.X - 9, cross.Y - 9, 18, 18));
            ctx.Clip();
            Ring(over);
            ctx.RestoreGState();
        }
        ctx.RestoreGState();
    }

    /// <summary>18-crown-6 jellyfish: pulsing macrocycle with a potassium passenger and
    /// glycol tentacles.</summary>
    void DrawJellyfish(CGContext ctx, in Swimmer s)
    {
        double pulse = 1 + 0.13 * Math.Sin(s.Phase * 2.2);
        double r = 20 * pulse;
        // Tentacles
        ctx.SetStrokeColor(new CGColor(0.95, 0.65, 0.6, 0.75));
        ctx.SetLineWidth(1.8);
        for (int i = 0; i < 4; i++)
        {
            double bx = (i - 2) * 9.0 + 4;
            ctx.Move(new CGPoint(bx, -r * 0.7));
            double py = -r * 0.7;
            double px = bx;
            for (int k = 1; k <= 4; k++)
            {
                py -= 9;
                px = bx + Math.Sin(s.Phase * 2 + k + i) * 4;
                ctx.AddLine(new CGPoint(px, py));
            }
            ctx.StrokePath();
        }
        // Macrocycle: the real 18-crown-6 scallop, an oxygen notch at twelve
        // o'clock, swaying gently instead of spinning.
        ctx.SetStrokeColor(crownRed);
        ctx.SetLineWidth(3.6);
        ctx.SetLineJoin(CGLineJoin.Round);
        double sway = Math.PI / 2 + 0.18 * Math.Sin(s.Phase * 0.7);
        var oxygens = AddCrownPath(ctx, radius: r, rotation: sway);
        ctx.StrokePath();
        ctx.SetFillColor(new CGColor(1, 0.85, 0.8, 1));
        foreach (var p in oxygens)
        {
            ctx.FillEllipse(new CGRect(p.X - 2.8, p.Y - 2.8, 5.6, 5.6));
        }
        // The potassium passenger
        ctx.SetFillColor(new CGColor(0.6, 0.4, 0.85, 1));
        ctx.FillEllipse(new CGRect(-7, -7, 14, 14));
        DrawTinyLabel(ctx, "K⁺", CGPoint.Zero, size: 8, color: labelWhite, halo: false);
    }

    // The trefoil's knot geometry never changes (it tumbles via the context
    // rotation), so the points, the depth order and the segment colours are
    // computed once instead of every frame.
    const double trefoilR = 12.5;
    const int trefoilN = 72;
    static readonly (CGPoint p, double z)[] trefoilPts = BuildTrefoilPts();
    static readonly int[] trefoilOrder = BuildTrefoilOrder();
    static readonly CGColor[] trefoilColors = BuildTrefoilColors();

    static (CGPoint p, double z)[] BuildTrefoilPts()
    {
        var pts = new (CGPoint p, double z)[trefoilN + 1];
        for (int i = 0; i <= trefoilN; i++)
        {
            double a = (double)i / trefoilN * 2 * Math.PI;
            pts[i] = (new CGPoint((Math.Sin(a) + 2 * Math.Sin(2 * a)) * trefoilR,
                                  (Math.Cos(a) - 2 * Math.Cos(2 * a)) * trefoilR),
                      Math.Sin(3 * a));
        }
        return pts;
    }

    static int[] BuildTrefoilOrder()
    {
        var pts = trefoilPts;
        // order.sort { pts[$0].z + pts[$0+1].z < pts[$1].z + pts[$1+1].z } — stable (LINQ OrderBy).
        return Enumerable.Range(0, trefoilN)
            .OrderBy(i => pts[i].z + pts[i + 1].z)
            .ToArray();
    }

    static CGColor[] BuildTrefoilColors()
    {
        var pts = trefoilPts;
        var colors = new CGColor[trefoilN];
        for (int i = 0; i < trefoilN; i++)
        {
            double depth = (pts[i].z + pts[i + 1].z) / 2;
            double bright = 0.6 + 0.4 * (depth + 1) / 2;
            colors[i] = new CGColor(0.95 * bright, 0.78 * bright, 0.28 * bright, 1);
        }
        return colors;
    }

    /// <summary>Molecular trefoil knot: the strand weaves over and under itself, drawn
    /// with depth-sorted segments so the crossings come out right, tumbling
    /// slowly as it drifts.</summary>
    static void DrawTrefoil(CGContext ctx, in Swimmer s)
    {
        ctx.SaveGState();
        ctx.RotateBy(s.Phase * 0.35);
        var pts = trefoilPts;
        var shadow = new CGColor(0.01, 0.07, 0.13, 1);
        foreach (int i in trefoilOrder)
        {
            ctx.SetStrokeColor(shadow);
            ctx.SetLineWidth(8.5);
            ctx.SetLineCap(CGLineCap.Round);
            ctx.Move(pts[i].p); ctx.AddLine(pts[i + 1].p); ctx.StrokePath();
            ctx.SetStrokeColor(trefoilColors[i]);
            ctx.SetLineWidth(5);
            ctx.Move(pts[i].p); ctx.AddLine(pts[i + 1].p); ctx.StrokePath();
        }
        ctx.RestoreGState();
    }

    /// <summary>[c2]Daisy-chain eel: threaded ring-and-rod units undulating along.</summary>
    static void DrawDaisyChain(CGContext ctx, in Swimmer s)
    {
        ctx.SaveGState();
        ctx.ScaleBy(s.Dir, 1);
        const int units = 4;
        const double spacing = 36;
        double phase = s.Phase;
        CGPoint UnitCenter(int u) =>
            new(u * spacing - spacing * (units - 1) / 2,
                Math.Sin(phase * 1.6 + u * 0.95) * 9);
        // Rods first, threading unit to unit, with end stoppers
        ctx.SetStrokeColor(new CGColor(0.8, 0.84, 0.9, 0.95));
        ctx.SetLineWidth(3);
        ctx.SetLineCap(CGLineCap.Round);
        CGPoint head = UnitCenter(0), tail = UnitCenter(units - 1);
        ctx.Move(new CGPoint(head.X - 20, head.Y));
        ctx.AddLine(head);
        ctx.StrokePath();
        for (int u = 0; u < units - 1; u++)
        {
            ctx.Move(UnitCenter(u)); ctx.AddLine(UnitCenter(u + 1));
            ctx.StrokePath();
        }
        ctx.Move(tail);
        ctx.AddLine(new CGPoint(tail.X + 20, tail.Y));
        ctx.StrokePath();
        var gray = new CGColor(0.62, 0.66, 0.74, 1);
        ctx.SetFillColor(gray);
        ctx.FillEllipse(new CGRect(head.X - 27, head.Y - 7, 14, 14));
        ctx.FillEllipse(new CGRect(tail.X + 13, tail.Y - 7, 14, 14));
        // Rings over the rods: threaded
        for (int u = 0; u < units; u++)
        {
            var c = UnitCenter(u);
            ctx.SetStrokeColor(u % 2 == 0 ? boxBlue : crownRed);
            ctx.SetLineWidth(4.5);
            if (u % 2 == 0)
                ctx.Stroke(new CGRect(c.X - 9, c.Y - 12, 18, 24).InsetBy(2.25, 2.25));
            else
                ctx.StrokeEllipse(new CGRect(c.X - 11, c.Y - 13, 22, 26));
        }
        ctx.RestoreGState();
    }

    // The barrel outline is a fixed shape in swimmer space; build it once
    // per view (not static, so views on different threads never share it).
    readonly CGMutablePath cucurbiturilBody = BuildCucurbiturilBody();

    static CGMutablePath BuildCucurbiturilBody()
    {
        var body = new CGMutablePath();
        body.Move(new CGPoint(-13, 16));
        body.AddQuadCurve(new CGPoint(-13, -16), control: new CGPoint(-24, 0));
        body.AddLine(new CGPoint(13, -16));
        body.AddQuadCurve(new CGPoint(13, 16), control: new CGPoint(24, 0));
        body.CloseSubpath();
        return body;
    }

    static readonly double[] staveXs = [-14, -5, 5, 14];
    static readonly double[] portalXs = [-11, -4.5, 2, 8.5];

    /// <summary>Cucurbituril: the pumpkin-shaped barrel, carbonyl-lined portals top and
    /// bottom, with a shy guest inside.</summary>
    void DrawCucurbituril(CGContext ctx, in Swimmer s)
    {
        var teal = new CGColor(0.35, 0.62, 0.62, 1);
        // Barrel body: bulging staves
        ctx.SetFillColor(new CGColor(0.10, 0.24, 0.27, 0.92));
        var body = cucurbiturilBody;
        ctx.AddPath(body);
        ctx.FillPath();
        // The guest, peeking out of the cavity
        ctx.SetFillColor(new CGColor(0.6, 0.64, 0.7, 1));
        ctx.FillEllipse(new CGRect(-6, 4 + Math.Sin(s.Phase * 2) * 4, 12, 12));
        // Glycoluril staves
        ctx.SetStrokeColor(teal);
        ctx.SetLineWidth(2);
        foreach (double sx in staveXs)
        {
            ctx.Move(new CGPoint(sx * 0.8, 15));
            ctx.AddQuadCurve(new CGPoint(sx * 0.8, -15),
                             control: new CGPoint(sx * 1.45, 0));
            ctx.StrokePath();
        }
        ctx.AddPath(body);
        ctx.SetLineWidth(2.6);
        ctx.StrokePath();
        // Carbonyl portals: red oxygens rimming both openings
        ctx.SetFillColor(new CGColor(0.95, 0.4, 0.32, 1));
        for (int q = 0; q < 2; q++)
        {
            double dy = q == 0 ? 16 : -16;
            foreach (double sx in portalXs)
            {
                ctx.FillEllipse(new CGRect(sx, dy - 2.4, 4.8, 4.8));
            }
        }
    }
}
