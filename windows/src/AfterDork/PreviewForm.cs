namespace AfterDork.App;

/// <summary>
/// /p &lt;hwnd&gt;: runs the module in preview mode as a child of the little
/// monitor in Windows' Screen Saver Settings dialog, and quits when that
/// window goes away.
/// </summary>
internal sealed class PreviewForm : Form
{
    readonly IntPtr parent;
    readonly Module module;
    readonly SaverSurface surface = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer watchdog = new() { Interval = 250 };

    public PreviewForm(IntPtr parent, Module module)
    {
        this.parent = parent;
        this.module = module;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Native.GetClientRect(parent, out var r);
        Bounds = new Rectangle(0, 0, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top));
        Controls.Add(surface);
        watchdog.Tick += (_, _) =>
        {
            if (!Native.IsWindow(parent)) Close();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style = Native.WS_CHILD | Native.WS_VISIBLE;
            cp.ExStyle = 0;
            cp.Parent = parent;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        surface.Show(module, preview: true);
        watchdog.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        watchdog.Stop();
        surface.View = null;
        base.OnFormClosed(e);
    }
}
