using AfterDork.Graphics;

namespace AfterDork.Savers;

// Orbital Box — the Windows "3D Flower Box" morphing cube, except the shape
// morphs through atomic orbital geometries: 1s, 2p, 3dz2, 3dx2-y2, 4fz3,
// rendered as the classic chunky low-poly surface in the six saturated
// face colors, spinning and bouncing around the screen.
// Port of OrbitalBox/OrbitalBox.swift.

public sealed class OrbitalBoxView : SaverView
{
    // Real spherical-harmonic magnitudes, normalized to a max of 1.
    static readonly (string name, Func<double, double, double> f)[] shapes =
    [
        ("1s", (_, _) => 1),
        ("2p", (th, _) => Math.Abs(Math.Cos(th))),
        ("3dz²", (th, _) => Math.Abs(1.5 * Math.Cos(th) * Math.Cos(th) - 0.5)),
        ("3dx²–y²", (th, ph) =>
        {
            double s = Math.Sin(th);
            return s * s * Math.Abs(Math.Cos(2 * ph));
        }),
        ("4fz³", (th, _) =>
        {
            double c = Math.Cos(th);
            return Math.Abs(2.5 * c * c * c - 1.5 * c);
        }),
    ];

    const int nTheta = 18, nPhi = 26;
    int holdFrames = 80, morphFrames = 66;
    double spinMul = 1.0;

    int tick = 0;
    int shapeIdx = 0;
    double rotX = 0.4, rotY = 0.0;
    double cx = 400, cy = 300;
    double vx = 1.4, vy = 1.0;

    // The Flower Box palette: one saturated color per face direction.
    static readonly CGColor[] palette =
    [
        new(0.92, 0.15, 0.15, 1),  // +x red
        new(0.10, 0.85, 0.90, 1),  // -x cyan
        new(0.95, 0.85, 0.10, 1),  // +y yellow
        new(0.20, 0.30, 0.95, 1),  // -y blue
        new(0.15, 0.85, 0.25, 1),  // +z green
        new(0.90, 0.20, 0.85, 1),  // -z magenta
    ];

    static readonly NSFont captionFont = NSFont.MonospacedSystemFont(17, NSFontWeight.Semibold);

    struct Quad
    {
        public CGPoint P0, P1, P2, P3;
        public double Depth;
        public int Color;
        public double Shade;
        public double Spec;
    }

    // Reused every frame instead of reallocating the mesh.
    readonly V3[,] verts = new V3[nTheta + 1, nPhi];
    readonly V3[,] dirs = new V3[nTheta + 1, nPhi];
    readonly List<Quad> quads = new(nTheta * nPhi);

    public OrbitalBoxView(CGRect frame, bool isPreview) : base(frame, isPreview)
    {
        AnimationTimeInterval = 1.0 / 30.0;
        cx = Math.Max(Bounds.MidX, 200);
        cy = Math.Max(Bounds.MidY, 150);
        ConfigureFromSettings();
    }

    void ConfigureFromSettings()
    {
        spinMul = Math.Max(0.2, Math.Min(3, Settings.Value("OrbitalBox", "spin", 1.0)));
        double morphMul = Math.Max(0.3, Math.Min(3, Settings.Value("OrbitalBox", "morph", 1.0)));
        holdFrames = Math.Max(12, (int)(80.0 / morphMul));
        morphFrames = Math.Max(20, (int)(66.0 / morphMul));
    }

    public override void AnimateOneFrame()
    {
        tick += 1;
        rotX += 0.011 * spinMul;
        rotY += 0.019 * spinMul;
        double w = Math.Max(Bounds.Width, 640), h = Math.Max(Bounds.Height, 400);
        double R = Math.Min(w, h) * 0.29;
        cx += vx; cy += vy;
        if (cx < R) { cx = R; vx = Math.Abs(vx); }
        if (cx > w - R) { cx = w - R; vx = -Math.Abs(vx); }
        if (cy < R) { cy = R; vy = Math.Abs(vy); }
        if (cy > h - R) { cy = h - R; vy = -Math.Abs(vy); }
        if (tick >= holdFrames + morphFrames)
        {
            tick = 0;
            shapeIdx = (shapeIdx + 1) % shapes.Length;
        }
        NeedsDisplay = true;
    }

