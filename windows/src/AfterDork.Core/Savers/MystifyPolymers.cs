using AfterDork.Graphics;

namespace AfterDork.Savers;

// Mystify Origami — the Windows "Mystify" saver done properly this time:
// rigid straight-edged polygons bouncing around the screen with deep echo
// trails, except the polygons are DNA nanostructure tiles — every edge is a
// straight double helix drawn as space-filling bead chains (navy strands,
// orange sticky ends, gray base-pair rungs) meeting at green single-stranded
// connector loops, after the classic Mao/Seeman tile figures. A benzene ring
// still bounces around DVD-logo style.
// Port of MystifyPolymers/MystifyPolymers.swift.

public sealed class MystifyPolymersView : SaverView
{
    static double rnd(double lo, double hi) => Rng.Range(lo, hi);

    // A mutable value type, as in Swift: always step it in place (array
    // element or field), never through a copy.
    struct Bouncer
    {
        public CGPoint P;
        public CGVector V;

        public Bouncer(CGPoint p, CGVector v) { P = p; V = v; }

        public void Step(CGRect r)
        {
            double x = P.X + V.Dx, y = P.Y + V.Dy;
            double dx = V.Dx, dy = V.Dy;
            if (x < r.MinX) { x = r.MinX; dx = Math.Abs(dx); }
            if (x > r.MaxX) { x = r.MaxX; dx = -Math.Abs(dx); }
            if (y < r.MinY) { y = r.MinY; dy = Math.Abs(dy); }
            if (y > r.MaxY) { y = r.MaxY; dy = -Math.Abs(dy); }
            P = new CGPoint(x, y);
            V = new CGVector(dx, dy);
        }
    }

    // Swift's Poly is a struct mutated only through `polys[c]`; a class holding
    // the Bouncer array keeps every mutation in place.
    sealed class Poly
    {
        public Bouncer[] Points;
        // Oldest first; each entry is its own snapshot array (Swift's [[CGPoint]]).
        public readonly Queue<CGPoint[]> History = new();
        public double Hue;
        public int RecordCounter = 0;

        public Poly(Bouncer[] points, double hue) { Points = points; Hue = hue; }
    }

    Poly[] polys = [];
    Bouncer benzene = new(CGPoint.Zero, CGVector.Zero);
    readonly Queue<CGPoint> benzeneTrail = new();
    double benzeneHue = 0.55;
    double benzeneRot = 0;
    double t = 0;
    int historyLen = 18;

    static readonly CGColor navy = new(0.18, 0.24, 0.62, 1);
    static readonly CGColor navyDim = new(0.11, 0.15, 0.42, 1);
    static readonly CGColor orange = new(0.95, 0.58, 0.14, 1);
    static readonly CGColor orangeDim = new(0.68, 0.40, 0.10, 1);
    static readonly CGColor connectorGreen = new(0.55, 0.78, 0.28, 1);
    static readonly CGColor rungGray = new(0.82, 0.84, 0.88, 0.9);

    public MystifyPolymersView(CGRect frame, bool isPreview) : base(frame, isPreview)
    {
        AnimationTimeInterval = 1.0 / 30.0;
        Setup();
    }

    void Setup()
    {
        var r = Bounds.InsetBy(10, 10);
        double w = Math.Max(r.Width, 600), h = Math.Max(r.Height, 400);
        historyLen = Math.Max(4, Math.Min(30, (int)Settings.Value("MystifyPolymers", "echo", 18)));
        double speedMul = Math.Max(0.3, Math.Min(3, Settings.Value("MystifyPolymers", "speed", 1.0)));
        double speed = (IsPreview ? 2.6 : 5.0) * speedMul;
        // Two rigid tiles: a triangle (3-point star tile) and a quad (cross tile).
        int[] counts = [3, 4];
        polys = new Poly[counts.Length];
        for (int i = 0; i < counts.Length; i++)
        {
            var pts = new Bouncer[counts[i]];
            for (int k = 0; k < pts.Length; k++)
            {
                var b = new Bouncer(new CGPoint(rnd(10, w), rnd(10, h)), CGVector.Zero);
                double a = rnd(0, 2 * Math.PI);
                double s = rnd(0.6, 1.0) * speed;
                b.V = new CGVector(Math.Cos(a) * s, Math.Sin(a) * s);
                pts[k] = b;
            }
            polys[i] = new Poly(pts, i * 0.45 + rnd(0, 0.1));
        }
        benzene = new Bouncer(new CGPoint(w / 2, h / 2),
                              new CGVector(speed * 0.7, speed * 0.55));
    }

