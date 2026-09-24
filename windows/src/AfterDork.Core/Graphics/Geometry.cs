using SkiaSharp;

namespace AfterDork.Graphics;

// Double-precision stand-ins for the CoreGraphics value types (CGFloat is a
// 64-bit double on the Mac). Names follow CoreGraphics so ported saver code
// reads the same as the Swift original.

public readonly record struct CGPoint(double X, double Y)
{
    public static readonly CGPoint Zero = new(0, 0);
    public static CGPoint operator +(CGPoint a, CGVector v) => new(a.X + v.Dx, a.Y + v.Dy);
    public static CGPoint operator -(CGPoint a, CGVector v) => new(a.X - v.Dx, a.Y - v.Dy);
    public CGPoint Offset(double dx, double dy) => new(X + dx, Y + dy);
    public SKPoint ToSK() => new((float)X, (float)Y);
}

public readonly record struct CGVector(double Dx, double Dy)
{
    public static readonly CGVector Zero = new(0, 0);
}

public readonly record struct CGSize(double Width, double Height)
{
    public static readonly CGSize Zero = new(0, 0);
}

public readonly record struct CGRect(double X, double Y, double Width, double Height)
{
    public static readonly CGRect Zero = new(0, 0, 0, 0);

    public CGRect(CGPoint origin, CGSize size) : this(origin.X, origin.Y, size.Width, size.Height) { }

    public CGPoint Origin => new(X, Y);
    public CGSize Size => new(Width, Height);

    // CoreGraphics' accessors standardize negative sizes; so do these.
    public double MinX => Width >= 0 ? X : X + Width;
    public double MaxX => Width >= 0 ? X + Width : X;
    public double MinY => Height >= 0 ? Y : Y + Height;
    public double MaxY => Height >= 0 ? Y + Height : Y;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;
    public CGPoint Mid => new(MidX, MidY);
    public bool IsEmpty => Width == 0 || Height == 0;

    public CGRect Standardized => new(MinX, MinY, Math.Abs(Width), Math.Abs(Height));

    public CGRect InsetBy(double dx, double dy)
    {
        var s = Standardized;
        return new CGRect(s.X + dx, s.Y + dy, s.Width - 2 * dx, s.Height - 2 * dy);
    }

    public CGRect OffsetBy(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    public bool Contains(CGPoint p) => p.X >= MinX && p.X < MaxX && p.Y >= MinY && p.Y < MaxY;

    public SKRect ToSK() => new((float)MinX, (float)MinY, (float)MaxX, (float)MaxY);
}

public enum CGLineCap { Butt, Round, Square }
public enum CGLineJoin { Miter, Round, Bevel }
public enum CGInterpolationQuality { Default, None, Low, Medium, High }
