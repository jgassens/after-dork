using System.Runtime.InteropServices;
using AfterDork.Graphics;

namespace AfterDork.Savers;

// Crystal Lattice Maze — the Windows 95 "3D Maze" saver, except you are a
// guest molecule diffusing through a metal-organic framework. Chunky software
// raycaster, procedural lattice wall textures, checkerboard floor, and instead
// of the smiley that flips you upside down, you bump into solvent molecules.
// Port of LatticeMaze/LatticeMaze.swift.

public sealed class LatticeMazeView : SaverView
{
    static uint PackRGB(int r, int g, int b) =>
        (0xFFu << 24) | ((uint)Math.Max(0, Math.Min(255, r)) << 16)
            | ((uint)Math.Max(0, Math.Min(255, g)) << 8) | (uint)Math.Max(0, Math.Min(255, b));

    struct Molecule
    {
        public int Cx, Cy;
        public int Type;
        public double Phase;
    }

    struct Gas
    {
        public double X, Y;
        public double Vx, Vy;
        public int Type;  // 0 CO2, 1 N2, 2 H2, 3 CH4
        public double Phase;
    }

    // Maze
    const int mw = 25, mh = 25;
    int[][] map = [];

    // Camera
    double posX = 1.5, posY = 1.5;
    double angle = 0.0;
    List<(int, int)> path = new();
    bool inverted = false;
    double flipT = -1.0;  // >=0 while animating

    // Molecules (the smiley stand-ins)
    readonly List<Molecule> molecules = new();
    // Gas guests diffusing through the corridors
    readonly List<Gas> gases = new();
    double speedMul = 1.0;

    // Framebuffer
    int iw = 420, ih = 236;
    uint[] buffer = [];
    double[] zbuf = [];

    // Textures
    const int ts = 64;    // sprite textures
    const int wts = 128;  // wall textures (bigger so ligand structures stay legible)
    uint[] wallTexA = [];
    uint[] wallTexB = [];
    uint[][][] spriteFrames = [];  // [type][frame][pixels], ARGB
    uint[][][] gasFrames = [];     // [type][frame][pixels], ARGB
    const int spriteFrameCount = 16;

    public LatticeMazeView(CGRect frame, bool isPreview) : base(frame, isPreview)
    {
        AnimationTimeInterval = 1.0 / 30.0;
        Setup();
    }

    void Setup()
    {
        iw = IsPreview ? 220 : 440;
        double aspect = Bounds.Width > 0 ? Bounds.Height / Bounds.Width : 0.5625;
        ih = Math.Max(120, (int)(iw * aspect));
        buffer = new uint[iw * ih];
        zbuf = new double[iw];
        Array.Fill(zbuf, 1e9);
        GenerateMaze();
        BuildWallTextures();
        BuildSprites();
        speedMul = Math.Max(0.3, Math.Min(3, Settings.Value("LatticeMaze", "speed", 1.0)));
        int gasN = Math.Max(0, Math.Min(60, (int)Settings.Value("LatticeMaze", "gas", 24)));
        posX = 1.5; posY = 1.5;
        angle = 0;
        molecules.Clear();
        for (int k = 0; k < 5; k++) PlaceMolecule();
        gases.Clear();
        var open = OpenCells();
        int count = IsPreview ? Math.Min(10, gasN) : gasN;
        for (int k = 0; k < count; k++)
        {
            if (open.Count == 0) break;
            var c = Rng.Element(open);
            double a = Rng.Range(0, 6.28);
            double x = c.Item1 + Rng.Range(0.3, 0.7);
            double y = c.Item2 + Rng.Range(0.3, 0.7);
            int type = Rng.Int(0, 4);
            double phase = Rng.Range(0, 6.28);
            gases.Add(new Gas
            {
                X = x, Y = y,
                Vx = Math.Cos(a) * 0.009, Vy = Math.Sin(a) * 0.009,
                Type = type,
                Phase = phase,
            });
        }
    }

    // MARK: - Maze generation (recursive backtracker)

    static readonly (int, int)[] carveDirs = [(2, 0), (-2, 0), (0, 2), (0, -2)];

