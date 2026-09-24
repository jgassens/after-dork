namespace AfterDork.App;

/// <summary>
/// /p &lt;hwnd&gt;: runs the module in preview mode inside the little monitor in
/// Windows' Screen Saver Settings dialog, and quits when that window goes
/// away. This is a plain control created directly as a WS_CHILD of the
/// dialog's window — a Form can't be used: WinForms rewrites a form's styles
/// and parks it in its own hidden window, so it never shows up there.
/// </summary>
internal sealed class PreviewWindow : SaverSurface
{
    readonly IntPtr parent;
    readonly Module module;
    readonly System.Windows.Forms.Timer watchdog = new() { Interval = 250 };

    public PreviewWindow(IntPtr parent, Module module)
    {
        this.parent = parent;
        this.module = module;
        Native.GetClientRect(parent, out var r);
        Bounds = new Rectangle(0, 0, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top));
        // Quit when the dialog goes away, or when it tears our window down
        // (it does that before starting a fresh /p after "Preview").
        watchdog.Tick += (_, _) =>
        {
            if (!Native.IsWindow(parent) || !IsHandleCreated || !Native.IsWindow(Handle)) Application.ExitThread();
        };
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        base.OnHandleDestroyed(e);
        SaverSession.Trace("preview window destroyed");
        Application.ExitThread();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Parent = parent;
            cp.Style = Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS;
            cp.ExStyle = 0;
            cp.X = 0;
            cp.Y = 0;
            cp.Width = Width;
            cp.Height = Height;
            return cp;
        }
    }

    /// <summary>Creates the child window and pumps messages until the dialog closes.</summary>
    public void Run()
    {
        CreateControl();
        Show(module, preview: true);
        watchdog.Start();
        SaverSession.Trace($"preview {module.Id} in {parent} at {Width}x{Height} (hwnd {Handle})");
        Application.Run();
        watchdog.Stop();
        View = null;
    }
}
