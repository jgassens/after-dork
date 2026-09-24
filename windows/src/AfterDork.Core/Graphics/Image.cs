using System.Runtime.InteropServices;
using SkiaSharp;

namespace AfterDork.Graphics;

/// <summary>An immutable image, like CGImage.</summary>
public sealed class CGImage : IDisposable
{
    internal readonly SKImage Sk;
    public int Width => Sk.Width;
    public int Height => Sk.Height;

    internal CGImage(SKImage sk) => Sk = sk;

    /// <summary>
    /// Builds an image from 32-bit pixels laid out like a CoreGraphics
    /// premultipliedFirst|byteOrder32Little (or noneSkipFirst) buffer:
    /// as a uint each pixel is 0xAARRGGBB, rows top-to-bottom.
    /// </summary>
    public static CGImage FromPixels(ReadOnlySpan<uint> pixels, int width, int height, bool opaque = false)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, opaque ? SKAlphaType.Opaque : SKAlphaType.Premul);
        var bytes = MemoryMarshal.AsBytes(pixels);
        unsafe
        {
            fixed (byte* p = bytes)
            {
                using var pixmap = new SKPixmap(info, (IntPtr)p, width * 4);
                return new CGImage(SKImage.FromPixelCopy(pixmap));
            }
        }
    }

    public static CGImage? FromEncoded(byte[] data)
    {
        var img = SKImage.FromEncodedData(data);
        return img is null ? null : new CGImage(img);
    }

    /// <summary>Loads an image embedded in AfterDork.Core (e.g. "pauling.png").</summary>
    public static CGImage? FromResource(string name)
    {
        using var s = typeof(CGImage).Assembly.GetManifestResourceStream(name);
        if (s is null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return FromEncoded(ms.ToArray());
    }

    public void Dispose() => Sk.Dispose();
}

/// <summary>
/// An offscreen drawing context, like CGContext(data: nil, width:, height:, …).
/// Drawing is y-up (row 0 of <see cref="Pixels"/> is the top of the image, as
/// in a CoreGraphics bitmap context). Pixels are BGRA premultiplied, i.e.
/// 0xAARRGGBB when read as a uint — the premultipliedFirst|byteOrder32Little
/// layout. Starts fully transparent.
/// </summary>
public sealed class BitmapContext : CGContext
{
    readonly SKBitmap bitmap;
    public int Width => bitmap.Width;
    public int PixelHeight => bitmap.Height;

    public BitmapContext(int width, int height) : this(Alloc(width, height)) { }

    BitmapContext(SKBitmap b) : base(new SKCanvas(b), b.Height) => bitmap = b;

    static SKBitmap Alloc(int w, int h)
    {
        var b = new SKBitmap(new SKImageInfo(Math.Max(1, w), Math.Max(1, h), SKColorType.Bgra8888, SKAlphaType.Premul));
        b.Erase(SKColors.Transparent);
        return b;
    }

    public Span<uint> Pixels
    {
        get
        {
            Canvas.Flush();
            return MemoryMarshal.Cast<byte, uint>(bitmap.GetPixelSpan());
        }
    }

    public uint[] ReadPixels() => Pixels.ToArray();

    public CGImage MakeImage()
    {
        Canvas.Flush();
        return new CGImage(SKImage.FromBitmap(bitmap));  // copies: the bitmap stays mutable
    }

    public override void Dispose()
    {
        base.Dispose();
        Canvas.Dispose();
        bitmap.Dispose();
    }
}
