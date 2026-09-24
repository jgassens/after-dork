using System.Collections.Concurrent;
using System.Text;
using SkiaSharp;

namespace AfterDork.Graphics;

public enum NSFontWeight { Regular, Medium, Semibold, Bold, Heavy }

/// <summary>
/// NSFont stand-in. Point sizes map 1:1 to user-space units. Mac system
/// fonts map to their Windows counterparts: SF → Segoe UI, SF Mono/Menlo →
/// Consolas.
/// </summary>
public sealed class NSFont
{
    public const string SystemFamily = "Segoe UI";
    public const string MonoFamily = "Consolas";

    internal readonly SKFont Sk;
    public double PointSize { get; }

    NSFont(string family, bool bold, double size)
    {
        var tf = SKTypeface.FromFamilyName(family, bold ? SKFontStyle.Bold : SKFontStyle.Normal)
                 ?? SKTypeface.Default;
        Sk = new SKFont(tf, (float)size) { Subpixel = true, Edging = SKFontEdging.Antialias };
        PointSize = size;
    }

    static readonly ConcurrentDictionary<(string, bool, double), NSFont> cache = new();
    static NSFont Get(string family, bool bold, double size) =>
        cache.GetOrAdd((family, bold, size), k => new NSFont(k.Item1, k.Item2, k.Item3));

    public static NSFont SystemFont(double size) => Get(SystemFamily, false, size);
    public static NSFont BoldSystemFont(double size) => Get(SystemFamily, true, size);
    public static NSFont SystemFont(double size, NSFontWeight weight) => Get(SystemFamily, weight >= NSFontWeight.Semibold, size);
    public static NSFont MonospacedSystemFont(double size, NSFontWeight weight) => Get(MonoFamily, weight >= NSFontWeight.Semibold, size);
    public static NSFont MonospacedDigitSystemFont(double size, NSFontWeight weight) => Get(SystemFamily, weight >= NSFontWeight.Semibold, size);

    /// <summary>NSFont(name:size:). Menlo maps to Consolas; unknown names return null like AppKit.</summary>
    public static NSFont? Named(string name, double size)
    {
        bool bold = name.EndsWith("-Bold", StringComparison.OrdinalIgnoreCase);
        string fam = name.Split('-')[0];
        if (fam.Equals("Menlo", StringComparison.OrdinalIgnoreCase) || fam.Equals("Monaco", StringComparison.OrdinalIgnoreCase))
            fam = MonoFamily;
        using var probe = SKTypeface.FromFamilyName(fam);
        if (probe is null || !probe.FamilyName.Equals(fam, StringComparison.OrdinalIgnoreCase)) return null;
        return Get(fam, bold, size);
    }

    /// <summary>Distance from baseline to top of the line box (positive).</summary>
    public double Ascent => -Sk.Metrics.Ascent;
    /// <summary>Distance from baseline to bottom of the line box (positive).</summary>
    public double Descent => Sk.Metrics.Descent;
    public double Leading => Sk.Metrics.Leading;
    public double LineHeight => Ascent + Descent + Leading;

    // Font fallback for characters the primary face lacks (≡, ₃, ⁺ …).
    readonly ConcurrentDictionary<int, SKFont> fallback = new();

    internal SKFont FontFor(int codepoint)
    {
        if (Sk.Typeface.GetGlyph(codepoint) != 0) return Sk;
        return fallback.GetOrAdd(codepoint, cp =>
        {
            var tf = SKFontManager.Default.MatchCharacter(Sk.Typeface.FamilyName, Sk.Typeface.FontStyle, null, cp);
            return tf is null ? Sk : new SKFont(tf, Sk.Size) { Subpixel = true, Edging = SKFontEdging.Antialias };
        });
    }

    /// <summary>Splits text into runs that share a font.</summary>
    internal IEnumerable<(string text, SKFont font)> Runs(string s)
    {
        var sb = new StringBuilder();
        SKFont? cur = null;
        foreach (var rune in s.EnumerateRunes())
        {
            var f = FontFor(rune.Value);
            if (cur is not null && !ReferenceEquals(f, cur))
            {
                yield return (sb.ToString(), cur);
                sb.Clear();
            }
            cur = f;
            sb.Append(rune.ToString());
        }
        if (cur is not null && sb.Length > 0) yield return (sb.ToString(), cur);
    }

    public double MeasureWidth(string s)
    {
        double w = 0;
        foreach (var (t, f) in Runs(s)) w += f.MeasureText(t);
        return w;
    }
}

/// <summary>
/// The slice of NSAttributedString the savers use: a single font and colour,
/// measured with Size() and drawn with Draw(at:). Draw places the bottom-left
/// corner of the line box at the point with glyphs upright — how
/// NSAttributedString.draw(at:) behaves in an unflipped view — and follows the
/// context's current transform.
/// </summary>
public sealed class NSAttributedString
{
    public string String { get; }
    public NSFont Font { get; }
    public CGColor Color { get; }

    public NSAttributedString(string s, NSFont font, CGColor color)
    {
        String = s;
        Font = font;
        Color = color;
    }

    public CGSize Size() => new(Font.MeasureWidth(String), Font.LineHeight);

    public void Draw(CGContext ctx, CGPoint at) =>
        ctx.DrawTextAtBaseline(String, new CGPoint(at.X, at.Y + Font.Descent), Font, Color);
}

public static class TextDrawing
{
    /// <summary>Draws text with its baseline starting at the given y-up point (CTLineDraw equivalent).</summary>
    public static void DrawTextAtBaseline(this CGContext ctx, string s, CGPoint baseline, NSFont font, CGColor color)
    {
        var canvas = ctx.Canvas;
        canvas.Save();
        canvas.Translate((float)baseline.X, (float)baseline.Y);
        canvas.Scale(1, -1);  // local y-down so glyphs stand upright
        using var paint = new SKPaint { Color = ctx.WithAlpha(color), IsAntialias = ctx.Antialias };
        float x = 0;
        foreach (var (t, f) in font.Runs(s))
        {
            var edging = f.Edging;
            f.Edging = ctx.Antialias ? SKFontEdging.Antialias : SKFontEdging.Alias;
            canvas.DrawText(t, x, 0, f, paint);
            x += f.MeasureText(t);
            f.Edging = edging;
        }
        canvas.Restore();
    }
}
