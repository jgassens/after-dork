using AfterDork.Graphics;
using SkiaSharp;

namespace AfterDork;

/// <summary>
/// The ScreenSaverView contract the ported savers subclass. Bounds are in
/// logical points (y-up, origin bottom-left); hosts scale for DPI. Hosts call
/// AnimateOneFrame at 1/AnimationTimeInterval Hz and Draw whenever they paint.
/// </summary>
public abstract class SaverView
{
    protected SaverView(CGRect frame, bool isPreview)
    {
        Bounds = new CGRect(0, 0, frame.Width, frame.Height);
        IsPreview = isPreview;
    }

    public CGRect Bounds { get; }
    public bool IsPreview { get; }
    public double AnimationTimeInterval { get; protected set; } = 1.0 / 30.0;
    public bool NeedsDisplay { get; set; } = true;

    public abstract void AnimateOneFrame();
    public abstract void Draw(CGContext ctx);

    public virtual void StartAnimation() { }
    public virtual void StopAnimation() { }

    public virtual bool HasConfigureSheet => false;
}

public static class SaverRenderer
{
    /// <summary>
    /// Draws one frame of a saver onto a canvas whose pixel size is
    /// bounds × scale. Clears to black first, as ScreenSaverView does.
    /// </summary>
    public static void Render(SKCanvas canvas, SaverView view, float scale)
    {
        canvas.Save();
        canvas.Clear(SKColors.Black);
        canvas.Scale(scale);
        using (var ctx = new CGContext(canvas, view.Bounds.Height))
        {
            ctx.Clip(view.Bounds);
            view.Draw(ctx);
        }
        canvas.Restore();
    }
}