    public override void AnimateOneFrame()
    {
        t += 1.0 / 30.0;
        var r = Bounds.InsetBy(8, 8);
        foreach (var poly in polys)
        {
            for (int i = 0; i < poly.Points.Length; i++) poly.Points[i].Step(r);
            poly.RecordCounter += 1;
            if (poly.RecordCounter >= 3)
            {
                poly.RecordCounter = 0;
                var snap = new CGPoint[poly.Points.Length];
                for (int i = 0; i < snap.Length; i++) snap[i] = poly.Points[i].P;
                poly.History.Enqueue(snap);
                if (poly.History.Count > historyLen) poly.History.Dequeue();
            }
        }
        var ring = Bounds.InsetBy(42, 42);
        var before = benzene.V;
        benzene.Step(ring);
        if (before.Dx != benzene.V.Dx || before.Dy != benzene.V.Dy)
        {
            benzeneHue = rnd(0, 1);
        }
        benzeneRot += 0.02;
        benzeneTrail.Enqueue(benzene.P);
        if (benzeneTrail.Count > 7) benzeneTrail.Dequeue();
        NeedsDisplay = true;
    }

    public override void Draw(CGContext ctx)
    {
        ctx.SetFillColor(new CGColor(0, 0, 0, 1));
        ctx.Fill(Bounds);
        ctx.SetLineJoin(CGLineJoin.Round);
        ctx.SetLineCap(CGLineCap.Round);
        foreach (var p in polys) DrawPoly(ctx, p);
        DrawBenzene(ctx);
    }

    // MARK: - Tiles

    void DrawPoly(CGContext ctx, Poly poly)
    {
        int n = poly.History.Count;
        if (n <= 0) return;
        // The famous echo: many straight-edged wireframe ghosts in cycling hue.
        CGPoint[] pts = [];
        int i = 0;
        foreach (var snapshot in poly.History)
        {
            if (i == n - 1) { pts = snapshot; break; }
            double age = (double)(i + 1) / n;
            double hue = (poly.Hue + t * 0.03 + i * 0.018) % 1;
            var color = NSColor.CalibratedHue(hue < 0 ? hue + 1 : hue,
                                              0.9,
                                              0.5 + 0.5 * age,
                                              0.10 + 0.55 * age);
            ctx.SetStrokeColor(color);
            ctx.SetLineWidth(1.4);
            ClosedPath(ctx, snapshot);
            ctx.StrokePath();
            i++;
        }
        // The front tile: every edge is a straight DNA duplex.
        for (int k = 0; k < pts.Length; k++)
        {
            DrawEdgeDuplex(ctx, pts[k], pts[(k + 1) % pts.Length]);
        }
        // Green single-stranded connector loops at the junctions.
        ctx.SetFillColor(connectorGreen);
        foreach (var v in pts)
        {
            for (int k = 0; k < 6; k++)
            {
                double a = k * Math.PI / 3 + t * 0.8;
                var bp = new CGPoint(v.X + 7.5 * Math.Cos(a), v.Y + 7.5 * Math.Sin(a));
                ctx.FillEllipse(new CGRect(bp.X - 2.6, bp.Y - 2.6, 5.2, 5.2));
            }
        }
    }

    static void ClosedPath(CGContext ctx, CGPoint[] pts)
    {
        if (pts.Length <= 1) return;
        ctx.Move(pts[0]);
        for (int k = 1; k < pts.Length; k++) ctx.AddLine(pts[k]);
        ctx.ClosePath();
    }

