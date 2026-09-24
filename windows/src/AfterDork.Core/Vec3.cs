namespace AfterDork;

/// <summary>SIMD3&lt;Double&gt; with the simd_* helpers the savers use.</summary>
public readonly record struct V3(double X, double Y, double Z)
{
    public static readonly V3 Zero = new(0, 0, 0);
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator -(V3 a) => new(-a.X, -a.Y, -a.Z);
    public static V3 operator *(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static V3 operator *(double s, V3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static V3 operator *(V3 a, V3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static V3 operator /(V3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static V3 Cross(V3 a, V3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static double Length(V3 a) => Math.Sqrt(Dot(a, a));
    public static V3 Normalize(V3 a) { double l = Length(a); return l == 0 ? a : a / l; }
}

/// <summary>SIMD3&lt;Int&gt;.</summary>
public readonly record struct I3(int X, int Y, int Z)
{
    public static I3 operator +(I3 a, I3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static I3 operator -(I3 a, I3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static I3 operator -(I3 a) => new(-a.X, -a.Y, -a.Z);
    public static I3 operator *(I3 a, int s) => new(a.X * s, a.Y * s, a.Z * s);
}
