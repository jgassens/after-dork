using System.Runtime.InteropServices;
using AfterDork.Graphics;

namespace AfterDork.Savers;

// Flying Flasks — an After Dark "Flying Toasters" homage for chemists.
// Winged Erlenmeyer flasks flap across a starfield, accompanied by drifting
// NMR tubes (the toast) and round-bottom flasks on bat wings, stir bars
// still going — the reaction waits for no one.
// Port of FlyingFlasks/FlyingFlasks.swift.

public sealed class FlyingFlasksView : SaverView
{
    static double rnd(double lo, double hi) => Rng.Range(lo, hi);

    struct Star
    {
        public double X, Y;
        public double Size;
        public double Base;
        public double Phase;
        public double Rate;
    }

    enum FlyerKind { Erlenmeyer, RoundBottom, NmrTube }

    struct Drop
    {
        public double X, Y;
        public double Vx, Vy;
        public double Hue;
        public double Size;
        public int Life;
    }

    struct Flyer
    {
        public FlyerKind Kind;
        public double X, Y;
        public double Speed;
        public double Scale;
        public double FlapPhase;
        public double FlapRate;
        public double Hue;
        public double Tilt;
        public int CapColor;
    }

    Star[] stars = [];
    Flyer[] flyers = [];
    readonly List<Drop> drops = new(64);
    bool dripsEnabled = true;
    double t = 0;
    // Classic toaster heading: down and to the left.
    readonly CGVector flightDir = new(-0.868, -0.496);
    static readonly CGColor[] capColors =
    [
        NSColor.CalibratedRed(0.85, 0.20, 0.22, 1),
        NSColor.CalibratedRed(0.20, 0.45, 0.90, 1),
        NSColor.CalibratedRed(0.25, 0.72, 0.35, 1),
        NSColor.CalibratedRed(0.92, 0.78, 0.20, 1),
    ];

    public FlyingFlasksView(CGRect frame, bool isPreview) : base(frame, isPreview)
    {
        AnimationTimeInterval = 1.0 / 30.0;
        Setup();
    }

    void Setup()
    {
        double w = Math.Max(Bounds.Width, 640), h = Math.Max(Bounds.Height, 400);
        stars = new Star[110];
        for (int i = 0; i < stars.Length; i++)
        {
            stars[i] = new Star
            {
                X = rnd(0, w), Y = rnd(0, h),
                Size = rnd(1.0, 2.6),
                Base = rnd(0.25, 0.95),
                Phase = rnd(0, 6.28),
                Rate = rnd(0.6, 2.4),
            };
        }
        int flock = (int)Settings.Value("FlyingFlasks", "flock", 15);
        dripsEnabled = Settings.Flag("FlyingFlasks", "drips", true);
        int count = IsPreview ? Math.Min(8, flock) : Math.Max(4, Math.Min(40, flock));
        // Swift would trap on a negative range here; treat it as an empty flock.
        count = Math.Max(0, count);
        var made = new Flyer[count];
        for (int i = 0; i < count; i++) made[i] = MakeFlyer(initial: true);
        // Painter's order by depth; a stable sort so equal scales can't swap.
        flyers = made.OrderBy(f => f.Scale).ToArray();
    }

    Flyer MakeFlyer(bool initial)
    {
        double w = Math.Max(Bounds.Width, 640), h = Math.Max(Bounds.Height, 400);
        double roll = Rng.Range(0, 1);
        FlyerKind kind = roll < 0.48 ? FlyerKind.Erlenmeyer : (roll < 0.82 ? FlyerKind.NmrTube : FlyerKind.RoundBottom);
        var f = new Flyer
        {
            Kind = kind,
            X = 0, Y = 0,
            Speed = rnd(2.0, 4.2),
            Scale = rnd(0.55, 1.25),
            FlapPhase = rnd(0, 6.28),
            FlapRate = rnd(10, 15),
            Hue = rnd(0, 1),
            Tilt = kind == FlyerKind.NmrTube ? rnd(-0.35, 0.05) : 0,
            CapColor = Rng.Int(0, 4),
        };
        if (kind == FlyerKind.NmrTube) f.Speed *= 0.62;        // toast drifts slower
        if (kind == FlyerKind.RoundBottom) f.FlapRate *= 0.6;  // lazier bat flaps
        if (initial)
        {
            f.X = rnd(-100, w + 100);
            f.Y = rnd(-50, h + 50);
        }
        else
        {
            Respawn(ref f);
        }
        return f;
    }