    /// A straight double helix in space-filling bead style: two antiphase bead
    /// strands about the edge line, gray rungs where the groove opens, orange
    /// sticky ends near the junctions, twist animated slowly.
    void DrawEdgeDuplex(CGContext ctx, CGPoint a, CGPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Max(double.Hypot(dx, dy), 0.001);
        if (!(len > 30)) return;
        double ux = dx / len, uy = dy / len;
        double px = -uy, py = ux;
        const double period = 38;
        const double amp = 5.5;
        double twist = t * 1.6;

        CGPoint StrandPoint(double d, double sign)
        {
            double off = amp * Math.Sin(d / period * 2 * Math.PI + twist) * sign;
            return new CGPoint(a.X + ux * d + px * off, a.Y + uy * d + py * off);
        }
        // Rungs under the beads
        // (The Swift strokes each rung separately. Rungs are 7.5 apart and 2
        // wide, so they never overlap and one stroke of all of them looks the
        // same even with the translucent gray: at most ±2/255 of antialiasing
        // rounding.)
        ctx.SetStrokeColor(rungGray);
        ctx.SetLineWidth(2);
        double d = 12;
        while (d < len - 12)
        {
            double phase = d / period * 2 * Math.PI + twist;
            if (Math.Abs(Math.Sin(phase)) > 0.45)
            {
                ctx.Move(StrandPoint(d, 1));
                ctx.AddLine(StrandPoint(d, -1));
            }
            d += 7.5;
        }
        if (!ctx.IsPathEmpty) ctx.StrokePath();
        // Bead strands, front/back shaded, sticky ends in orange
        // (Kept as one fill per bead, in the Swift order: the beads are opaque
        // and overlap across colours, so the order is visible. Batching them
        // by colour/overlap layer was tried and measured no faster, because
        // rasterising the ~3k small antialiased discs dominates, not the
        // per-call overhead.)
        d = 3;
        while (d < len - 3)
        {
            double phase = d / period * 2 * Math.PI + twist;
            bool ends = d < 17 || d > len - 17;
            for (int s = 0; s < 2; s++)
            {
                double sign = s == 0 ? 1 : -1;
                bool front = Math.Cos(phase) * sign > 0;
                var color = ends ? (front ? orange : orangeDim)
                                 : (front ? navy : navyDim);
                ctx.SetFillColor(color);
                var q = StrandPoint(d, sign);
                double r = front ? 2.6 : 2.2;
                ctx.FillEllipse(new CGRect(q.X - r, q.Y - r, 2 * r, 2 * r));
            }
            d += 3.4;
        }
    }

    // MARK: - Benzene bouncer

    void DrawBenzene(CGContext ctx)
    {
        int count = benzeneTrail.Count;
        int i = 0;
        foreach (var p in benzeneTrail)
        {
            double a = 0.05 + 0.08 * i / Math.Max(count, 1);
            DrawBenzeneRing(ctx, p, 30, a, 0.7);
            i++;
        }
        DrawBenzeneRing(ctx, benzene.P, 30, 1.0, 1.0);
    }

    void DrawBenzeneRing(CGContext ctx, CGPoint p, double radius, double alpha, double lineScale)
    {
        var color = NSColor.CalibratedHue(benzeneHue, 0.75, 1.0, alpha);
        ctx.SaveGState();
        ctx.TranslateBy(p.X, p.Y);
        ctx.RotateBy(benzeneRot);
        ctx.SetStrokeColor(color);
        ctx.SetLineWidth(3 * lineScale);
        var hex = HexPath(radius);
        ctx.AddPath(hex);
        ctx.StrokePath();
        ctx.SetLineWidth(2 * lineScale);
        ctx.StrokeEllipse(new CGRect(-radius * 0.58, -radius * 0.58,
                                     radius * 1.16, radius * 1.16));
        ctx.SetFillColor(color);
        for (int k = 0; k < 6; k++)
        {
            double a = k * Math.PI / 3 + Math.PI / 6;
            var v = new CGPoint(radius * Math.Cos(a), radius * Math.Sin(a));
            double r = 3 * lineScale;
            ctx.FillEllipse(new CGRect(v.X - r, v.Y - r, 2 * r, 2 * r));
        }
        ctx.RestoreGState();
    }

    // The Swift rebuilds the hexagon every call; the radius is always 30, so
    // cache the last one built.
    CGPath? hexCache;
    double hexCacheRadius = double.NaN;

    CGPath HexPath(double radius)
    {
        if (hexCache != null && hexCacheRadius == radius) return hexCache;
        var hex = new CGMutablePath();
        for (int k = 0; k < 6; k++)
        {
            double a = k * Math.PI / 3 + Math.PI / 6;
            var v = new CGPoint(radius * Math.Cos(a), radius * Math.Sin(a));
            if (k == 0) hex.Move(v); else hex.AddLine(v);
        }
        hex.CloseSubpath();
        hexCache = hex;
        hexCacheRadius = radius;
        return hex;
    }
}
