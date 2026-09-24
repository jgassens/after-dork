using SkiaSharp;

namespace AfterDork.Graphics;

/// <summary>
/// CGPath / CGMutablePath over SKPath. Coordinates are stored as given (user
/// space, y-up); the y-flip lives in the canvas matrix, so arc and winding
/// maths are identical to CoreGraphics.
/// </summary>
public class CGPath
{
    internal readonly SKPath Sk;

    public CGPath() => Sk = new SKPath { FillType = SKPathFillType.Winding };
    internal CGPath(SKPath sk) => Sk = sk;

    public static CGPath Rect(CGRect r) { var p = new CGMutablePath(); p.AddRect(r); return p; }
    public static CGPath Ellipse(CGRect r) { var p = new CGMutablePath(); p.AddEllipse(r); return p; }
    public static CGPath RoundedRect(CGRect r, double cornerWidth, double cornerHeight)
    {
        var p = new CGMutablePath();
        p.AddRoundedRect(r, cornerWidth, cornerHeight);
        return p;
    }

    public bool IsEmpty => Sk.IsEmpty;
    public CGPoint CurrentPoint { get { var l = Sk.LastPoint; return new CGPoint(l.X, l.Y); } }
    public CGRect BoundingBox { get { var b = Sk.Bounds; return new CGRect(b.Left, b.Top, b.Width, b.Height); } }

    public CGMutablePath MutableCopy() => new(new SKPath(Sk));

    public void Move(CGPoint to) => PathOps.Move(Sk, to);
    public void AddLine(CGPoint to) => PathOps.AddLine(Sk, to);
    public void AddLines(ReadOnlySpan<CGPoint> pts) => PathOps.AddLines(Sk, pts);
    public void AddQuadCurve(CGPoint to, CGPoint control) => PathOps.AddQuadCurve(Sk, to, control);
    public void AddCurve(CGPoint to, CGPoint control1, CGPoint control2) => PathOps.AddCurve(Sk, to, control1, control2);
    public void AddArc(CGPoint center, double radius, double startAngle, double endAngle, bool clockwise) =>
        PathOps.AddArc(Sk, center, radius, startAngle, endAngle, clockwise);
    public void AddRect(CGRect r) => PathOps.AddRect(Sk, r);
    public void AddEllipse(CGRect r) => PathOps.AddEllipse(Sk, r);
    public void AddRoundedRect(CGRect r, double cornerWidth, double cornerHeight) =>
        PathOps.AddRoundedRect(Sk, r, cornerWidth, cornerHeight);
    public void AddPath(CGPath other) => Sk.AddPath(other.Sk, SKPathAddMode.Append);
    public void CloseSubpath() => Sk.Close();

    public bool Contains(CGPoint p, bool evenOdd = false)
    {
        var old = Sk.FillType;
        Sk.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        bool r = Sk.Contains((float)p.X, (float)p.Y);
        Sk.FillType = old;
        return r;
    }
}

public sealed class CGMutablePath : CGPath
{
    public CGMutablePath() { }
    internal CGMutablePath(SKPath sk) : base(sk) { }
}

/// <summary>Path construction shared by CGPath and the context's current path.</summary>
internal static class PathOps
{
    const double TwoPi = Math.PI * 2;

    public static void Move(SKPath p, CGPoint to) => p.MoveTo((float)to.X, (float)to.Y);

    public static void AddLine(SKPath p, CGPoint to)
    {
        // CoreGraphics requires a current point; Skia would silently insert
        // a moveTo(0,0). Treat a line with no current point as a move.
        if (p.PointCount == 0) p.MoveTo((float)to.X, (float)to.Y);
        else p.LineTo((float)to.X, (float)to.Y);
    }

    public static void AddLines(SKPath p, ReadOnlySpan<CGPoint> pts)
    {
        if (pts.Length == 0) return;
        Move(p, pts[0]);
        for (int i = 1; i < pts.Length; i++) p.LineTo((float)pts[i].X, (float)pts[i].Y);
    }

    public static void AddQuadCurve(SKPath p, CGPoint to, CGPoint c) =>
        p.QuadTo((float)c.X, (float)c.Y, (float)to.X, (float)to.Y);

    public static void AddCurve(SKPath p, CGPoint to, CGPoint c1, CGPoint c2) =>
        p.CubicTo((float)c1.X, (float)c1.Y, (float)c2.X, (float)c2.Y, (float)to.X, (float)to.Y);

    /// <summary>
    /// CGContextAddArc semantics: angles in radians measured in user space;
    /// clockwise == false sweeps toward increasing angle. A sweep of 2π or
    /// more draws a full circle. A current point gets a connecting line.
    /// </summary>
    public static void AddArc(SKPath p, CGPoint c, double r, double start, double end, bool clockwise)
    {
        double sweep = ArcSweep(start, end, clockwise);
        var oval = new SKRect((float)(c.X - r), (float)(c.Y - r), (float)(c.X + r), (float)(c.Y + r));
        double startDeg = start * 180 / Math.PI;
        double sweepDeg = sweep * 180 / Math.PI;
        if (r <= 0 || sweepDeg == 0)
        {
            var pt = new CGPoint(c.X + r * Math.Cos(start), c.Y + r * Math.Sin(start));
            AddLine(p, pt);
            return;
        }
        // Skia's arcTo misbehaves near ±360°; split large sweeps into halves.
        if (Math.Abs(sweepDeg) > 180)
        {
            double half = sweepDeg / 2;
            p.ArcTo(oval, (float)startDeg, (float)half, false);
            p.ArcTo(oval, (float)(startDeg + half), (float)half, false);
        }
        else
        {
            p.ArcTo(oval, (float)startDeg, (float)sweepDeg, false);
        }
    }

    public static double ArcSweep(double start, double end, bool clockwise)
    {
        double d = end - start;
        if (!clockwise)
        {
            if (d >= TwoPi) return TwoPi;
            d %= TwoPi;
            if (d < 0) d += TwoPi;
        }
        else
        {
            if (d <= -TwoPi) return -TwoPi;
            d %= TwoPi;
            if (d > 0) d -= TwoPi;
        }
        return d;
    }

    // Rects and ellipses both use Skia's "clockwise", which is the same
    // winding for both (increasing angle) — as in CoreGraphics, where
    // an ellipse overlapping a rect in one nonzero fill leaves no hole.
    public static void AddRect(SKPath p, CGRect r) => p.AddRect(r.ToSK(), SKPathDirection.Clockwise);

    public static void AddEllipse(SKPath p, CGRect r) => p.AddOval(r.ToSK(), SKPathDirection.Clockwise);

    public static void AddRoundedRect(SKPath p, CGRect r, double cw, double ch)
    {
        var s = r.ToSK();
        float rx = (float)Math.Min(cw, s.Width / 2), ry = (float)Math.Min(ch, s.Height / 2);
        p.AddRoundRect(new SKRoundRect(s, rx, ry), SKPathDirection.Clockwise);
    }
}
