using System.Diagnostics;
using AfterDork.Graphics;
using SkiaSharp;

namespace AfterDork.App;

/// <summary>
/// A control that shows a running saver: renders the SaverView into a Skia
/// bitmap at device resolution and blits it straight to the window. The
/// view's bounds are in logical points (pixels ÷ DPI scale), like a Retina
/// ScreenSaverView, so savers look the same size as on the Mac.
/// </summary>
internal sealed class SaverSurface : Control
{
    SKBitmap? bitmap;
    SKCanvas? canvas;
    SaverView? view;

    public SaverSurface()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        BackColor = Color.Black;
    }

    public float PixelScale => DeviceDpi / 96f;

    /// <summary>Logical size a view should be created with to fill this surface.</summary>
    public CGRect LogicalBounds => new(0, 0, ClientSize.Width / PixelScale, ClientSize.Height / PixelScale);

    public SaverView? View
    {
        get => view;
        set
        {
            view?.StopAnimation();
            view = value;
            view?.StartAnimation();
            if (view is null) FrameClock.Unregister(this); else FrameClock.Register(this);
            Invalidate();
        }
    }

    /// <summary>Builds a view for the module sized to this surface (or to an explicit logical size).</summary>
    public void Show(Module module, bool preview, CGSize? logicalSize = null)
    {
        var size = logicalSize ?? LogicalBounds.Size;
        View = module.Make(new CGRect(0, 0, Math.Max(1, size.Width), Math.Max(1, size.Height)), preview);
    }

    internal void Tick(int frames)
    {
        if (view is null) return;
        for (int i = 0; i < frames; i++) view.AnimateOneFrame();
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
        if (bitmap is null || bitmap.Width != w || bitmap.Height != h)
        {
            canvas?.Dispose();
            bitmap?.Dispose();
            bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            canvas = new SKCanvas(bitmap);
        }
        if (view is not null)
        {
            // Scale the logical view to exactly fill the surface.
            float sx = (float)(w / view.Bounds.Width);
            SaverRenderer.Render(canvas!, view, sx);
        }
        else
        {
            canvas!.Clear(SKColors.Black);
        }
        canvas!.Flush();
        Blit(e.Graphics, bitmap, w, h);
    }

    static void Blit(System.Drawing.Graphics g, SKBitmap bmp, int w, int h)
    {
        var bi = new Native.BITMAPINFOHEADER
        {
            biSize = 40,
            biWidth = w,
            biHeight = -h,  // top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr hdc = g.GetHdc();
        try
        {
            Native.SetDIBitsToDevice(hdc, 0, 0, (uint)w, (uint)h, 0, 0, 0, (uint)h, bmp.GetPixels(), ref bi, 0);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            View = null;
            canvas?.Dispose();
            bitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// One 30 fps fixed-step clock for every live surface in the process (the
/// control panel's preview and any demo windows), like the Mac app's single
/// animation timer. Raises the system timer resolution while running so the
/// cadence is even.
/// </summary>
internal static class FrameClock
{
    const double Fps = 30;
    static readonly List<SaverSurface> surfaces = [];
    static readonly Stopwatch clock = new();
    static System.Windows.Forms.Timer? timer;
    static long framesDone;

    public static void Register(SaverSurface s)
    {
        if (surfaces.Contains(s)) return;
        surfaces.Add(s);
        if (timer is null)
        {
            Native.timeBeginPeriod(1);
            timer = new System.Windows.Forms.Timer { Interval = 4 };
            timer.Tick += (_, _) => Tick();
            clock.Restart();
            framesDone = 0;
            timer.Start();
        }
    }

    public static void Unregister(SaverSurface s)
    {
        surfaces.Remove(s);
        if (surfaces.Count == 0 && timer is not null)
        {
            timer.Stop();
            timer.Dispose();
            timer = null;
            Native.timeEndPeriod(1);
        }
    }

    static void Tick()
    {
        long target = (long)(clock.Elapsed.TotalSeconds * Fps);
        long due = target - framesDone;
        if (due <= 0) return;
        // After a stall (debugger, sleep) don't fast-forward; just resume.
        if (due > 3) { framesDone = target - 1; due = 1; }
        framesDone += due;
        foreach (var s in surfaces.ToArray())
            if (!s.IsDisposed) s.Tick((int)due);
    }
}