    void Respawn(ref Flyer f)
    {
        double w = Math.Max(Bounds.Width, 640), h = Math.Max(Bounds.Height, 400);
        double m = 110 * f.Scale;
        // Spawn along the top edge (extended to the right) or the right edge.
        if (Rng.Range(0, 1) < 0.62)
        {
            f.X = rnd(0, w + 500);
            f.Y = h + m;
        }
        else
        {
            f.X = w + m;
            f.Y = rnd(0, h + 200);
        }
    }

    public override void AnimateOneFrame()
    {
        t += 1.0 / 30.0;
        for (int i = 0; i < flyers.Length; i++)
        {
            double v = flyers[i].Speed * (0.5 + flyers[i].Scale);
            flyers[i].X += flightDir.Dx * v;
            flyers[i].Y += flightDir.Dy * v;
            flyers[i].FlapPhase += flyers[i].FlapRate / 30.0;
            double m = 130 * flyers[i].Scale;
            if (flyers[i].X < -m || flyers[i].Y < -m)
            {
                var nf = MakeFlyer(initial: false);
                nf.Scale = flyers[i].Scale;  // keep depth slot so draw order stays valid
                flyers[i] = nf;
            }
            // Open glassware sloshes: a drop escapes the mouth now and then.
            // NMR tubes are capped and drip nothing, obviously.
            var f = flyers[i];
            if (dripsEnabled && f.Kind != FlyerKind.NmrTube && drops.Count < 60 && Rng.Range(0, 1) < 0.02)
            {
                double v2 = f.Speed * (0.5 + f.Scale);
                drops.Add(new Drop
                {
                    X = f.X + rnd(-4, 4) * f.Scale,
                    Y = f.Y + 31 * f.Scale,
                    Vx = flightDir.Dx * v2 * 0.5 + rnd(-0.4, 0.4),
                    Vy = rnd(0.5, 1.6),
                    Hue = f.Hue,
                    Size = rnd(2.0, 3.2) * f.Scale,
                    Life = 110,
                });
            }
        }
        var ds = CollectionsMarshal.AsSpan(drops);
        for (int i = 0; i < ds.Length; i++)
        {
            ds[i].X += ds[i].Vx;
            ds[i].Y += ds[i].Vy;
            ds[i].Vy -= 0.16;  // gravity
            ds[i].Life -= 1;
        }
        drops.RemoveAll(d => d.Life <= 0 || d.Y < -20);
        NeedsDisplay = true;
    }

    public override void Draw(CGContext ctx)
    {
        // Deep-space background
        ctx.SetFillColor(new CGColor(0.012, 0.012, 0.055, 1));
        ctx.Fill(Bounds);
        DrawStars(ctx);
        DrawDrops(ctx);
        foreach (var f in flyers) DrawFlyer(ctx, f);
    }

    void DrawDrops(CGContext ctx)
    {
        foreach (var d in drops)
        {
            double fade = Math.Min(d.Life / 30.0, 1);
            var color = NSColor.CalibratedHue(d.Hue, 0.8, 0.9, 0.9 * fade);
            ctx.SetFillColor(color);
            // Teardrop: elongates as it picks up speed falling
            double stretch = Math.Min(1.0 + Math.Abs(d.Vy) * 0.12, 1.8);
            ctx.FillEllipse(new CGRect(d.X - d.Size / 2,
                                       d.Y - d.Size * stretch / 2,
                                       d.Size, d.Size * stretch));
            ctx.SetFillColor(new CGColor(1, 1, 1, 0.5 * fade));
            double hr = d.Size * 0.18;
            ctx.FillEllipse(new CGRect(d.X - d.Size * 0.15 - hr,
                                       d.Y + d.Size * 0.2 - hr,
                                       2 * hr, 2 * hr));
        }
    }

    void DrawStars(CGContext ctx)
    {
        foreach (var s in stars)
        {
            double b = s.Base * (0.55 + 0.45 * Math.Sin(t * s.Rate + s.Phase));
            ctx.SetFillColor(new CGColor(0.9, 0.92, 1.0, b));
            ctx.FillEllipse(new CGRect(s.X, s.Y, s.Size, s.Size));
        }
    }

    void DrawFlyer(CGContext ctx, in Flyer f)
    {
        ctx.SaveGState();
        ctx.TranslateBy(f.X, f.Y);
        ctx.ScaleBy(f.Scale, f.Scale);
        if (f.Tilt != 0) ctx.RotateBy(f.Tilt);
        switch (f.Kind)
        {
            case FlyerKind.Erlenmeyer: DrawErlenmeyer(ctx, f); break;
            case FlyerKind.RoundBottom: DrawRoundBottom(ctx, f); break;
            case FlyerKind.NmrTube: DrawNMRTube(ctx, f); break;
        }
        ctx.RestoreGState();
    }