    public override void Draw(CGContext ctx)
    {
        ctx.SetFillColor(new CGColor(0, 0, 0, 1));
        ctx.Fill(Bounds);

        // Morph blend between the current shape and the next
        int next = (shapeIdx + 1) % shapes.Length;
        double u = 0.0;
        if (tick > holdFrames)
        {
            double raw = (double)(tick - holdFrames) / morphFrames;
            u = raw * raw * (3 - 2 * raw);  // smoothstep
        }
        var fA = shapes[shapeIdx].f;
        var fB = shapes[next].f;

        // Build the mesh in model space, remembering model-space directions
        // so the face colors are painted onto the shape and rotate with it.
        double cxr = Math.Cos(rotX), sxr = Math.Sin(rotX);
        double cyr = Math.Cos(rotY), syr = Math.Sin(rotY);
        V3 Rotate(V3 v)
        {
            double y1 = v.Y * cxr - v.Z * sxr;
            double z1 = v.Y * sxr + v.Z * cxr;
            double x2 = v.X * cyr + z1 * syr;
            double z2 = -v.X * syr + z1 * cyr;
            return new V3(x2, y1, z2);
        }

        for (int i = 0; i <= nTheta; i++)
        {
            double th = (double)i / nTheta * Math.PI;
            for (int j = 0; j < nPhi; j++)
            {
                double ph = (double)j / nPhi * 2 * Math.PI;
                double mag = (1 - u) * fA(th, ph) + u * fB(th, ph);
                double r = 0.18 + 0.82 * mag;
                var dir = new V3(Math.Sin(th) * Math.Cos(ph), Math.Cos(th), Math.Sin(th) * Math.Sin(ph));
                verts[i, j] = Rotate(dir * r);
                dirs[i, j] = dir;
            }
        }

        double Rr = Math.Min(Math.Max(Bounds.Width, 640), Math.Max(Bounds.Height, 400)) * 0.29;
        const double D = 3.3;
        var light = V3.Normalize(new V3(0.35, 0.55, 0.75));
        var half = V3.Normalize(light + new V3(0, 0, 1));

        quads.Clear();
        CGPoint Project(V3 p)
        {
            double denom = D - p.Z;
            return new CGPoint(cx + p.X * Rr * 2.5 / denom, cy + p.Y * Rr * 2.5 / denom);
        }

        for (int i = 0; i < nTheta; i++)
        {
            for (int j = 0; j < nPhi; j++)
            {
                int j2 = (j + 1) % nPhi;
                V3 v0 = verts[i, j], v1 = verts[i, j2], v2 = verts[i + 1, j2], v3 = verts[i + 1, j];
                var center = (v0 + v1 + v2 + v3) * 0.25;
                var n = V3.Cross(v2 - v0, v3 - v1);
                double nl = V3.Length(n);
                if (nl < 1e-9) continue;
                n /= nl;
                if (V3.Dot(n, center) < 0) n = -n;
                var mdir = (dirs[i, j] + dirs[i, j2] + dirs[i + 1, j2] + dirs[i + 1, j]) * 0.25;
                double ax = Math.Abs(mdir.X), ay = Math.Abs(mdir.Y), az = Math.Abs(mdir.Z);
                int color;
                if (ax >= ay && ax >= az) color = mdir.X >= 0 ? 0 : 1;
                else if (ay >= ax && ay >= az) color = mdir.Y >= 0 ? 2 : 3;
                else color = mdir.Z >= 0 ? 4 : 5;
                double shade = 0.32 + 0.68 * Math.Max(0, V3.Dot(n, light));
                double spec = Math.Pow(Math.Max(0, V3.Dot(n, half)), 20);
                quads.Add(new Quad
                {
                    P0 = Project(v0), P1 = Project(v1), P2 = Project(v2), P3 = Project(v3),
                    Depth = center.Z, Color = color, Shade = shade, Spec = spec,
                });
            }
        }
        // Swift's sort is introsort (unstable) too; ties are invisible here.
        quads.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        foreach (var q in quads)
        {
            var comps = palette[q.Color].Components;
            double r = Math.Min(comps[0] * q.Shade + q.Spec * 0.9, 1);
            double g = Math.Min(comps[1] * q.Shade + q.Spec * 0.9, 1);
            double b = Math.Min(comps[2] * q.Shade + q.Spec * 0.9, 1);
            ctx.SetFillColor(new CGColor(r, g, b, 1));
            ctx.Move(q.P0);
            ctx.AddLine(q.P1);
            ctx.AddLine(q.P2);
            ctx.AddLine(q.P3);
            ctx.ClosePath();
            ctx.FillPath();
        }

        // Corner caption naming the current orbital
        string name = u < 0.5 ? shapes[shapeIdx].name : shapes[next].name;
        double alpha = tick <= holdFrames ? 0.6 : 0.6 * Math.Abs(1 - 2 * u);
        new NSAttributedString(name, captionFont, NSColor.CalibratedWhite(0.8, alpha))
            .Draw(ctx, new CGPoint(26, 22));
    }
}
