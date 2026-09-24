using System.Runtime.InteropServices;
using AfterDork.Graphics;

namespace AfterDork.Savers;

// Schlenk Pipes — the Windows "3D Pipes" saver rebuilt as an air-free
// plumbing nightmare: glass tubing grows across the screen joint by joint,
// sprouting ground-glass collars, stopcocks, manifold take-offs, and the
// occasional condenser coil; runs retire into Schlenk flasks, oil bubblers,
// and cold traps, until the hood is full and everything gets flushed.
// Port of GlasswarePipes/GlasswarePipes.swift.

public sealed class GlasswarePipesView : SaverView
{
    static double Rnd(double lo, double hi) => Rng.Range(lo, hi);

    struct Camera
    {
        public V3 Eye = new(0, 0, 10);
        public V3 Fwd = new(0, 0, -1);
        public V3 Right = new(1, 0, 0);
        public V3 Up = new(0, 1, 0);
        public double Fl = 800;
        public double Cx = 0, Cy = 0;

        public Camera(V3 target, double azimuth, double elevation, double radius,
                      double viewW, double viewH)
        {
            Eye = target + new V3(radius * Math.Cos(elevation) * Math.Cos(azimuth),
                                  radius * Math.Sin(elevation),
                                  radius * Math.Cos(elevation) * Math.Sin(azimuth));
            Fwd = V3.Normalize(target - Eye);
            Right = V3.Normalize(V3.Cross(Fwd, new V3(0, 1, 0)));
            Up = V3.Cross(Right, Fwd);
            Fl = viewH * 1.25;
            Cx = viewW / 2;
            Cy = viewH / 2;
        }

        public readonly (CGPoint pt, double z) Project(V3 p)
        {
            var d = p - Eye;
            double z = V3.Dot(d, Fwd);
            double zc = Math.Max(z, 0.6);
            double x = V3.Dot(d, Right);
            double y = V3.Dot(d, Up);
            return (new CGPoint(Cx + Fl * x / zc, Cy + Fl * y / zc), z);
        }
    }

    struct DrawItem
    {
        // 0 tube, 1 ball joint, 2 ground-glass collar, 3 condenser,
        // 4 clamped ball, 5 cold trap, 6 stopcock, 7 Schlenk flask, 8 bubbler,
        // 9 manifold take-off with flask
        public int Kind;
        public double Depth;
        public CGPoint P1;
        public CGPoint P2;
        public double W;
        public int Color;
        public CGPoint[] Coil;
        // Insertion order: breaks depth ties so the sort is stable (no flicker).
        public long Seq;
        // Layer-cache bookkeeping (see UpdateLayer): the item's painted
        // footprint and the envelopes of its animated parts.
        public CGRect Box, AnimBox, AnimBox2;
    }

    struct Pipe
    {
        public I3 Head;
        public I3 Dir;
        public int Color;
        public double Progress;
        public bool Alive;
    }

    const int nx = 13, ny = 9, nz = 13;
    readonly HashSet<int> occupied = new();
    readonly List<DrawItem> items = new();
    bool needSort = false;
    readonly List<Pipe> pipes = new();
    Camera camera = new(V3.Zero, 0, 0.3, 14, 1280, 720);
    int deadStarts = 0;
    double fade = -1;  // >=0 means flushing
    long tick = 0;
    long itemSeq = 0;