    // MARK: - Wings
    // The Swift rebuilds these constant paths on every call; here they're built once.

    static readonly CGPath featherWing = FeatherWingPath();
    static readonly CGPath batWing = BatWingPath();

    static CGPath FeatherWingPath()
    {
        // Wing pointing up and outward (+x is outboard); shoulder at origin.
        var p = new CGMutablePath();
        p.Move(CGPoint.Zero);
        p.AddCurve(new CGPoint(30, 34),
                   new CGPoint(8, 16),
                   new CGPoint(24, 30));
        // Scalloped trailing edge back toward the shoulder
        p.AddQuadCurve(new CGPoint(30, 18), new CGPoint(38, 26));
        p.AddQuadCurve(new CGPoint(24, 7), new CGPoint(33, 11));
        p.AddQuadCurve(new CGPoint(14, -1), new CGPoint(24, 0));
        p.AddQuadCurve(CGPoint.Zero, new CGPoint(6, -3));
        p.CloseSubpath();
        return p;
    }

    static CGPath BatWingPath()
    {
        var p = new CGMutablePath();
        p.Move(CGPoint.Zero);
        p.AddQuadCurve(new CGPoint(34, 30), new CGPoint(14, 26));
        // Scalloped membrane
        p.AddQuadCurve(new CGPoint(26, 10), new CGPoint(34, 16));
        p.AddQuadCurve(new CGPoint(15, 8), new CGPoint(20, 3));
        p.AddQuadCurve(CGPoint.Zero, new CGPoint(6, 0));
        p.CloseSubpath();
        return p;
    }

    static readonly double[] wingSides = [1, -1];

    /// Draws both wings behind the body. `shoulder` is the +x attachment point.
    void DrawWings(CGContext ctx, double flap, CGPoint shoulder, bool bat)
    {
        var path = bat ? batWing : featherWing;
        CGColor fill = bat
            ? new CGColor(0.36, 0.18, 0.48, 0.96)
            : new CGColor(0.97, 0.97, 1.0, 0.97);
        CGColor stroke = bat
            ? new CGColor(0.75, 0.6, 0.9, 0.9)
            : new CGColor(0.55, 0.58, 0.7, 0.9);
        foreach (double side in wingSides)
        {
            ctx.SaveGState();
            ctx.ScaleBy(side, 1);
            ctx.TranslateBy(shoulder.X, shoulder.Y);
            ctx.RotateBy(flap);
            ctx.AddPath(path);
            ctx.SetFillColor(fill);
            ctx.FillPath();
            ctx.AddPath(path);
            ctx.SetStrokeColor(stroke);
            ctx.SetLineWidth(1.4);
            ctx.StrokePath();
            ctx.RestoreGState();
        }
    }

    // MARK: - Bodies

    static readonly CGPath erlenmeyerBody = ErlenmeyerBody();

    static CGPath ErlenmeyerBody()
    {
        var p = new CGMutablePath();
        p.Move(new CGPoint(-20, -28));
        p.AddLine(new CGPoint(20, -28));
        p.AddLine(new CGPoint(7, 6));
        p.AddLine(new CGPoint(7, 26));
        p.AddLine(new CGPoint(9, 28));
        p.AddLine(new CGPoint(9, 31));
        p.AddLine(new CGPoint(-9, 31));
        p.AddLine(new CGPoint(-9, 28));
        p.AddLine(new CGPoint(-7, 26));
        p.AddLine(new CGPoint(-7, 6));
        p.CloseSubpath();
        return p;
    }

    static readonly double[] poses = [0.95, 0.45, -0.22, 0.45];

    /// The After Dark toasters didn't glide — they snapped through a 4-pose
    /// mechanical flap cycle (up, mid, down, mid) with no smoothing.
    static double ToasterFlap(double phase)
    {
        return poses[(int)((long)(phase / (Math.PI / 2)) % 4)];
    }

