using AfterDork.Graphics;
using SkiaSharp;

namespace AfterDork.Cli;

// Port of scripts/make_icon.swift: a beveled 1996 CRT monitor with a winged
// Erlenmeyer flying across a starfield on the tube. Writes a Windows .ico
// (PNG-compressed frames) instead of a macOS .iconset.
internal static class IconArt
{
    static void Draw(double S, CGContext ctx)
    {
        double u = S / 128.0;  // design unit: art authored on a 128 grid

        CGPath Rr(double x, double y, double w, double h, double r) =>
            CGPath.RoundedRect(new CGRect(x * u, y * u, w * u, h * u), r * u, r * u);

        // CRT body
        var body = Rr(6, 8, 116, 112, 10);
        ctx.AddPath(body);
        ctx.SetFillColor(new CGColor(0.78, 0.78, 0.76, 1));
        ctx.FillPath();
        // Bevel: light top-left, dark bottom-right
        ctx.SaveGState();
        ctx.AddPath(body);
        ctx.Clip();
        ctx.SetStrokeColor(new CGColor(0.97, 0.97, 0.95, 1));
        ctx.SetLineWidth(5 * u);
        ctx.Move(new CGPoint(4 * u, 12 * u));
        ctx.AddLine(new CGPoint(4 * u, 122 * u));
        ctx.AddLine(new CGPoint(124 * u, 122 * u));
        ctx.StrokePath();
        ctx.SetStrokeColor(new CGColor(0.42, 0.42, 0.40, 1));
        ctx.Move(new CGPoint(5 * u, 9 * u));
        ctx.AddLine(new CGPoint(123 * u, 9 * u));
        ctx.AddLine(new CGPoint(123 * u, 120 * u));
        ctx.StrokePath();
        ctx.RestoreGState();

        // Screen (sunken)
        var screen = new CGRect(16 * u, 34 * u, 96 * u, 76 * u);
        ctx.SetFillColor(new CGColor(0.29, 0.29, 0.28, 1));
        ctx.Fill(screen.InsetBy(-2.5 * u, -2.5 * u));
        ctx.SetFillColor(new CGColor(0.02, 0.02, 0.10, 1));
        ctx.Fill(screen);
        // Phosphor glow at the bottom of the tube
        var grad = new CGGradient([new CGColor(0.05, 0.16, 0.10, 1), new CGColor(0.02, 0.02, 0.10, 0)], [0, 1]);
        ctx.SaveGState();
        ctx.Clip(screen);
        ctx.DrawLinearGradient(grad, new CGPoint(0, screen.MinY), new CGPoint(0, screen.MidY), CGGradientDrawingOptions.None);
        ctx.RestoreGState();
        // Stars
        ctx.SaveGState();
        ctx.Clip(screen);
        ulong seed = 0xC0FFEE;
        for (int i = 0; i < 26; i++)
        {
            seed = unchecked(seed * 6364136223846793005UL + 1442695040888963407UL);
            double sx = screen.MinX + (seed % 1000) / 1000.0 * screen.Width;
            double sy = screen.MinY + ((seed >> 12) % 1000) / 1000.0 * screen.Height;
            double b = 0.4 + ((seed >> 24) % 100) / 160.0;
            ctx.SetFillColor(new CGColor(0.9, 0.92, 1, b));
            ctx.Fill(new CGRect(sx, sy, 1.6 * u, 1.6 * u));
        }

        // Winged Erlenmeyer, centered on the tube
        ctx.TranslateBy(screen.MidX, screen.MidY - 2 * u);
        ctx.ScaleBy(0.9 * u, 0.9 * u);
        // Wings
        var wing = new CGMutablePath();
        wing.Move(CGPoint.Zero);
        wing.AddCurve(new CGPoint(30, 34), new CGPoint(8, 16), new CGPoint(24, 30));
        wing.AddQuadCurve(new CGPoint(30, 18), new CGPoint(38, 26));
        wing.AddQuadCurve(new CGPoint(24, 7), new CGPoint(33, 11));
        wing.AddQuadCurve(new CGPoint(14, -1), new CGPoint(24, 0));
        wing.AddQuadCurve(CGPoint.Zero, new CGPoint(6, -3));
        wing.CloseSubpath();
        foreach (double side in new double[] { 1, -1 })
        {
            ctx.SaveGState();
            ctx.ScaleBy(side, 1);
            ctx.TranslateBy(11, 6);
            ctx.RotateBy(0.55);
            ctx.AddPath(wing);
            ctx.SetFillColor(new CGColor(0.97, 0.97, 1, 0.97));
            ctx.FillPath();
            ctx.AddPath(wing);
            ctx.SetStrokeColor(new CGColor(0.55, 0.58, 0.7, 0.9));
            ctx.SetLineWidth(1.4);
            ctx.StrokePath();
            ctx.RestoreGState();
        }
        // Flask body
        var flask = new CGMutablePath();
        flask.Move(new CGPoint(-20, -28));
        flask.AddLine(new CGPoint(20, -28));
        flask.AddLine(new CGPoint(7, 6));
        flask.AddLine(new CGPoint(7, 26));
        flask.AddLine(new CGPoint(9, 28));
        flask.AddLine(new CGPoint(9, 31));
        flask.AddLine(new CGPoint(-9, 31));
        flask.AddLine(new CGPoint(-9, 28));
        flask.AddLine(new CGPoint(-7, 26));
        flask.AddLine(new CGPoint(-7, 6));
        flask.CloseSubpath();
        ctx.AddPath(flask);
        ctx.SetFillColor(new CGColor(0.85, 0.9, 1.0, 0.16));
        ctx.FillPath();
        ctx.SaveGState();
        ctx.AddPath(flask);
        ctx.Clip();
        ctx.SetFillColor(new CGColor(0.22, 0.85, 0.35, 0.95));
        ctx.Fill(new CGRect(-22, -30, 44, 22));
        ctx.SetFillColor(new CGColor(0.5, 1.0, 0.6, 0.9));
        ctx.FillEllipse(new CGRect(-16, -10.5, 32, 5));
        ctx.RestoreGState();
        ctx.AddPath(flask);
        ctx.SetStrokeColor(new CGColor(0.92, 0.95, 1.0, 0.95));
        ctx.SetLineWidth(2.4);
        ctx.SetLineJoin(CGLineJoin.Round);
        ctx.StrokePath();
        ctx.RestoreGState();

        // Control strip: vents + power LED
        ctx.SetFillColor(new CGColor(0.55, 0.55, 0.53, 1));
        for (int i = 0; i < 3; i++)
            ctx.Fill(new CGRect((18 + i * 13) * u, 19 * u, 9 * u, 3 * u));
        ctx.SetFillColor(new CGColor(0.2, 0.85, 0.3, 1));
        ctx.FillEllipse(new CGRect(102 * u, 17.5 * u, 6.5 * u, 6.5 * u));
    }

    public static byte[] RenderPng(int px)
    {
        using var ctx = new BitmapContext(px, px);
        Draw(px, ctx);
        using var img = ctx.MakeImage();
        using var data = img.Sk.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Writes a multi-resolution .ico with PNG frames.</summary>
    public static void WriteIco(string path, int[] sizes)
    {
        var frames = sizes.Select(RenderPng).ToArray();
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(frames[i].Length);
            w.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var f in frames) w.Write(f);
    }
}