    void GenerateMaze()
    {
        map = new int[mh][];
        for (int y = 0; y < mh; y++) { map[y] = new int[mw]; Array.Fill(map[y], 1); }
        var stack = new List<(int, int)> { (1, 1) };
        map[1][1] = 0;
        while (stack.Count > 0)
        {
            var (cx, cy) = stack[^1];
            var dirs = Rng.Shuffled(carveDirs);
            bool carved = false;
            foreach (var (dx, dy) in dirs)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx > 0 && nx < mw - 1 && ny > 0 && ny < mh - 1 && map[ny][nx] == 1)
                {
                    map[ny][nx] = 0;
                    map[cy + dy / 2][cx + dx / 2] = 0;
                    stack.Add((nx, ny));
                    carved = true;
                    break;
                }
            }
            if (!carved) stack.RemoveAt(stack.Count - 1);
        }
        // Knock a few extra openings through so the crawl can loop.
        int extra = 0;
        while (extra < 14)
        {
            int x = Rng.Int(1, mw - 1), y = Rng.Int(1, mh - 1);
            if (map[y][x] == 1 && ((map[y][x - 1] == 0 && map[y][x + 1] == 0)
                || (map[y - 1][x] == 0 && map[y + 1][x] == 0)))
            {
                map[y][x] = 0;
                extra += 1;
            }
        }
    }

    List<(int, int)> OpenCells()
    {
        var @out = new List<(int, int)>();
        for (int y = 0; y < mh; y++) for (int x = 0; x < mw; x++) if (map[y][x] == 0) @out.Add((x, y));
        return @out;
    }

    void PlaceMolecule()
    {
        var cells = new List<(int, int)>();
        foreach (var c in OpenCells())
        {
            if (Math.Abs(c.Item1 - (int)posX) + Math.Abs(c.Item2 - (int)posY) <= 5) continue;
            bool taken = false;
            foreach (var m in molecules) if (m.Cx == c.Item1 && m.Cy == c.Item2) { taken = true; break; }
            if (!taken) cells.Add(c);
        }
        if (cells.Count > 0)
        {
            var c = Rng.Element(cells);
            int type = Rng.Int(0, 3);
            double phase = Rng.Range(0, 6.28);
            molecules.Add(new Molecule { Cx = c.Item1, Cy = c.Item2, Type = type, Phase = phase });
        }
    }

    // MARK: - Textures

    void BuildWallTextures()
    {
        wallTexA = MakeLatticeTexture(style: 0);  // ZIF-8: Zn + 2-methylimidazolate
        wallTexB = MakeLatticeTexture(style: 1);  // UiO-66: Zr + terephthalate
    }

    /// <summary>
    /// Draws one wall tile as a framework fragment with an atomically correct
    /// bridging ligand connecting the diagonal metal nodes.
    /// Style 0 = ZIF-8 (2-methylimidazolate, N-Zn on both nitrogens),
    /// style 1 = UiO-66 (terephthalate, para carboxylates chelating Zr).
    /// </summary>
    uint[] MakeLatticeTexture(int style)
    {
        using var ctx = MakeContext(wts);
        double S = wts;
        var c = new CGPoint(S / 2, S / 2);
        var bg = new CGColor(0.045, 0.055, 0.10, 1);
        var bond = new CGColor(0.88, 0.90, 0.94, 1);
        var nBlue = NSColor.CalibratedRed(0.45, 0.63, 1.0, 1);
        var oRed = NSColor.CalibratedRed(1.0, 0.38, 0.32, 1);
        (double, double, double) steel = (0.72, 0.74, 0.80);

        ctx.SetFillColor(bg);
        ctx.Fill(new CGRect(0, 0, S, S));
        for (int k = 0; k < 320; k++)
        {
            ctx.SetFillColor(new CGColor(0.085, 0.095, 0.16, 1));
            double rx = RndT(0, S), ry = RndT(0, S);
            ctx.Fill(new CGRect(rx, ry, 1.6, 1.6));
        }

        // (Swift makes an NSGraphicsContext here for the text; the port draws
        // attributed strings straight into the bitmap context.)

        CGPoint Pt(CGPoint center, double r, double deg)
        {
            double a = deg * Math.PI / 180;
            return new CGPoint(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a));
        }
        void Line(CGPoint a, CGPoint b, double w, CGColor color)
        {
            ctx.SetStrokeColor(color);
            ctx.SetLineWidth(w);
            ctx.SetLineCap(CGLineCap.Round);
            ctx.Move(a); ctx.AddLine(b); ctx.StrokePath();
        }
        // Second stroke of a double bond, offset toward the ring center.
        void InnerBond(CGPoint a, CGPoint b, CGPoint center, double w)
        {
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
            double ox = center.X - mx, oy = center.Y - my;
            double ol = Math.Max(Math.Sqrt(ox * ox + oy * oy), 0.001);
            const double off = 4.5;
            double dx = ox / ol * off, dy = oy / ol * off;
            var a2 = new CGPoint(a.X * 0.82 + b.X * 0.18 + dx, a.Y * 0.82 + b.Y * 0.18 + dy);
            var b2 = new CGPoint(b.X * 0.82 + a.X * 0.18 + dx, b.Y * 0.82 + a.Y * 0.18 + dy);
            Line(a2, b2, w, bond);
        }
        void AtomLabel(string s, CGPoint p, CGColor color, double size)
        {
            var str = new NSAttributedString(s, NSFont.BoldSystemFont(size), color);
            var sz = str.Size();
            ctx.SetFillColor(bg);
            ctx.FillEllipse(new CGRect(p.X - sz.Width / 2 - 2, p.Y - sz.Height / 2 - 1,
                                       sz.Width + 4, sz.Height + 2));
            str.Draw(ctx, new CGPoint(p.X - sz.Width / 2, p.Y - sz.Height / 2));
        }
        void MetalBall(CGPoint p, double r, (double, double, double) tint)
        {
            ctx.SetFillColor(new CGColor(tint.Item1 * 0.4, tint.Item2 * 0.4, tint.Item3 * 0.4, 1));
            ctx.FillEllipse(new CGRect(p.X - r, p.Y - r, 2 * r, 2 * r));
            ctx.SetFillColor(new CGColor(tint.Item1, tint.Item2, tint.Item3, 1));
            double r2 = r * 0.72;
            ctx.FillEllipse(new CGRect(p.X - r2 - r * 0.1, p.Y - r2 + r * 0.1, 2 * r2, 2 * r2));
            ctx.SetFillColor(new CGColor(1, 1, 1, 0.7));
            double hr = r * 0.2;
            ctx.FillEllipse(new CGRect(p.X - r * 0.35 - hr, p.Y + r * 0.35 - hr, 2 * hr, 2 * hr));
        }

        CGPoint[] corners = [new(0, 0), new(S, 0), new(S, S), new(0, S)];
        // Faint framework edge along the tile border.
        var frame = new CGColor(0.22, 0.28, 0.42, 1);
        for (int i = 0; i < 4; i++) Line(corners[i], corners[(i + 1) % 4], 2.5, frame);

        CGPoint Toward(CGPoint corner, CGPoint from, double stopAt)
        {
            double dx = from.X - corner.X, dy = from.Y - corner.Y;
            double l = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.001);
            return new CGPoint(corner.X + dx / l * stopAt, corner.Y + dy / l * stopAt);
        }

        if (style == 0)
        {
            // ZIF-8: 2-methylimidazolate bridging two Zn at the tile's side-edge
            // midpoints. The ring sits above the Zn-Zn line so the Zn-Im-Zn
            // angle comes out ~145 degrees — the zeolitic angle that makes
            // ZIFs ZIFs. Tiled horizontally this reads as the real zigzag
            // chain. Ring: N1-C2(-CH3)-N3-C4-C5; doubles drawn C2=N3, C4=C5.
            (double, double, double) znTint = (0.55, 0.62, 0.78);
            var znL = new CGPoint(0, S / 2);
            var znR = new CGPoint(S, S / 2);
            var rc = new CGPoint(S / 2, S / 2 + 24);
            const double r = 19;
            CGPoint n1 = Pt(rc, r, 198), c2 = Pt(rc, r, 270), n3 = Pt(rc, r, 342);
            CGPoint c4 = Pt(rc, r, 54), c5 = Pt(rc, r, 126);
            Line(n1, c2, 2.6, bond);
            Line(c2, n3, 2.6, bond);
            Line(n3, c4, 2.6, bond);
            Line(c4, c5, 2.6, bond);
            Line(c5, n1, 2.6, bond);
            InnerBond(c2, n3, rc, 2.0);
            InnerBond(c4, c5, rc, 2.0);
            // 2-methyl hangs down into the pore
            var me = Pt(rc, r + 11, 270);
            Line(c2, me, 2.4, bond);
            AtomLabel("CH₃", Pt(rc, r + 22, 270), NSColor.White, 10);
            // N -> Zn coordination, radial through each nitrogen
            Line(n1, Toward(znL, n1, 15), 2.2, bond);
            Line(n3, Toward(znR, n3, 15), 2.2, bond);
            AtomLabel("N", n1, nBlue, 12);
            AtomLabel("N", n3, nBlue, 12);
            // Tetrahedral Zn: stub bonds toward the imidazolates we can't see
            foreach (var (ball, mirror) in new (CGPoint, double)[] { (znL, 1), (znR, -1) })
            {
                foreach (double ang in new double[] { -52, -115 })
                {
                    double a = mirror > 0 ? ang : 180 - ang;
                    Line(Pt(ball, 13, a), Pt(ball, 26, a), 2.0, new CGColor(0.5, 0.53, 0.6, 1));
                }
            }
            MetalBall(znL, 13, znTint);
            MetalBall(znR, 13, znTint);
            AtomLabel("Zn", new CGPoint(17, S / 2 + 21), NSColor.CalibratedWhite(0.62, 1), 8);
        }
        else
        {
            // UiO-66: terephthalate (BDC) between diagonal Zr6 cluster nodes.
            // O-C-O opened to ~125 degrees, and the two oxygens of each
            // carboxylate bridge two DIFFERENT Zr atoms of the cluster
            // (mu2-eta1:eta1), not chelate one metal.
            const double r = 20;
            var v = new List<CGPoint>();
            for (int k = 0; k < 6; k++) v.Add(Pt(c, r, 45 + k * 60.0));
            for (int k = 0; k < 6; k++) Line(v[k], v[(k + 1) % 6], 2.6, bond);
            foreach (int k in new[] { 0, 2, 4 }) InnerBond(v[k], v[(k + 1) % 6], c, 2.0);
            foreach (var (vi, corner) in new (int, CGPoint)[] { (3, corners[0]), (0, corners[2]) })
            {
                var vp = v[vi];
                double dirx = (vp.X - c.X) / r, diry = (vp.Y - c.Y) / r;
                var cc = new CGPoint(vp.X + dirx * 13, vp.Y + diry * 13);
                Line(vp, cc, 2.6, bond);
                double baseA = Math.Atan2(diry, dirx);
                // Perpendicular to the diagonal, to split the two Zr targets
                var perp = new CGPoint(-diry, dirx);
                foreach (var (spread, isDouble) in new (double, bool)[] { (1.09, true), (-1.09, false) })
                {
                    var o = new CGPoint(cc.X + 12 * Math.Cos(baseA + spread),
                                        cc.Y + 12 * Math.Sin(baseA + spread));
                    Line(cc, o, 2.4, bond);
                    if (isDouble)
                    {
                        double px = -(o.Y - cc.Y), py = o.X - cc.X;
                        double pl = Math.Max(Math.Sqrt(px * px + py * py), 0.001);
                        Line(new CGPoint(cc.X + px / pl * 3.4, cc.Y + py / pl * 3.4),
                             new CGPoint(o.X + px / pl * 3.4, o.Y + py / pl * 3.4), 1.8, bond);
                    }
                    // Each O binds its own Zr of the cluster
                    double side = spread > 0 ? 9 : -9;
                    var target = new CGPoint(corner.X + dirx * 12 + perp.X * side,
                                             corner.Y + diry * 12 + perp.Y * side);
                    Line(o, target, 1.6, new CGColor(0.55, 0.58, 0.66, 1));
                    AtomLabel("O", o, oRed, 11);
                }
            }
            // Zr6O4(OH)4 nodes: a cluster of Zr atoms with mu3-O caps, not
            // a single metal ball.
            foreach (var corner in corners)
            {
                foreach (var off in new CGPoint[] { new(0, 10), new(9, -6), new(-9, -6) })
                    MetalBall(new CGPoint(corner.X + off.X, corner.Y + off.Y), 8, steel);
                ctx.SetFillColor(oRed);
                foreach (var off in new CGPoint[] { new(0, -8), new(7, 5), new(-7, 5) })
                {
                    ctx.FillEllipse(new CGRect(corner.X + off.X - 2.6, corner.Y + off.Y - 2.6, 5.2, 5.2));
                }
            }
            AtomLabel("Zr₆", new CGPoint(26, 24), NSColor.CalibratedWhite(0.62, 1), 8);
        }
        return ReadPixels(ctx);
    }

    static double RndT(double lo, double hi) => Rng.Range(lo, hi);

    // MARK: - Sprites

    static BitmapContext MakeSpriteContext() => MakeContext(ts);

    // premultipliedFirst | byteOrder32Little, row 0 = top: BitmapContext's layout.
    static BitmapContext MakeContext(int size) => new(size, size);

    static uint[] ReadSprite(BitmapContext ctx) => ReadPixels(ctx);

    static uint[] ReadPixels(BitmapContext ctx) => ctx.ReadPixels();

    /// <summary>CPK-style overlapping-ball atom.</summary>
    static void CpkBall(CGContext ctx, CGPoint p, double r, double red, double green, double blue)
    {
        ctx.SetFillColor(new CGColor(red * 0.5, green * 0.5, blue * 0.5, 1));
        ctx.FillEllipse(new CGRect(p.X - r, p.Y - r, 2 * r, 2 * r));
        ctx.SetFillColor(new CGColor(red, green, blue, 1));
        double r2 = r * 0.78;
        ctx.FillEllipse(new CGRect(p.X - r2 - r * 0.08, p.Y - r2 + r * 0.08, 2 * r2, 2 * r2));
        ctx.SetFillColor(new CGColor(1, 1, 1, 0.65));
        double hr = r * 0.18;
        ctx.FillEllipse(new CGRect(p.X - r * 0.32 - hr, p.Y + r * 0.32 - hr, 2 * hr, 2 * hr));
    }

    void BuildGasSprites()
    {
        gasFrames = new uint[4][][];
        for (int type = 0; type < 4; type++)
        {
            gasFrames[type] = new uint[spriteFrameCount][];
            for (int f = 0; f < spriteFrameCount; f++)
            {
                using var ctx = MakeSpriteContext();
                ctx.TranslateBy(ts / 2.0, ts / 2.0);
                ctx.RotateBy((double)f / spriteFrameCount * 2 * Math.PI);
                switch (type)
                {
                    case 0:  // CO2: O=C=O
                        CpkBall(ctx, new CGPoint(-19, 0), 13, 0.92, 0.25, 0.2);
                        CpkBall(ctx, new CGPoint(19, 0), 13, 0.92, 0.25, 0.2);
                        CpkBall(ctx, CGPoint.Zero, 12, 0.45, 0.45, 0.45);
                        break;
                    case 1:  // N2
                        CpkBall(ctx, new CGPoint(-10, 0), 13, 0.25, 0.35, 0.92);
                        CpkBall(ctx, new CGPoint(10, 0), 13, 0.25, 0.35, 0.92);
                        break;
                    case 2:  // H2
                        CpkBall(ctx, new CGPoint(-8, 0), 10, 0.92, 0.92, 0.95);
                        CpkBall(ctx, new CGPoint(8, 0), 10, 0.92, 0.92, 0.95);
                        break;
                    default:  // CH4
                        for (int k = 0; k < 4; k++)
                        {
                            double a = k * Math.PI / 2 + Math.PI / 4;
                            CpkBall(ctx, new CGPoint(17 * Math.Cos(a), 17 * Math.Sin(a)), 9, 0.92, 0.92, 0.95);
                        }
                        CpkBall(ctx, CGPoint.Zero, 13, 0.45, 0.45, 0.45);
                        break;
                }
                gasFrames[type][f] = ReadSprite(ctx);
            }
        }
    }

    void BuildSprites()
    {
        BuildGasSprites();
        spriteFrames = new uint[3][][];
        for (int type = 0; type < 3; type++)
        {
            spriteFrames[type] = new uint[spriteFrameCount][];
            for (int f = 0; f < spriteFrameCount; f++)
            {
                using var ctx = MakeSpriteContext();
                ctx.TranslateBy(ts / 2.0, ts / 2.0);
                ctx.RotateBy((double)f / spriteFrameCount * 2 * Math.PI);
                switch (type)
                {
                    case 0: DrawBenzeneSprite(ctx); break;
                    case 1: DrawWaterSprite(ctx); break;
                    default: DrawBuckySprite(ctx); break;
                }
                spriteFrames[type][f] = ReadSprite(ctx);
            }
        }
    }

    static void DrawBenzeneSprite(CGContext ctx)
    {
        ctx.SetStrokeColor(new CGColor(0.95, 0.95, 1, 1));
        ctx.SetLineWidth(3);
        var path = new CGMutablePath();
        for (int i = 0; i < 6; i++)
        {
            double a = i * Math.PI / 3;
            var p = new CGPoint(21 * Math.Cos(a), 21 * Math.Sin(a));
            if (i == 0) path.Move(p); else path.AddLine(p);
        }
        path.CloseSubpath();
        ctx.AddPath(path);
        ctx.StrokePath();
        ctx.SetLineWidth(2);
        ctx.StrokeEllipse(new CGRect(-12, -12, 24, 24));
        ctx.SetFillColor(new CGColor(0.4, 0.85, 0.5, 1));
        for (int i = 0; i < 6; i++)
        {
            double a = i * Math.PI / 3;
            ctx.FillEllipse(new CGRect(21 * Math.Cos(a) - 4, 21 * Math.Sin(a) - 4, 8, 8));
        }
    }

    static void DrawWaterSprite(CGContext ctx)
    {
        // Two H, one O, 104.5-degree bend.
        double hAngle = 104.5 * Math.PI / 180 / 2;
        var h1 = new CGPoint(-18 * Math.Sin(hAngle), -18 * Math.Cos(hAngle));
        var h2 = new CGPoint(18 * Math.Sin(hAngle), -18 * Math.Cos(hAngle));
        ctx.SetStrokeColor(new CGColor(0.7, 0.7, 0.75, 1));
        ctx.SetLineWidth(4);
        ctx.Move(CGPoint.Zero); ctx.AddLine(h1);
        ctx.Move(CGPoint.Zero); ctx.AddLine(h2);
        ctx.StrokePath();
        ctx.SetFillColor(new CGColor(0.9, 0.2, 0.15, 1));
        ctx.FillEllipse(new CGRect(-11, -11, 22, 22));
        ctx.SetFillColor(new CGColor(1, 1, 1, 1));
        ctx.FillEllipse(new CGRect(h1.X - 7, h1.Y - 7, 14, 14));
        ctx.FillEllipse(new CGRect(h2.X - 7, h2.Y - 7, 14, 14));
        ctx.SetFillColor(new CGColor(1, 0.6, 0.55, 1));
        ctx.FillEllipse(new CGRect(-7, 0, 7, 7));
    }

    static void DrawBuckySprite(CGContext ctx)
    {
        ctx.SetFillColor(new CGColor(0.35, 0.32, 0.3, 1));
        ctx.FillEllipse(new CGRect(-22, -22, 44, 44));
        ctx.SetStrokeColor(new CGColor(0.8, 0.75, 0.7, 1));
        ctx.SetLineWidth(1.5);
        // Hexagon/pentagon cage suggestion
        foreach (double ring in new[] { 10.0, 19.0 })
        {
            int n = ring < 15 ? 5 : 6;
            var path = new CGMutablePath();
            for (int i = 0; i < n; i++)
            {
                double a = (double)i / n * 2 * Math.PI + (ring < 15 ? 0.3 : 0);
                var p = new CGPoint(ring * Math.Cos(a), ring * Math.Sin(a));
                if (i == 0) path.Move(p); else path.AddLine(p);
            }
            path.CloseSubpath();
            ctx.AddPath(path);
            ctx.StrokePath();
        }
        for (int i = 0; i < 6; i++)
        {
            double a = i / 6.0 * 2 * Math.PI;
            ctx.SetStrokeColor(new CGColor(0.7, 0.65, 0.6, 1));
            ctx.Move(new CGPoint(10 * Math.Cos(a + 0.3), 10 * Math.Sin(a + 0.3)));
            ctx.AddLine(new CGPoint(19 * Math.Cos(a), 19 * Math.Sin(a)));
            ctx.StrokePath();
        }
        ctx.SetFillColor(new CGColor(1, 1, 1, 0.35));
        ctx.FillEllipse(new CGRect(-14, 4, 10, 10));
    }

    // MARK: - Navigation

    static readonly (int, int)[] bfsDirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    void RecomputePath()
    {
        var startC = ((int)posX, (int)posY);
        var dist = new int[mh][];
        var prev = new (int, int)[mh][];
        for (int y = 0; y < mh; y++)
        {
            dist[y] = new int[mw]; Array.Fill(dist[y], -1);
            prev[y] = new (int, int)[mw]; Array.Fill(prev[y], (-1, -1));
        }
        var queue = new List<(int, int)> { startC };
        dist[startC.Item2][startC.Item1] = 0;
        int qi = 0;
        while (qi < queue.Count)
        {
            var (cx, cy) = queue[qi]; qi += 1;
            foreach (var (dx, dy) in bfsDirs)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx >= 0 && nx < mw && ny >= 0 && ny < mh && map[ny][nx] == 0
                    && dist[ny][nx] < 0)
                {
                    dist[ny][nx] = dist[cy][cx] + 1;
                    prev[ny][nx] = (cx, cy);
                    queue.Add((nx, ny));
                }
            }
        }
        var far = queue.FindAll(q => dist[q.Item2][q.Item1] > 6);
        var pool = far.Count == 0 ? queue : far;
        if (pool.Count == 0) return;
        var target = Rng.Element(pool);
        var newPath = new List<(int, int)>();
        while (target != startC && target.Item1 >= 0)
        {
            newPath.Add(target);
            target = prev[target.Item2][target.Item1];
        }
        newPath.Reverse();
        path = newPath;
    }

    public override void AnimateOneFrame()
    {
        if (flipT >= 0)
        {
            flipT += 1.0 / 36.0;
            if (flipT >= 1)
            {
                flipT = -1;
                inverted = !inverted;
            }
        }
        if (path.Count == 0) RecomputePath();
        if (path.Count > 0)
        {
            var next = path[0];
            double tx = next.Item1 + 0.5, ty = next.Item2 + 0.5;
            double dx = tx - posX, dy = ty - posY;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double desired = Math.Atan2(dy, dx);
            double diff = desired - angle;
            while (diff > Math.PI) diff -= 2 * Math.PI;
            while (diff < -Math.PI) diff += 2 * Math.PI;
            if (Math.Abs(diff) > 0.05)
            {
                double turnCap = 0.085 * speedMul;
                angle += Math.Max(-turnCap, Math.Min(turnCap, diff));  // turn in place, classic style
            }
            else
            {
                angle = desired;
                double step = Math.Min(0.052 * speedMul, distance);
                posX += Math.Cos(angle) * step;
                posY += Math.Sin(angle) * step;
            }
            if (distance < 0.1) path.RemoveAt(0);
        }
        // Solvent collision → barrel-flip and relocate the molecule.
        if (flipT < 0)
        {
            for (int i = 0; i < molecules.Count; i++)
            {
                if (molecules[i].Cx == (int)posX && molecules[i].Cy == (int)posY)
                {
                    flipT = 0;
                    molecules.RemoveAt(i);
                    PlaceMolecule();
                    break;
                }
            }
        }
        var mols = CollectionsMarshal.AsSpan(molecules);
        for (int i = 0; i < mols.Length; i++) mols[i].Phase += 0.09;
        // Gas guests: Brownian tumble down the corridors, bouncing off walls.
        var gs = CollectionsMarshal.AsSpan(gases);
        for (int i = 0; i < gs.Length; i++)
        {
            ref var g = ref gs[i];
            g.Phase += 0.06;
            double turn = Rng.Range(-0.09, 0.09);
            var (vx, vy) = (g.Vx, g.Vy);
            g.Vx = vx * Math.Cos(turn) - vy * Math.Sin(turn);
            g.Vy = vx * Math.Sin(turn) + vy * Math.Cos(turn);
            double nx = g.X + g.Vx;
            double ny = g.Y + g.Vy;
            const double margin = 0.18;
            bool Blocked(double px, double py)
            {
                int cx = (int)px, cy = (int)py;
                if (cx < 0 || cx >= mw || cy < 0 || cy >= mh) return true;
                if (map[cy][cx] == 1) return true;
                // Keep a margin off the walls so sprites don't clip into them
                double fx = px - cx, fy = py - cy;
                if (fx < margin && cx > 0 && map[cy][cx - 1] == 1) return true;
                if (fx > 1 - margin && cx < mw - 1 && map[cy][cx + 1] == 1) return true;
                if (fy < margin && cy > 0 && map[cy - 1][cx] == 1) return true;
                if (fy > 1 - margin && cy < mh - 1 && map[cy + 1][cx] == 1) return true;
                return false;
            }
            if (Blocked(nx, g.Y)) g.Vx = -g.Vx;
            if (Blocked(g.X, ny)) g.Vy = -g.Vy;
            g.X += g.Vx;
            g.Y += g.Vy;
        }
        Render();
        NeedsDisplay = true;
    }

    // MARK: - Renderer

    // Sprites, far to near: big solvent molecules and the small gas
    // guests diffusing through the pores.
    struct SpriteItem
    {
        public double X, Y;
        public uint[] Pix;
        public double SizeMul;
        public double BobAmp, BobPhase;
        public double Dist2;  // sort key, (x-posX)² + (y-posY)²
    }
    readonly List<SpriteItem> spriteItems = new();
    static readonly Comparison<SpriteItem> farToNear = (a, b) => b.Dist2.CompareTo(a.Dist2);

    void Render()
    {
        double dirX = Math.Cos(angle), dirY = Math.Sin(angle);
        const double planeScale = 0.66;
        double planeX = -Math.Sin(angle) * planeScale;
        double planeY = Math.Cos(angle) * planeScale;
        int w = iw, h = ih;
        int halfH = h / 2;
        var buf = buffer.AsSpan();
        var zb = zbuf;

        // Ceiling and floor (horizontal casting for the checkerboard).
        for (int y = halfH + 1; y < h; y++)
        {
            double p = y - halfH;
            double rowDist = halfH / p;
            double stepX = rowDist * (2 * planeX) / w;
            double stepY = rowDist * (2 * planeY) / w;
            double fx = posX + rowDist * (dirX - planeX);
            double fy = posY + rowDist * (dirY - planeY);
            double shade = Math.Max(0.25, Math.Min(1.0, 1.15 - rowDist * 0.12));
            // Ceiling row (mirrored): dark with distance haze
            int cy = h - 1 - y;
            uint cv = PackRGB((int)(16 * shade + 6), (int)(18 * shade + 6), (int)(30 * shade + 8));
            int crBase = cy * w;
            buf.Slice(crBase, w).Fill(cv);
            int frBase = y * w;
            for (int x = 0; x < w; x++)
            {
                int cellX = (int)Math.Floor(fx * 2), cellY = (int)Math.Floor(fy * 2);
                int checker = (cellX + cellY) & 1;
                double @base = checker == 0 ? 170.0 : 55.0;
                int v = (int)(@base * shade);
                buf[frBase + x] = checker == 0
                    ? PackRGB(v, v, (int)(v * 1.05))
                    : PackRGB((int)(v * 0.8), (int)(v * 0.9), v);
                fx += stepX;
                fy += stepY;
            }
        }
        // Horizon rows
        uint hv = PackRGB(10, 12, 20);
        buf.Slice(halfH * w, w).Fill(hv);

        // Walls
        for (int x = 0; x < w; x++)
        {
            double cameraX = 2 * (double)x / w - 1;
            double rayX = dirX + planeX * cameraX;
            double rayY = dirY + planeY * cameraX;
            int mapX = (int)posX, mapY = (int)posY;
            double deltaX = rayX == 0 ? 1e30 : Math.Abs(1 / rayX);
            double deltaY = rayY == 0 ? 1e30 : Math.Abs(1 / rayY);
            int stepX, stepY;
            double sideX, sideY;
            if (rayX < 0) { stepX = -1; sideX = (posX - mapX) * deltaX; }
            else { stepX = 1; sideX = (mapX + 1 - posX) * deltaX; }
            if (rayY < 0) { stepY = -1; sideY = (posY - mapY) * deltaY; }
            else { stepY = 1; sideY = (mapY + 1 - posY) * deltaY; }
            int side = 0;
            int guard_ = 0;
            while (guard_ < 64)
            {
                guard_ += 1;
                if (sideX < sideY) { sideX += deltaX; mapX += stepX; side = 0; }
                else { sideY += deltaY; mapY += stepY; side = 1; }
                if (mapX < 0 || mapX >= mw || mapY < 0 || mapY >= mh) break;
                if (map[mapY][mapX] == 1) break;
            }
            double perpDist = side == 0
                ? Math.Max(sideX - deltaX, 0.02)
                : Math.Max(sideY - deltaY, 0.02);
            zb[x] = perpDist;
            int lineH = (int)(h / perpDist);
            int drawStart = -lineH / 2 + halfH;
            int drawEnd = lineH / 2 + halfH;
            if (drawStart < 0) drawStart = 0;
            if (drawEnd >= h) drawEnd = h - 1;
            double wallX = side == 0 ? posY + perpDist * rayY : posX + perpDist * rayX;
            wallX -= Math.Floor(wallX);
            int texX = (int)(wallX * wts);
            if ((side == 0 && rayX < 0) || (side == 1 && rayY > 0)) texX = wts - texX - 1;
            if (texX < 0) texX = 0;
            if (texX >= wts) texX = wts - 1;
            // Whole neighborhoods share a framework, so ligand chains
            // continue across adjacent wall tiles instead of alternating.
            var tp = (((mapX / 6) + (mapY / 6)) & 1) == 0 ? wallTexA : wallTexB;
            double texStep = (double)wts / lineH;
            double texPos = (drawStart - halfH + lineH / 2) * texStep;
            double shade = Math.Max(0.22, Math.Min(1.0, 1.25 - perpDist * 0.11));
            if (side == 1) shade *= 0.72;
            int yEnd = Math.Max(drawStart, drawEnd);
            for (int y = drawStart; y <= yEnd; y++)
            {
                int texY = (int)texPos;
                if (texY < 0) texY = 0;
                if (texY >= wts) texY = wts - 1;
                texPos += texStep;
                uint c = tp[texY * wts + texX];
                double r = ((c >> 16) & 0xFF) * shade;
                double g = ((c >> 8) & 0xFF) * shade;
                double b = (c & 0xFF) * shade;
                buf[y * w + x] = PackRGB((int)r, (int)g, (int)b);
            }
        }

        // Sprites, far to near: big solvent molecules and the small gas
        // guests diffusing through the pores.
        spriteItems.Clear();
        foreach (var m in molecules)
        {
            int frameIdx = (((int)(m.Phase * 3) % spriteFrameCount)
                + spriteFrameCount) % spriteFrameCount;
            double sx0 = m.Cx + 0.5, sy0 = m.Cy + 0.5;
            spriteItems.Add(new SpriteItem
            {
                X = sx0, Y = sy0,
                Pix = spriteFrames[m.Type][frameIdx],
                SizeMul = 0.62,
                BobAmp = 0.05, BobPhase = m.Phase * 0.7,
                Dist2 = Sq(sx0 - posX) + Sq(sy0 - posY),
            });
        }
        foreach (var g in gases)
        {
            int frameIdx = (((int)(g.Phase * 2) % spriteFrameCount)
                + spriteFrameCount) % spriteFrameCount;
            spriteItems.Add(new SpriteItem
            {
                X = g.X, Y = g.Y,
                Pix = gasFrames[g.Type][frameIdx],
                SizeMul = 0.17,
                BobAmp = 0.09, BobPhase = g.Phase * 1.3,
                Dist2 = Sq(g.X - posX) + Sq(g.Y - posY),
            });
        }
        // Swift's sort is unstable too; equal distances are invisible.
        spriteItems.Sort(farToNear);
        double invDet = 1.0 / (planeX * dirY - dirX * planeY);
        foreach (var m in CollectionsMarshal.AsSpan(spriteItems))
        {
            double relX = m.X - posX;
            double relY = m.Y - posY;
            double transX = invDet * (dirY * relX - dirX * relY);
            double transY = invDet * (-planeY * relX + planeX * relY);
            if (transY <= 0.15) continue;
            int screenX = (int)(w / 2.0 * (1 + transX / transY));
            int size = Math.Abs((int)(h * m.SizeMul / transY));
            if (size < 2) continue;
            var sp = m.Pix;
            int bob = (int)(h * m.BobAmp / transY * Math.Sin(m.BobPhase));
            int startY = Math.Max(0, halfH - size / 2 + bob);
            int endY = Math.Min(h - 1, halfH + size / 2 + bob);
            int startX = Math.Max(0, screenX - size / 2);
            int endX = Math.Min(w - 1, screenX + size / 2);
            if (startX > endX || startY > endY) continue;
            int div = Math.Max(size, 1);
            for (int sx = startX; sx <= endX; sx++)
            {
                if (transY >= zb[sx]) continue;
                int tx = (sx - (screenX - size / 2)) * ts / div;
                if (tx < 0 || tx >= ts) continue;
                for (int sy = startY; sy <= endY; sy++)
                {
                    int ty = (sy - (halfH - size / 2 + bob)) * ts / div;
                    if (ty < 0 || ty >= ts) continue;
                    uint c = sp[ty * ts + tx];
                    if ((c >> 24) < 0x60) continue;
                    buf[sy * w + sx] = c | 0xFF000000u;
                }
            }
        }
    }

    static double Sq(double v) => v * v;

    public override void Draw(CGContext ctx)
    {
        ctx.SetFillColor(new CGColor(0, 0, 0, 1));
        ctx.Fill(Bounds);
        // noneSkipFirst | byteOrder32Little: the same 0xFFRRGGBB layout.
        using var image = CGImage.FromPixels(buffer, iw, ih, opaque: true);
        double scaleY = inverted ? -1 : 1;
        if (flipT >= 0)
        {
            double s = Math.Cos(Math.PI * flipT) * (inverted ? -1.0 : 1.0);
            scaleY = Math.Abs(s) < 0.03 ? (s < 0 ? -0.03 : 0.03) : s;
        }
        ctx.SaveGState();
        ctx.InterpolationQuality = CGInterpolationQuality.None;
        ctx.TranslateBy(Bounds.MidX, Bounds.MidY);
        ctx.ScaleBy(1, scaleY);
        ctx.Draw(image, new CGRect(-Bounds.Width / 2, -Bounds.Height / 2, Bounds.Width, Bounds.Height));
        ctx.RestoreGState();
    }
}