    void DrawErlenmeyer(CGContext ctx, in Flyer f)
    {
        double flap = ToasterFlap(f.FlapPhase);
        DrawWings(ctx, flap, new CGPoint(11, 6), bat: false);

        var body = erlenmeyerBody;
        // Glass fill
        ctx.AddPath(body);
        ctx.SetFillColor(new CGColor(0.85, 0.9, 1.0, 0.13));
        ctx.FillPath();
        // Liquid
        ctx.SaveGState();
        ctx.AddPath(body);
        ctx.Clip();
        var liquid = NSColor.CalibratedHue(f.Hue, 0.75, 0.85, 0.92);
        ctx.SetFillColor(liquid);
        ctx.Fill(new CGRect(-22, -30, 44, 22));
        ctx.SetFillColor(NSColor.CalibratedHue(f.Hue, 0.6, 1.0, 0.9));
        ctx.FillEllipse(new CGRect(-16, -10.5, 32, 5));
        ctx.RestoreGState();
        // Outline + highlight
        ctx.AddPath(body);
        ctx.SetStrokeColor(new CGColor(0.92, 0.95, 1.0, 0.9));
        ctx.SetLineWidth(2);
        ctx.SetLineJoin(CGLineJoin.Round);
        ctx.StrokePath();
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.55));
        ctx.SetLineWidth(1.6);
        ctx.Move(new CGPoint(-13, -22));
        ctx.AddLine(new CGPoint(-5, 0));
        ctx.StrokePath();
    }

    static readonly CGPath roundBottomBody = RoundBottomBody();

    static CGPath RoundBottomBody()
    {
        var body = new CGMutablePath();
        body.AddEllipse(new CGRect(-18, -24, 36, 36));
        body.AddRect(new CGRect(-5, 8, 10, 20));
        body.AddRect(new CGRect(-7, 28, 14, 3.5));
        return body;
    }

    void DrawRoundBottom(CGContext ctx, in Flyer f)
    {
        double flap = ToasterFlap(f.FlapPhase);
        DrawWings(ctx, flap, new CGPoint(13, 2), bat: true);

        var body = roundBottomBody;

        ctx.AddPath(body);
        ctx.SetFillColor(new CGColor(0.85, 0.9, 1.0, 0.13));
        ctx.FillPath();
        // Liquid inside the bulb
        ctx.SaveGState();
        ctx.AddEllipse(new CGRect(-18, -24, 36, 36));
        ctx.Clip();
        var liquid = NSColor.CalibratedHue(f.Hue, 0.75, 0.8, 0.92);
        ctx.SetFillColor(liquid);
        ctx.Fill(new CGRect(-18, -24, 36, 16));
        // Stir bar going, mid-flight: its apparent length breathes as it
        // whirls, seen side-on.
        double spin = Math.Cos(t * 9 + f.FlapPhase);
        double half = 11 * Math.Max(Math.Abs(spin), 0.2);
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.95));
        ctx.SetLineWidth(4);
        ctx.SetLineCap(CGLineCap.Round);
        ctx.Move(new CGPoint(-half, -17));
        ctx.AddLine(new CGPoint(half, -17));
        ctx.StrokePath();
        // Vortex dimple in the surface while the bar spins
        ctx.SetFillColor(new CGColor(0.012, 0.012, 0.055, 0.35));
        ctx.FillEllipse(new CGRect(-6, -10, 12, 4));
        ctx.RestoreGState();
        // Outlines
        ctx.SetStrokeColor(new CGColor(0.92, 0.95, 1.0, 0.9));
        ctx.SetLineWidth(2);
        ctx.StrokeEllipse(new CGRect(-18, -24, 36, 36));
        ctx.Stroke(new CGRect(-5, 8, 10, 20));
        ctx.Stroke(new CGRect(-7, 28, 14, 3.5));
        // Glass glint
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.55));
        ctx.SetLineWidth(1.6);
        ctx.AddArc(new CGPoint(0, -6), 13, 2.4, 3.4, clockwise: false);
        ctx.StrokePath();
    }

    static readonly CGPath nmrTube = CGPath.RoundedRect(new CGRect(-4, -30, 8, 58), 3, 3);

    void DrawNMRTube(CGContext ctx, in Flyer f)
    {
        var tube = nmrTube;
        ctx.AddPath(tube);
        ctx.SetFillColor(new CGColor(0.85, 0.9, 1.0, 0.14));
        ctx.FillPath();
        // Sample solution
        ctx.SaveGState();
        ctx.AddPath(tube);
        ctx.Clip();
        ctx.SetFillColor(NSColor.CalibratedHue(f.Hue, 0.8, 0.85, 0.95));
        ctx.Fill(new CGRect(-4, -30, 8, 17));
        ctx.RestoreGState();
        // Outline
        ctx.AddPath(tube);
        ctx.SetStrokeColor(new CGColor(0.92, 0.95, 1.0, 0.85));
        ctx.SetLineWidth(1.6);
        ctx.StrokePath();
        // Colored cap
        var cap = capColors[f.CapColor];
        ctx.SetFillColor(cap);
        ctx.Fill(new CGRect(-5.5, 26, 11, 7));
        ctx.SetStrokeColor(new CGColor(0, 0, 0, 0.35));
        ctx.SetLineWidth(1);
        ctx.Stroke(new CGRect(-5.5, 26, 11, 7));
    }
}