    static readonly (double, double, double)[] tints =
    [
        (0.88, 0.93, 0.97),  // borosilicate clear
        (0.85, 0.62, 0.25),  // amber
        (0.45, 0.75, 0.95),  // cool blue
        (0.55, 0.86, 0.62),  // green
        (0.9, 0.55, 0.75),   // rhodamine pink
    ];
    static readonly CGColor[] keckColors =
    [
        new(0.15, 0.45, 0.95, 1),  // keck blue
        new(0.2, 0.8, 0.35, 1),    // keck green
        new(1.0, 0.8, 0.15, 1),    // keck yellow
        new(0.95, 0.45, 0.15, 1),  // keck orange
    ];
    static readonly I3[] dirs =
    [
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1),
    ];

    // Dark fume-hood interior (built once rather than every frame).
    static readonly CGGradient hoodGradient = new(
        [new CGColor(0.05, 0.06, 0.09, 1), new CGColor(0.01, 0.015, 0.03, 1)], [0, 1]);

    // Reused by chooseDir instead of allocating a filtered array.
    readonly List<I3> dirOptions = new(6);

    public GlasswarePipesView(CGRect frame, bool isPreview) : base(frame, isPreview)
    {
        AnimationTimeInterval = 1.0 / 30.0;
        ResetScene();
    }

    static int CellIndex(I3 c) => (c.X * ny + c.Y) * nz + c.Z;
    static bool InBounds(I3 c) =>
        c.X >= 0 && c.X < nx && c.Y >= 0 && c.Y < ny && c.Z >= 0 && c.Z < nz;
    static V3 World(I3 c) => new(c.X, c.Y, c.Z);

    double speedMul = 1.0;
    bool fancyOn = true;

    void ResetScene()
    {
        speedMul = Math.Max(0.3, Math.Min(3, Settings.Value("GlasswarePipes", "speed", 1.0)));
        // Settings key kept as "alembics" for compatibility; it now gates
        // all the fancy glassware.
        fancyOn = Settings.Flag("GlasswarePipes", "alembics", true);
        occupied.Clear();
        items.Clear();
        pipes.Clear();
        deadStarts = 0;
        fade = -1;
        sceneGen += 1;  // the layer cache must be rebuilt from scratch
        var target = new V3((nx - 1) / 2.0, (ny - 1) / 2.0, (nz - 1) / 2.0);
        camera = new Camera(target,
                            azimuth: Rnd(0, 2 * Math.PI),
                            elevation: Rnd(0.18, 0.5),
                            radius: Math.Max(nx, nz) * 1.35,
                            viewW: Math.Max(Bounds.Width, 640),
                            viewH: Math.Max(Bounds.Height, 400));
        for (int i = 0; i < 3; i++) SpawnPipe();
    }

    void SpawnPipe()
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            var c = new I3(Rng.Int(0, nx), Rng.Int(0, ny), Rng.Int(0, nz));
            if (!occupied.Contains(CellIndex(c)))
            {
                occupied.Add(CellIndex(c));
                pipes.Add(new Pipe
                {
                    Head = c, Dir = Rng.Element(dirs),
                    Color = Rng.Int(0, tints.Length), Progress = 0, Alive = true,
                });
                return;
            }
        }
        deadStarts = 99;  // grid effectively full
    }

    I3? ChooseDir(I3 c, I3 current)
    {
        var straight = c + current;
        if (InBounds(straight) && !occupied.Contains(CellIndex(straight)) && Rnd(0, 1) < 0.55)
            return current;
        dirOptions.Clear();
        foreach (var d in dirs)
        {
            var n = c + d;
            if (InBounds(n) && !occupied.Contains(CellIndex(n))) dirOptions.Add(d);
        }
        if (dirOptions.Count == 0) return null;
        return Rng.Element(dirOptions);
    }

    public override void AnimateOneFrame()
    {
        tick += 1;
        if (fade >= 0)
        {
            fade += 1.0 / 45.0;
            if (fade >= 1) ResetScene();
            NeedsDisplay = true;
            return;
        }
        var span = CollectionsMarshal.AsSpan(pipes);
        for (int i = 0; i < span.Length; i++)
        {
            if (!span[i].Alive) continue;
            span[i].Progress += 0.2 * speedMul;
            if (span[i].Progress >= 1)
            {
                span[i].Progress = 0;
                FinishSegment(ref span[i]);
            }
        }
        pipes.RemoveAll(p => !p.Alive);
        while (pipes.Count < 3 && deadStarts < 12) SpawnPipe();
        double full = (double)occupied.Count / (nx * ny * nz);
        if (full > 0.62 || deadStarts >= 12) fade = 0;
        if (needSort)
        {
            // Far to near; ties keep insertion order (stable).
            items.Sort(static (a, b) =>
            {
                int c = b.Depth.CompareTo(a.Depth);
                return c != 0 ? c : a.Seq.CompareTo(b.Seq);
            });
            needSort = false;
        }
        NeedsDisplay = true;
    }

    void FinishSegment(ref Pipe p)
    {
        var from = p.Head;
        var to = from + p.Dir;
        occupied.Add(CellIndex(to));
        AddTube(World(from), World(to), p.Color);
        p.Head = to;
        if (ChooseDir(to, p.Dir) is not I3 nd)
        {
            // Stuck: retire the pipe into a piece of end glassware.
            AddTerminator(World(to), p.Color);
            p.Alive = false;
            deadStarts += 1;
            return;
        }
        if (fancyOn && Rnd(0, 1) < 0.03)
        {
            // Deliberate retirement: the run ends in fancy glassware.
            AddTerminator(World(to), p.Color, fancyOnly: true);
            p.Alive = false;
            return;
        }
        if (nd != p.Dir)
        {
            // Elbow ball joint, some held by a pinch clamp.
            AddBall(World(to), p.Color, r: 0.34, clamped: Rnd(0, 1) < 0.3);
        }
        else
        {
            // Hardware on a straight run: ground-glass joint, stopcock, or —
            // on horizontal runs — a manifold take-off with a flask plumbed in.
            double roll = Rnd(0, 1);
            if (roll < 0.10)
                AddCollar(World(to), nd);
            else if (fancyOn && roll < 0.18)
                AddStopcock(World(to), nd);
            else if (fancyOn && nd.Y == 0 && roll < 0.26)
                AddManifold(World(to), nd);
        }
        p.Dir = nd;
    }

    /// <summary>End-of-run glassware: Schlenk flask, oil bubbler, cold trap on a
    /// vacuum pump, or a plain ball-joint cap.</summary>
    void AddTerminator(V3 p, int color, bool fancyOnly = false)
    {
        if (!fancyOn)
        {
            AddBall(p, color, r: 0.30, clamped: false);
            return;
        }
        double roll = Rnd(0, 1);
        if (roll < 0.30)
            AddGlass(7, p, color);
        else if (roll < 0.55)
            AddGlass(8, p, color);
        else if (roll < 0.80 || fancyOnly)
            AddGlass(5, p, color);
        else
            AddBall(p, color, r: 0.30, clamped: false);
    }

    // MARK: - Display list

    void Append(DrawItem item)
    {
        item.Seq = itemSeq++;
        ComputeBoxes(ref item);
        items.Add(item);
    }

    void AddTube(V3 a, V3 b, int color)
    {
        var (p1, z1) = camera.Project(a);
        var (p2, z2) = camera.Project(b);
        double zm = (z1 + z2) / 2;
        var item = new DrawItem
        {
            Kind = 0, Depth = zm, P1 = p1, P2 = p2,
            W = 0.30 * camera.Fl / Math.Max(zm, 0.6), Color = color, Coil = [],
        };
        if (Math.Abs(a.Y - b.Y) < 0.01 && Rnd(0, 1) < 0.07)
        {
            // Horizontal run becomes a condenser: precompute the coil.
            item.Kind = 3;
            var axis = b - a;
            var perp1 = V3.Cross(axis, new V3(0, 1, 0));
            if (V3.Length(perp1) < 0.01) perp1 = new V3(1, 0, 0);
            perp1 = V3.Normalize(perp1);
            var perp2 = V3.Normalize(V3.Cross(axis, perp1));
            var coil = new CGPoint[41];
            const double turns = 5.0;
            for (int k = 0; k <= 40; k++)
            {
                double s = k / 40.0;
                double angle = s * turns * 2 * Math.PI;
                var p = a + axis * s + perp1 * (0.26 * Math.Cos(angle)) + perp2 * (0.26 * Math.Sin(angle));
                coil[k] = camera.Project(p).pt;
            }
            item.Coil = coil;
        }
        Append(item);
        needSort = true;
    }

    void AddBall(V3 p, int color, double r, bool clamped)
    {
        var (pt, z) = camera.Project(p);
        Append(new DrawItem
        {
            Kind = clamped ? 4 : 1, Depth = z - 0.01, P1 = pt,
            W = r * 2 * camera.Fl / Math.Max(z, 0.6), Color = color, Coil = [],
        });
        needSort = true;
    }

    void AddManifold(V3 p, I3 dir)
    {
        var d = new V3(dir.X, dir.Y, dir.Z) * 0.5;
        var (p1, z1) = camera.Project(p - d);
        var (p2, z2) = camera.Project(p + d);
        double zm = (z1 + z2) / 2 - 0.015;
        Append(new DrawItem
        {
            Kind = 9, Depth = zm, P1 = p1, P2 = p2,
            W = 0.30 * camera.Fl / Math.Max(zm, 0.6),
            Color = Rng.Int(0, tints.Length), Coil = [],
        });
        needSort = true;
    }

    void AddGlass(int kind, V3 p, int color)
    {
        var (pt, z) = camera.Project(p);
        Append(new DrawItem
        {
            Kind = kind, Depth = z - 0.02, P1 = pt,
            W = 0.5 * camera.Fl / Math.Max(z, 0.6), Color = color, Coil = [],
        });
        needSort = true;
    }

    void AddStopcock(V3 p, I3 dir)
    {
        var d = new V3(dir.X, dir.Y, dir.Z) * 0.3;
        var (p1, z1) = camera.Project(p - d);
        var (p2, z2) = camera.Project(p + d);
        double zm = (z1 + z2) / 2 - 0.015;
        Append(new DrawItem
        {
            Kind = 6, Depth = zm, P1 = p1, P2 = p2,
            W = 0.30 * camera.Fl / Math.Max(zm, 0.6), Color = 0, Coil = [],
        });
        needSort = true;
    }

    void AddCollar(V3 p, I3 dir)
    {
        var d = new V3(dir.X, dir.Y, dir.Z) * 0.22;
        var (p1, z1) = camera.Project(p - d);
        var (p2, z2) = camera.Project(p + d);
        double zm = (z1 + z2) / 2 - 0.01;
        // color doubles as the Keck clip color for collars
        Append(new DrawItem
        {
            Kind = 2, Depth = zm, P1 = p1, P2 = p2,
            W = 0.42 * camera.Fl / Math.Max(zm, 0.6),
            Color = Rng.Int(0, keckColors.Length), Coil = [],
        });
        needSort = true;
    }

    // MARK: - Drawing

    public override void Draw(CGContext ctx)
    {
        if (UseLayerCache)
        {
            // Background + display list come from the cached layer (see
            // UpdateLayer); the result is pixel-identical to drawing them here.
            UpdateLayer(ctx);
            ctx.Draw(layer!, CGPoint.Zero);
            ctx.SetLineCap(CGLineCap.Round);
        }
        else
        {
            DrawHood(ctx);
            ctx.SetLineCap(CGLineCap.Round);
            var span = CollectionsMarshal.AsSpan(items);
            for (int i = 0; i < span.Length; i++) DrawItemAt(ctx, in span[i]);
        }
        foreach (var p in pipes)
        {
            if (!(p.Alive && p.Progress > 0.02)) continue;
            var a = World(p.Head);
            var b = a + new V3(p.Dir.X, p.Dir.Y, p.Dir.Z) * p.Progress;
            var (p1, z1) = camera.Project(a);
            var (p2, z2) = camera.Project(b);
            double zm = (z1 + z2) / 2;
            DrawCapsule(ctx, p1, p2, w: 0.30 * camera.Fl / Math.Max(zm, 0.6),
                        color: p.Color, depth: zm);
        }
        if (fade >= 0)
        {
            ctx.SetFillColor(new CGColor(0.01, 0.015, 0.03, Math.Min(fade * 1.2, 1)));
            ctx.Fill(Bounds);
        }
    }

    void DrawHood(CGContext ctx)
    {
        // Dark fume-hood interior
        ctx.DrawRadialGradient(hoodGradient,
                               new CGPoint(Bounds.MidX, Bounds.MidY), 0,
                               new CGPoint(Bounds.MidX, Bounds.MidY),
                               Math.Max(Bounds.Width, Bounds.Height) * 0.75,
                               CGGradientDrawingOptions.DrawsAfterEndLocation);
    }

    // MARK: - Layer cache (Windows only)
    //
    // A full hood holds thousands of anti-aliased strokes, far too many to
    // rasterise from scratch 30 times a second on the CPU. The background and
    // display list are therefore kept in a CGLayer at device resolution and
    // repaired each frame only where something changed: the footprint of each
    // item added since the last update, plus the fixed envelope inside which
    // each animated item (stir bars, bubbles, steam, the buzzing pump) moves.
    // Repair clips (without anti-aliasing, so every pixel is either untouched
    // or fully recomputed) to those rectangles and repaints the background and
    // every item whose footprint touches them, in display-list order — so
    // each repaired pixel gets exactly the value a full redraw would give it.

    /// <summary>Tests turn this off to compare against the plain full redraw.</summary>
    internal static bool UseLayerCache = true;
    internal int ItemCount => items.Count;

    CGLayer? layer;
    int sceneGen = 0, layerGen = -1;
    long layerSeq = 0;
    readonly List<CGRect> dirty = new();

    void UpdateLayer(CGContext ctx)
    {
        if (layer is null || !layer.IsCompatible(ctx))
        {
            layer?.Dispose();
            layer = new CGLayer(ctx, Bounds.Size);
            layerGen = -1;
        }
        var lc = layer.Context;
        var span = CollectionsMarshal.AsSpan(items);
        bool full = layerGen != sceneGen;
        dirty.Clear();
        double ux0 = double.MaxValue, uy0 = double.MaxValue, ux1 = double.MinValue, uy1 = double.MinValue;
        void AddDirty(CGRect r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            dirty.Add(r);
            ux0 = Math.Min(ux0, r.MinX); uy0 = Math.Min(uy0, r.MinY);
            ux1 = Math.Max(ux1, r.MaxX); uy1 = Math.Max(uy1, r.MaxY);
        }
        if (!full)
        {
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Seq >= layerSeq) AddDirty(span[i].Box);
                AddDirty(span[i].AnimBox);
                AddDirty(span[i].AnimBox2);
            }
            if (dirty.Count == 0) return;
            if (dirty.Count > 600) full = true;
        }
        if (full)
        {
            lc.SaveGState();
            DrawHood(lc);
            lc.SetLineCap(CGLineCap.Round);
            for (int i = 0; i < span.Length; i++) DrawItemAt(lc, in span[i]);
            lc.RestoreGState();
        }
        else
        {
            // Items are drawn into the scratch layer without the dirty clip:
            // Skia picks its anti-aliasing rasteriser by comparing a path's
            // bounds with the clip bounds, so a path cut by a tighter clip
            // could come out a shade different from a full redraw. Only the
            // dirty pixels (all background + every item that touches them)
            // are then copied across.
            if (scratch is null || !scratch.IsCompatible(ctx))
            {
                scratch?.Dispose();
                scratch = new CGLayer(ctx, Bounds.Size);
            }
            var sc = scratch.Context;
            sc.SaveGState();
            ClipToDirty(sc);
            DrawHood(sc);
            sc.RestoreGState();
            sc.SaveGState();
            sc.SetLineCap(CGLineCap.Round);
            for (int i = 0; i < span.Length; i++)
            {
                var b = span[i].Box;
                if (b.MaxX < ux0 || b.MinX > ux1 || b.MaxY < uy0 || b.MinY > uy1) continue;
                bool hit = false;
                foreach (var r in dirty)
                {
                    if (b.MaxX >= r.MinX && b.MinX <= r.MaxX && b.MaxY >= r.MinY && b.MinY <= r.MaxY)
                    {
                        hit = true;
                        break;
                    }
                }
                if (hit) DrawItemAt(sc, in span[i]);
            }
            sc.RestoreGState();
            lc.SaveGState();
            ClipToDirty(lc);
            lc.Draw(scratch, CGPoint.Zero);
            lc.RestoreGState();
        }
        layerGen = sceneGen;
        layerSeq = itemSeq;
    }

    CGLayer? scratch;

    /// <summary>Clips to the union of the dirty rects, aliased so each pixel is wholly in or out.</summary>
    void ClipToDirty(CGContext c)
    {
        c.SetShouldAntialias(false);
        foreach (var r in dirty) c.AddRect(r);
        c.Clip();
        c.SetShouldAntialias(true);
    }

    static CGRect Around(double x, double y, double rx, double ry) =>
        new(x - rx, y - ry, 2 * rx, 2 * ry);

    static CGRect Span(CGPoint a, CGPoint b, double pad) =>
        new(Math.Min(a.X, b.X) - pad, Math.Min(a.Y, b.Y) - pad,
            Math.Abs(a.X - b.X) + 2 * pad, Math.Abs(a.Y - b.Y) + 2 * pad);

    /// <summary>
    /// Conservative footprint of everything DrawItemAt can paint for the item
    /// (Box), and the tick-independent envelopes of its animated parts
    /// (AnimBox, AnimBox2). Each bound covers the widest stroke, round caps,
    /// the minimum line widths, and a few pixels of anti-aliasing.
    /// </summary>
    void ComputeBoxes(ref DrawItem item)
    {
        const double pad = 3;
        double w = item.W, s = item.W;
        var p = item.P1;
        item.AnimBox = CGRect.Zero;
        item.AnimBox2 = CGRect.Zero;
        switch (item.Kind)
        {
            case 0:
                item.Box = Span(item.P1, item.P2, w / 2 + pad);
                break;
            case 1:
                item.Box = Around(p.X, p.Y, w / 2 + pad, w / 2 + pad);
                break;
            case 4:
                // The pinch clamp's tabs, stem and wing nut reach ~1.8r from the centre.
                item.Box = Around(p.X, p.Y, w + pad, w + pad);
                break;
            case 2:
            {
                double len = double.Hypot(item.P2.X - item.P1.X, item.P2.Y - item.P1.Y);
                item.Box = Span(item.P1, item.P2, 0.2 * len + 0.8 * w + pad);
                break;
            }
            case 3:
            {
                var b = Span(item.P1, item.P2, 0);
                double x0 = b.MinX, y0 = b.MinY, x1 = b.MaxX, y1 = b.MaxY;
                foreach (var c in item.Coil)
                {
                    x0 = Math.Min(x0, c.X); y0 = Math.Min(y0, c.Y);
                    x1 = Math.Max(x1, c.X); y1 = Math.Max(y1, c.Y);
                }
                double e = 1.05 * w + pad;
                item.Box = new CGRect(x0 - e, y0 - e, x1 - x0 + 2 * e, y1 - y0 + 2 * e);
                break;
            }
            case 6:
            {
                var pc = new CGPoint((item.P1.X + item.P2.X) / 2, (item.P1.Y + item.P2.Y) / 2);
                item.Box = Around(pc.X, pc.Y, 1.7 * w + pad, 1.7 * w + pad);
                break;
            }
            case 9:
            {
                var pc = new CGPoint((item.P1.X + item.P2.X) / 2, (item.P1.Y + item.P2.Y) / 2);
                item.Box = Around(pc.X, pc.Y, 3.75 * w + pad, 3.75 * w + pad);
                if (w > 5)
                {
                    // Stir bar: same geometry as DrawManifold.
                    double dx = item.P2.X - item.P1.X, dy = item.P2.Y - item.P1.Y;
                    double len = Math.Max(double.Hypot(dx, dy), 0.001);
                    var u = new CGVector(dx / len, dy / len);
                    var v = new CGVector(-u.Dy, u.Dx);
                    if (v.Dy > 0) v = new CGVector(-v.Dx, -v.Dy);
                    var fc = new CGPoint(pc.X + v.Dx * w * 2.55, pc.Y + v.Dy * w * 2.55);
                    double r = w * 0.95;
                    double hw = Math.Max(r * 0.17, 1.2) / 2;
                    item.AnimBox = Around(fc.X, fc.Y - r * 0.6, r * 0.45 + hw + pad, hw + pad);
                }
                break;
            }
            case 7:
            {
                item.Box = new CGRect(p.X - 1.15 * s - pad, p.Y - 2.5 * s - pad,
                                      2.5 * s + 2 * pad, 2.95 * s + 2 * pad);
                double hw = Math.Max(s * 0.16, 1.4) / 2;
                item.AnimBox = Around(p.X, p.Y - s * 1.35 - s * 0.62, s * 0.42 + hw + pad, hw + pad);
                break;
            }
            case 8:
                item.Box = new CGRect(p.X - 0.6 * s - pad, p.Y - 2.35 * s - pad,
                                      1.2 * s + 2 * pad, 2.65 * s + 2 * pad);
                if (s > 5)
                {
                    // Bubble column: x in p.x + s(0.07…0.25) ± 0.1s, y in p.y − s(1.65…0.3).
                    item.AnimBox = new CGRect(p.X - 0.03 * s - pad, p.Y - 1.65 * s - pad,
                                              0.38 * s + 2 * pad, 1.35 * s + 2 * pad);
                }
                break;
            case 5:
            {
                item.Box = new CGRect(p.X - 1.1 * s - pad, p.Y - 2.6 * s - pad,
                                      3.8 * s + 2 * pad, 2.85 * s + 2 * pad);
                // The buzzing pump and the arm's end that follows it.
                double vib = s * 0.025;
                double pcx = p.X + s * 1.55, pcy = p.Y - s * 1.7;
                item.AnimBox = new CGRect(pcx - 0.3 * s - vib - pad, pcy - 0.45 * s - vib - pad,
                                          1.3 * s + 2 * vib + 2 * pad, 1.87 * s + 2 * vib + 2 * pad);
                // Steam wisps above the dewar rim.
                double rimY = p.Y - s * 1.35;
                item.AnimBox2 = new CGRect(p.X - 1.0 * s - pad, rimY + 0.04 * s - pad,
                                           2.0 * s + 2 * pad, 1.18 * s + 2 * pad);
                break;
            }
            default:
                item.Box = CGRect.Zero;
                break;
        }
    }

    static CGColor Shade(int color, double mul, double alpha, double depth)
    {
        var t = tints[color];
        double dim = Math.Max(0.35, Math.Min(1.0, 1.35 - depth / 26.0));  // depth cueing
        return new CGColor(t.Item1 * mul * dim, t.Item2 * mul * dim,
                           t.Item3 * mul * dim, alpha);
    }

    static void DrawCapsule(CGContext ctx, CGPoint p1, CGPoint p2,
                            double w, int color, double depth)
    {
        // Dark rim, bright core, specular streak: cheap fake cylinder shading.
        ctx.SetStrokeColor(Shade(color, 0.42, alpha: 1, depth: depth));
        ctx.SetLineWidth(w);
        ctx.Move(p1); ctx.AddLine(p2); ctx.StrokePath();
        ctx.SetStrokeColor(Shade(color, 0.85, alpha: 1, depth: depth));
        ctx.SetLineWidth(w * 0.66);
        ctx.Move(p1); ctx.AddLine(p2); ctx.StrokePath();
        // Specular highlight offset toward the light (up-left).
        double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        double len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.001);
        dx /= len; dy /= len;
        double ox = -dy, oy = dx;
        if (ox * (-0.5) + oy * 0.86 < 0) { ox = -ox; oy = -oy; }
        double off = w * 0.2;
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.5));
        ctx.SetLineWidth(Math.Max(w * 0.16, 0.8));
        ctx.Move(new CGPoint(p1.X + ox * off, p1.Y + oy * off));
        ctx.AddLine(new CGPoint(p2.X + ox * off, p2.Y + oy * off));
        ctx.StrokePath();
    }

    void DrawItemAt(CGContext ctx, in DrawItem item)
    {
        switch (item.Kind)
        {
            case 0:
                DrawCapsule(ctx, item.P1, item.P2, w: item.W, color: item.Color, depth: item.Depth);
                break;
            case 1:
            case 4:
            {
                double r = item.W / 2;
                var rect = new CGRect(item.P1.X - r, item.P1.Y - r, 2 * r, 2 * r);
                ctx.SetFillColor(Shade(item.Color, 0.55, alpha: 1, depth: item.Depth));
                ctx.FillEllipse(rect);
                ctx.SetFillColor(Shade(item.Color, 0.95, alpha: 1, depth: item.Depth));
                ctx.FillEllipse(rect.InsetBy(r * 0.25, r * 0.25)
                                    .OffsetBy(-r * 0.1, r * 0.1));
                ctx.SetFillColor(new CGColor(1, 1, 1, 0.6));
                double hr = r * 0.22;
                ctx.FillEllipse(new CGRect(item.P1.X - r * 0.38 - hr,
                                           item.P1.Y + r * 0.38 - hr,
                                           2 * hr, 2 * hr));
                if (item.Kind == 4 && r > 5) DrawPinchClamp(ctx, item.P1, r);
                break;
            }
            case 2:
                // Frosted ground-glass collar
                ctx.SetStrokeColor(new CGColor(0.92, 0.94, 0.96, 0.85));
                ctx.SetLineWidth(item.W);
                ctx.Move(item.P1); ctx.AddLine(item.P2); ctx.StrokePath();
                ctx.SetStrokeColor(new CGColor(0.6, 0.64, 0.7, 0.8));
                ctx.SetLineWidth(item.W * 0.15);
                ctx.Move(item.P1); ctx.AddLine(item.P2); ctx.StrokePath();
                DrawKeckClip(ctx, item);
                break;
            case 3:
                // Condenser: outer glass envelope, coil, inner tube.
                ctx.SetStrokeColor(new CGColor(0.75, 0.85, 0.95, 0.28));
                ctx.SetLineWidth(item.W * 2.1);
                ctx.Move(item.P1); ctx.AddLine(item.P2); ctx.StrokePath();
                if (item.Coil.Length > 1)
                {
                    ctx.SetStrokeColor(Shade(item.Color, 0.9, alpha: 0.9, depth: item.Depth));
                    ctx.SetLineWidth(Math.Max(item.W * 0.22, 1));
                    ctx.Move(item.Coil[0]);
                    for (int k = 1; k < item.Coil.Length; k++) ctx.AddLine(item.Coil[k]);
                    ctx.StrokePath();
                }
                DrawCapsule(ctx, item.P1, item.P2, w: item.W * 0.55,
                            color: item.Color, depth: item.Depth);
                break;
            case 5:
                DrawColdTrap(ctx, item.P1, s: item.W, color: item.Color, depth: item.Depth);
                break;
            case 6:
                DrawStopcock(ctx, item);
                break;
            case 7:
                DrawSchlenkFlask(ctx, item.P1, s: item.W, color: item.Color, depth: item.Depth);
                break;
            case 8:
                DrawBubbler(ctx, item.P1, s: item.W, color: item.Color, depth: item.Depth);
                break;
            case 9:
                DrawManifold(ctx, item);
                break;
            default:
                break;
        }
    }

    /// <summary>Cold trap: the line drops into a glass cold finger sunk in a silver
    /// dewar of liquid nitrogen, side-armed over to a little vacuum pump.
    /// Vapor curls off the rim.</summary>
    void DrawColdTrap(CGContext ctx, CGPoint p, double s, int color, double depth)
    {
        ctx.SetLineCap(CGLineCap.Round);
        // Side arm out over the dewar, then down to the pump inlet
        double armY = p.Y - s * 0.4;
        // The whole pump assembly buzzes about a pixel — it's running.
        double vib = s * 0.025;
        var pumpC = new CGPoint(
            p.X + s * 1.55 + vib * Math.Sin(tick * 1.9 + p.X),
            p.Y - s * 1.7 + vib * Math.Cos(tick * 2.3 + p.X));
        ctx.SetStrokeColor(Shade(color, 0.45, alpha: 1, depth: depth));
        ctx.SetLineWidth(Math.Max(s * 0.22, 1.6));
        ctx.Move(new CGPoint(p.X, armY));
        ctx.AddLine(new CGPoint(pumpC.X, armY));
        ctx.AddLine(new CGPoint(pumpC.X, pumpC.Y + s * 0.35));
        ctx.StrokePath();
        ctx.SetStrokeColor(Shade(color, 0.9, alpha: 1, depth: depth));
        ctx.SetLineWidth(Math.Max(s * 0.12, 1));
        ctx.Move(new CGPoint(p.X, armY));
        ctx.AddLine(new CGPoint(pumpC.X, armY));
        ctx.AddLine(new CGPoint(pumpC.X, pumpC.Y + s * 0.35));
        ctx.StrokePath();
        // Neck down from the line
        ctx.SetLineWidth(Math.Max(s * 0.3, 2));
        ctx.Move(p);
        ctx.AddLine(new CGPoint(p.X, p.Y - s * 1.0));
        ctx.StrokePath();
        // The cold finger proper: a fatter glass trap body that visibly
        // pokes out of the dewar mouth before sinking in.
        ctx.SetStrokeColor(Shade(color, 0.45, alpha: 1, depth: depth));
        ctx.SetLineWidth(s * 0.58);
        ctx.Move(new CGPoint(p.X, p.Y - s * 0.9));
        ctx.AddLine(new CGPoint(p.X, p.Y - s * 1.8));
        ctx.StrokePath();
        ctx.SetStrokeColor(Shade(color, 0.85, alpha: 1, depth: depth));
        ctx.SetLineWidth(s * 0.4);
        ctx.Move(new CGPoint(p.X, p.Y - s * 0.9));
        ctx.AddLine(new CGPoint(p.X, p.Y - s * 1.8));
        ctx.StrokePath();
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.5));
        ctx.SetLineWidth(Math.Max(s * 0.09, 0.8));
        ctx.Move(new CGPoint(p.X - s * 0.14, p.Y - s * 0.95));
        ctx.AddLine(new CGPoint(p.X - s * 0.14, p.Y - s * 1.3));
        ctx.StrokePath();
        // Silver dewar swallowing the bottom of the finger: a tall narrow
        // cup — noticeably longer than it is wide — with a flat open rim.
        double rimY = p.Y - s * 1.35;
        double botY = p.Y - s * 2.5;
        double halfW = s * 0.42;
        var cup = new CGMutablePath();
        cup.Move(new CGPoint(p.X - halfW, rimY));
        cup.AddLine(new CGPoint(p.X - halfW, botY + s * 0.3));
        cup.AddQuadCurve(new CGPoint(p.X, botY),
                         control: new CGPoint(p.X - halfW, botY));
        cup.AddQuadCurve(new CGPoint(p.X + halfW, botY + s * 0.3),
                         control: new CGPoint(p.X + halfW, botY));
        cup.AddLine(new CGPoint(p.X + halfW, rimY));
        cup.CloseSubpath();
        ctx.AddPath(cup);
        ctx.SetFillColor(new CGColor(0.62, 0.66, 0.73, 1));
        ctx.FillPath();
        ctx.AddPath(cup);
        ctx.SetStrokeColor(new CGColor(0.38, 0.40, 0.46, 1));
        ctx.SetLineWidth(Math.Max(s * 0.09, 1));
        ctx.StrokePath();
        // Rim lip
        ctx.SetStrokeColor(new CGColor(0.80, 0.83, 0.88, 1));
        ctx.SetLineWidth(Math.Max(s * 0.14, 1.2));
        ctx.Move(new CGPoint(p.X - halfW - s * 0.06, rimY));
        ctx.AddLine(new CGPoint(p.X + halfW + s * 0.06, rimY));
        ctx.StrokePath();
        // Highlight streak on the wall
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.55));
        ctx.SetLineWidth(Math.Max(s * 0.12, 1));
        ctx.Move(new CGPoint(p.X - s * 0.26, rimY - s * 0.2));
        ctx.AddLine(new CGPoint(p.X - s * 0.26, botY + s * 0.3));
        ctx.StrokePath();
        // Steam pouring off the rim: wisps rising, spreading, thinning out
        for (int k = 0; k < 4; k++)
        {
            double f = (tick * (0.007 + k * 0.0017) + k * 0.27 + p.X * 0.002) % 1;
            double side = k % 2 == 0 ? 1.0 : -1.0;
            double vx = p.X + side * s * (0.28 + 0.38 * f + 0.06 * Math.Sin(f * 10 + k * 2));
            double vy = rimY + s * (0.1 + f * 1.0);
            double vw = s * (0.14 + 0.14 * f);
            ctx.SetFillColor(new CGColor(1, 1, 1, 0.45 * (1 - f)));
            ctx.FillEllipse(new CGRect(vx - vw, vy - vw * 0.4, vw * 2, vw * 0.8));
        }
        // The vacuum pump, seen from the side: pump head under the hose
        // barb, finned motor cylinder behind it, feet, oil sight glass.
        var headDark = new CGColor(0.16, 0.17, 0.20, 1);
        var motorGray = new CGColor(0.26, 0.28, 0.33, 1);
        var outline = new CGColor(0.45, 0.47, 0.53, 1);
        var head = new CGRect(pumpC.X - s * 0.26, pumpC.Y - s * 0.32, s * 0.52, s * 0.64);
        var motor = new CGRect(pumpC.X + s * 0.24, pumpC.Y - s * 0.24, s * 0.75, s * 0.48);
        // Feet first, so the body sits on them
        ctx.SetFillColor(headDark);
        for (int q = 0; q < 2; q++)
        {
            double fx = q == 0 ? head.MinX + s * 0.06 : motor.MaxX - s * 0.16;
            ctx.Fill(new CGRect(fx, head.MinY - s * 0.1, s * 0.12, s * 0.12));
        }
        // Motor with cooling fins
        ctx.SetFillColor(motorGray);
        ctx.AddPath(CGPath.RoundedRect(motor, s * 0.08, s * 0.08));
        ctx.FillPath();
        ctx.SetStrokeColor(headDark);
        ctx.SetLineWidth(Math.Max(s * 0.045, 0.6));
        for (int i = 1; i <= 4; i++)
        {
            double fx = motor.MinX + motor.Width * i / 5;
            ctx.Move(new CGPoint(fx, motor.MinY + s * 0.05));
            ctx.AddLine(new CGPoint(fx, motor.MaxY - s * 0.05));
            ctx.StrokePath();
        }
        // Pump head, hose barb on top, oil sight glass low on the side
        ctx.SetFillColor(headDark);
        ctx.AddPath(CGPath.RoundedRect(head, s * 0.06, s * 0.06));
        ctx.FillPath();
        ctx.SetStrokeColor(outline);
        ctx.SetLineWidth(Math.Max(s * 0.05, 0.7));
        ctx.AddPath(CGPath.RoundedRect(head, s * 0.06, s * 0.06));
        ctx.StrokePath();
        ctx.SetStrokeColor(headDark);
        ctx.SetLineWidth(Math.Max(s * 0.16, 1.2));
        ctx.Move(new CGPoint(pumpC.X, head.MaxY));
        ctx.AddLine(new CGPoint(pumpC.X, head.MaxY + s * 0.14));
        ctx.StrokePath();
        ctx.SetFillColor(new CGColor(0.85, 0.62, 0.25, 1));
        double og = Math.Max(s * 0.06, 0.9);
        ctx.FillEllipse(new CGRect(head.MaxX - s * 0.16 - og,
                                   head.MinY + s * 0.14 - og,
                                   2 * og, 2 * og));
        // Power lamp on the motor end
        ctx.SetFillColor(new CGColor(0.3, 0.95, 0.4, 1));
        double lr = Math.Max(s * 0.04, 0.7);
        ctx.FillEllipse(new CGRect(motor.MaxX - s * 0.1 - lr,
                                   motor.MaxY - s * 0.1 - lr,
                                   2 * lr, 2 * lr));
    }

    /// <summary>Manifold take-off on a straight run: a stubby down-tube with its own
    /// stopcock and a round-bottom flask plumbed in underneath, stir bar
    /// going — the line moonlighting as a Schlenk manifold.</summary>
    void DrawManifold(CGContext ctx, in DrawItem item)
    {
        if (!(item.W > 5)) return;
        double dx = item.P2.X - item.P1.X, dy = item.P2.Y - item.P1.Y;
        double len = Math.Max(double.Hypot(dx, dy), 0.001);
        var u = new CGVector(dx / len, dy / len);
        var v = new CGVector(-u.Dy, u.Dx);
        if (v.Dy > 0) v = new CGVector(-v.Dx, -v.Dy);  // flask hangs down
        var pc = new CGPoint((item.P1.X + item.P2.X) / 2,
                             (item.P1.Y + item.P2.Y) / 2);
        double w = item.W;
        CGPoint At(double a, double b) =>
            new(pc.X + u.Dx * a + v.Dx * b, pc.Y + u.Dy * a + v.Dy * b);
        ctx.SetLineCap(CGLineCap.Round);
        // Take-off stub (clear glass)
        ctx.SetStrokeColor(Shade(0, 0.42, alpha: 1, depth: item.Depth));
        ctx.SetLineWidth(w * 0.6);
        ctx.Move(At(0, w * 0.3)); ctx.AddLine(At(0, w * 1.7));
        ctx.StrokePath();
        ctx.SetStrokeColor(Shade(0, 0.85, alpha: 1, depth: item.Depth));
        ctx.SetLineWidth(w * 0.4);
        ctx.Move(At(0, w * 0.3)); ctx.AddLine(At(0, w * 1.7));
        ctx.StrokePath();
        // Stopcock on the stub
        var red = new CGColor(0.82, 0.18, 0.14, 1);
        ctx.SetStrokeColor(red);
        ctx.SetLineWidth(Math.Max(w * 0.2, 1.4));
        ctx.Move(At(0, w * 0.95)); ctx.AddLine(At(w * 0.75, w * 0.95));
        ctx.StrokePath();
        ctx.SetLineWidth(Math.Max(w * 0.26, 1.7));
        ctx.Move(At(w * 0.75, w * 0.62)); ctx.AddLine(At(w * 0.75, w * 1.28));
        ctx.StrokePath();
        // Frosted joint where the flask hangs on
        ctx.SetStrokeColor(new CGColor(0.92, 0.94, 0.96, 0.85));
        ctx.SetLineWidth(w * 0.7);
        ctx.Move(At(0, w * 1.45)); ctx.AddLine(At(0, w * 1.75));
        ctx.StrokePath();
        // Round-bottom flask with contents and a spinning stir bar
        var fc = At(0, w * 2.55);
        double r = w * 0.95;
        var rect = new CGRect(fc.X - r, fc.Y - r, 2 * r, 2 * r);
        ctx.SetFillColor(Shade(0, 0.75, alpha: 0.96, depth: item.Depth));
        ctx.FillEllipse(rect);
        ctx.SaveGState();
        ctx.AddEllipse(rect);
        ctx.Clip();
        ctx.SetFillColor(Shade(item.Color, 1.0, alpha: 0.95, depth: item.Depth));
        ctx.Fill(new CGRect(rect.MinX, rect.MinY, rect.Width, r * 0.95));
        double spin = Math.Cos(tick * 0.3 + pc.X * 0.07);
        double half = r * 0.45 * Math.Max(Math.Abs(spin), 0.18);
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.92));
        ctx.SetLineWidth(Math.Max(r * 0.17, 1.2));
        ctx.Move(new CGPoint(fc.X - half, fc.Y - r * 0.6));
        ctx.AddLine(new CGPoint(fc.X + half, fc.Y - r * 0.6));
        ctx.StrokePath();
        ctx.RestoreGState();
        ctx.AddEllipse(rect);
        ctx.SetStrokeColor(Shade(0, 0.4, alpha: 1, depth: item.Depth));
        ctx.SetLineWidth(Math.Max(r * 0.12, 1));
        ctx.StrokePath();
        // Glint
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.5));
        ctx.SetLineWidth(Math.Max(r * 0.09, 0.8));
        ctx.AddArc(fc, r * 0.72, 2.2, 3.0, clockwise: false);
        ctx.StrokePath();
    }

    /// <summary>Glass stopcock plugged through a straight run: frosted barrel across
    /// the tube, red plug stem sticking out with a T-grip — a little piece of
    /// Schlenk manifold.</summary>
    static void DrawStopcock(CGContext ctx, in DrawItem item)
    {
        if (!(item.W > 4)) return;
        double dx = item.P2.X - item.P1.X, dy = item.P2.Y - item.P1.Y;
        double len = Math.Max(double.Hypot(dx, dy), 0.001);
        var u = new CGVector(dx / len, dy / len);
        var v = new CGVector(-u.Dy, u.Dx);
        if (v.Dy < 0) v = new CGVector(-v.Dx, -v.Dy);  // handle points up
        var pc = new CGPoint((item.P1.X + item.P2.X) / 2,
                             (item.P1.Y + item.P2.Y) / 2);
        double w = item.W;
        CGPoint At(double a, double b) =>
            new(pc.X + u.Dx * a + v.Dx * b, pc.Y + u.Dy * a + v.Dy * b);
        ctx.SetLineCap(CGLineCap.Round);
        // Frosted barrel crossing the tube
        ctx.SetStrokeColor(new CGColor(0.92, 0.94, 0.96, 0.9));
        ctx.SetLineWidth(w * 0.55);
        ctx.Move(At(0, -w * 0.75)); ctx.AddLine(At(0, w * 0.75));
        ctx.StrokePath();
        // Red plug stem and T-grip
        var red = new CGColor(0.82, 0.18, 0.14, 1);
        ctx.SetStrokeColor(red);
        ctx.SetLineWidth(Math.Max(w * 0.22, 1.5));
        ctx.Move(At(0, w * 0.7)); ctx.AddLine(At(0, w * 1.35));
        ctx.StrokePath();
        ctx.SetLineWidth(Math.Max(w * 0.3, 2));
        ctx.Move(At(-w * 0.5, w * 1.35)); ctx.AddLine(At(w * 0.5, w * 1.35));
        ctx.StrokePath();
        // Glint on the barrel
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.5));
        ctx.SetLineWidth(Math.Max(w * 0.12, 0.8));
        ctx.Move(At(-w * 0.1, -w * 0.4)); ctx.AddLine(At(-w * 0.1, w * 0.4));
        ctx.StrokePath();
    }

    /// <summary>Schlenk flask hanging off the end of a run: frosted ground joint,
    /// neck, round bulb with solvent and a spinning stir bar, and the
    /// signature sidearm stopcock.</summary>
    void DrawSchlenkFlask(CGContext ctx, CGPoint p, double s, int color, double depth)
    {
        var bulbC = new CGPoint(p.X, p.Y - s * 1.35);
        double bulbR = s;
        double neckW = Math.Max(s * 0.34, 2);
        ctx.SetLineCap(CGLineCap.Round);
        // Neck down from the pipe
        ctx.SetStrokeColor(Shade(color, 0.6, alpha: 0.95, depth: depth));
        ctx.SetLineWidth(neckW);
        ctx.Move(p);
        ctx.AddLine(new CGPoint(bulbC.X, bulbC.Y + bulbR * 0.8));
        ctx.StrokePath();
        // Sidearm angling off the neck, with its own little red stopcock
        var armBase = new CGPoint(p.X, p.Y - s * 0.45);
        var armEnd = new CGPoint(p.X + s * 1.15, p.Y - s * 0.1);
        ctx.SetLineWidth(neckW * 0.7);
        ctx.Move(armBase); ctx.AddLine(armEnd); ctx.StrokePath();
        var red = new CGColor(0.82, 0.18, 0.14, 1);
        var armMid = new CGPoint((armBase.X + armEnd.X) / 2,
                                 (armBase.Y + armEnd.Y) / 2);
        var plugTop = new CGPoint(armMid.X + s * 0.18, armMid.Y + s * 0.5);
        ctx.SetStrokeColor(red);
        ctx.SetLineWidth(Math.Max(s * 0.14, 1.2));
        ctx.Move(armMid); ctx.AddLine(plugTop); ctx.StrokePath();
        ctx.SetLineWidth(Math.Max(s * 0.18, 1.6));
        ctx.Move(new CGPoint(plugTop.X - s * 0.28, plugTop.Y - s * 0.06));
        ctx.AddLine(new CGPoint(plugTop.X + s * 0.28, plugTop.Y + s * 0.06));
        ctx.StrokePath();
        // Bulb
        var rect = new CGRect(bulbC.X - bulbR, bulbC.Y - bulbR, bulbR * 2, bulbR * 2);
        ctx.SetFillColor(Shade(color, 0.75, alpha: 0.96, depth: depth));
        ctx.FillEllipse(rect);
        // Solvent pooling, stir bar whirling (its apparent length breathes)
        ctx.SaveGState();
        ctx.AddEllipse(rect);
        ctx.Clip();
        ctx.SetFillColor(Shade((color + 2) % tints.Length, 1.0, alpha: 0.95, depth: depth));
        ctx.Fill(new CGRect(rect.MinX, rect.MinY, rect.Width, bulbR * 0.9));
        double spin = Math.Cos(tick * 0.35 + p.X * 0.05);
        double half = s * 0.42 * Math.Max(Math.Abs(spin), 0.18);
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.92));
        ctx.SetLineWidth(Math.Max(s * 0.16, 1.4));
        ctx.Move(new CGPoint(bulbC.X - half, bulbC.Y - bulbR * 0.62));
        ctx.AddLine(new CGPoint(bulbC.X + half, bulbC.Y - bulbR * 0.62));
        ctx.StrokePath();
        ctx.RestoreGState();
        // Outline
        ctx.AddEllipse(rect);
        ctx.SetStrokeColor(Shade(color, 0.4, alpha: 1, depth: depth));
        ctx.SetLineWidth(Math.Max(s * 0.12, 1.2));
        ctx.StrokePath();
        // Frosted ground joint where flask meets pipe
        ctx.SetStrokeColor(new CGColor(0.92, 0.94, 0.96, 0.85));
        ctx.SetLineWidth(neckW * 1.6);
        ctx.Move(new CGPoint(p.X, p.Y - s * 0.02));
        ctx.AddLine(new CGPoint(p.X, p.Y - s * 0.3));
        ctx.StrokePath();
        // Glint on the bulb
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.55));
        ctx.SetLineWidth(Math.Max(s * 0.09, 1));
        ctx.AddArc(bulbC, bulbR * 0.72, 2.2, 3.0, clockwise: false);
        ctx.StrokePath();
    }

    /// <summary>Mineral-oil bubbler: the run's dip tube plunges into a little vessel
    /// of amber oil and burps a steady stream of bubbles — the Schlenk
    /// line's exhaust.</summary>
    void DrawBubbler(CGContext ctx, CGPoint p, double s, int color, double depth)
    {
        double topY = p.Y - s * 0.35;
        double botY = p.Y - s * 1.75;
        double halfW = s * 0.52;
        ctx.SetLineCap(CGLineCap.Round);
        // Glass vessel envelope
        ctx.SetStrokeColor(new CGColor(0.75, 0.85, 0.95, 0.30));
        ctx.SetLineWidth(halfW * 2);
        ctx.Move(new CGPoint(p.X, topY));
        ctx.AddLine(new CGPoint(p.X, botY));
        ctx.StrokePath();
        // Oil sitting in the bottom
        ctx.SetStrokeColor(new CGColor(0.85, 0.62, 0.25, 0.85));
        ctx.SetLineWidth(halfW * 1.7);
        ctx.Move(new CGPoint(p.X, botY + s * 0.72));
        ctx.AddLine(new CGPoint(p.X, botY + s * 0.12));
        ctx.StrokePath();
        // Dip tube from the line down into the oil
        ctx.SetStrokeColor(Shade(color, 0.85, alpha: 1, depth: depth));
        ctx.SetLineWidth(Math.Max(s * 0.16, 1.2));
        ctx.Move(p);
        ctx.AddLine(new CGPoint(p.X, botY + s * 0.22));
        ctx.StrokePath();
        // Bubbles rising off the dip tube outlet
        if (s > 5)
        {
            for (int k = 0; k < 3; k++)
            {
                double f = (tick * (0.011 + k * 0.002) + k * 0.37 + p.X * 0.004) % 1;
                double by = botY + s * 0.2 + f * s * 1.15;
                double bx = p.X + s * (0.16 + 0.09 * Math.Sin(f * 14 + k * 2));
                double br = s * (0.055 + 0.045 * f);
                ctx.SetFillColor(new CGColor(1, 1, 1, 0.75 * (1 - f * 0.4)));
                ctx.FillEllipse(new CGRect(bx - br, by - br, br * 2, br * 2));
            }
        }
        // Rim glint
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.4));
        ctx.SetLineWidth(Math.Max(s * 0.08, 1));
        ctx.Move(new CGPoint(p.X - halfW * 0.7, topY + halfW));
        ctx.AddLine(new CGPoint(p.X + halfW * 0.7, topY + halfW));
        ctx.StrokePath();
    }

    /// <summary>Colored plastic Keck clip straddling a ground-glass joint: a spine
    /// along one side of the tube with two fork arms wrapping across it.</summary>
    static void DrawKeckClip(CGContext ctx, in DrawItem item)
    {
        double dx = item.P2.X - item.P1.X, dy = item.P2.Y - item.P1.Y;
        double len = Math.Max(double.Hypot(dx, dy), 0.001);
        if (!(item.W > 6)) return;
        var u = new CGVector(dx / len, dy / len);
        var v = new CGVector(-u.Dy, u.Dx);
        var pc = new CGPoint((item.P1.X + item.P2.X) / 2, (item.P1.Y + item.P2.Y) / 2);
        double r = item.W * 0.62;
        double d = len * 0.58;
        double lw = Math.Max(item.W * 0.22, 1.5);
        var color = keckColors[item.Color % keckColors.Length];
        CGPoint Pt(double alongAxis, double acrossAxis) =>
            new(pc.X + u.Dx * alongAxis + v.Dx * acrossAxis,
                pc.Y + u.Dy * alongAxis + v.Dy * acrossAxis);
        ctx.SetStrokeColor(color);
        ctx.SetLineWidth(lw);
        ctx.SetLineCap(CGLineCap.Round);
        // Spine along one side
        ctx.Move(Pt(-d, r));
        ctx.AddLine(Pt(d, r));
        ctx.StrokePath();
        // Fork arms wrapping across the joint
        for (int q = 0; q < 2; q++)
        {
            double s = q == 0 ? -d : d;
            ctx.Move(Pt(s, r));
            ctx.AddLine(Pt(s * 1.12, -r * 0.95));
            ctx.StrokePath();
        }
        // Highlight on the spine
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.55));
        ctx.SetLineWidth(lw * 0.35);
        ctx.Move(Pt(-d * 0.7, r * 1.12));
        ctx.AddLine(Pt(d * 0.7, r * 1.12));
        ctx.StrokePath();
    }

    /// <summary>Metal pinch clamp gripping a ball joint: a horseshoe jaw hugging the
    /// lower half of the ball, with a screw stem and wing nut attached below.</summary>
    static void DrawPinchClamp(CGContext ctx, CGPoint p, double r)
    {
        var metal = new CGColor(0.60, 0.63, 0.69, 1);
        double jr = r * 1.02;
        double lw = Math.Max(r * 0.30, 2.0);
        ctx.SetLineCap(CGLineCap.Round);
        // Jaw wraps the bottom of the ball, opening upward for the tubing.
        ctx.SetStrokeColor(metal);
        ctx.SetLineWidth(lw);
        ctx.AddArc(p, jr, Math.PI * 1.13, Math.PI * 1.87, clockwise: false);
        ctx.StrokePath();
        // Gripping tabs at the jaw tips
        for (int q = 0; q < 2; q++)
        {
            double a = q == 0 ? Math.PI * 1.13 : Math.PI * 1.87;
            var tip = new CGPoint(p.X + jr * Math.Cos(a), p.Y + jr * Math.Sin(a));
            ctx.Move(tip);
            ctx.AddLine(new CGPoint(p.X + (jr + r * 0.34) * Math.Cos(a),
                                    p.Y + (jr + r * 0.34) * Math.Sin(a)));
            ctx.StrokePath();
        }
        // Screw stem straight off the jaw, then a wing nut.
        double jawBottom = p.Y - jr;
        ctx.SetLineWidth(Math.Max(r * 0.18, 1.4));
        ctx.Move(new CGPoint(p.X, jawBottom));
        ctx.AddLine(new CGPoint(p.X, jawBottom - r * 0.55));
        ctx.StrokePath();
        double nutY = jawBottom - r * 0.62;
        ctx.SetFillColor(metal);
        for (int q = 0; q < 2; q++)
        {
            double sx = q == 0 ? -1.0 : 1.0;
            ctx.FillEllipse(new CGRect(p.X + sx * r * 0.08 - (sx < 0 ? r * 0.42 : 0),
                                       nutY - r * 0.16,
                                       r * 0.42, r * 0.32));
        }
        // Specular hint on the jaw
        ctx.SetStrokeColor(new CGColor(1, 1, 1, 0.5));
        ctx.SetLineWidth(lw * 0.3);
        ctx.AddArc(p, jr, Math.PI * 1.35, Math.PI * 1.65, clockwise: false);
        ctx.StrokePath();
    }
}
