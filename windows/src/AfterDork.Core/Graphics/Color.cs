using SkiaSharp;

namespace AfterDork.Graphics;

/// <summary>Device-RGB colour with unpremultiplied components in 0…1, like CGColor.</summary>
public readonly record struct CGColor(double Red, double Green, double Blue, double Alpha = 1)
{
    public static readonly CGColor Black = new(0, 0, 0, 1);
    public static readonly CGColor White = new(1, 1, 1, 1);
    public static readonly CGColor Clear = new(0, 0, 0, 0);

    public static CGColor Gray(double white, double alpha = 1) => new(white, white, white, alpha);

    /// <summary>Matches CGColor.components for an RGB colour: [r, g, b, a].</summary>
    public double[] Components => [Red, Green, Blue, Alpha];

    public CGColor CopyWithAlpha(double alpha) => this with { Alpha = alpha };

    // NSColor-style accessors, so `.redComponent` etc. port directly.
    public double RedComponent => Red;
    public double GreenComponent => Green;
    public double BlueComponent => Blue;
    public double AlphaComponent => Alpha;

    public SKColor ToSK() => new(B(Red), B(Green), B(Blue), B(Alpha));

    static byte B(double v) => (byte)Math.Clamp(Math.Round(v * 255.0), 0, 255);
}

/// <summary>
/// The NSColor constructors the savers use. "Calibrated" colour spaces are
/// treated as sRGB; the difference is invisible at screensaver fidelity.
/// </summary>
public static class NSColor
{
    public static readonly CGColor White = CGColor.White;
    public static readonly CGColor Black = CGColor.Black;

    public static CGColor CalibratedRed(double red, double green, double blue, double alpha = 1) =>
        new(red, green, blue, alpha);

    public static CGColor CalibratedWhite(double white, double alpha = 1) => CGColor.Gray(white, alpha);

    /// <summary>HSB → RGB with hue in 0…1 (values outside wrap, as AppKit does).</summary>
    public static CGColor CalibratedHue(double hue, double saturation, double brightness, double alpha = 1)
    {
        double h = hue % 1.0;
        if (h < 0) h += 1;
        double s = Math.Clamp(saturation, 0, 1), v = Math.Clamp(brightness, 0, 1);
        if (s == 0) return new CGColor(v, v, v, alpha);
        double hf = h * 6;
        int i = (int)Math.Floor(hf) % 6;
        double f = hf - Math.Floor(hf);
        double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        return i switch
        {
            0 => new CGColor(v, t, p, alpha),
            1 => new CGColor(q, v, p, alpha),
            2 => new CGColor(p, v, t, alpha),
            3 => new CGColor(p, q, v, alpha),
            4 => new CGColor(t, p, v, alpha),
            _ => new CGColor(v, p, q, alpha),
        };
    }
}
