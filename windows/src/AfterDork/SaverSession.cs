using System.Diagnostics;

namespace AfterDork.App;

/// <summary>
/// A full-screen run of one module on every monitor: /s, and the control
/// panel's Demo button. Ends on a key, click, wheel, a mouse move beyond a
/// few pixels, or the app losing activation — but ignores input for a short
/// grace period after starting (the Mac demo used 0.7 s).
/// </summary>
internal sealed class SaverSession
{
    const int MoveThreshold = 8;

    readonly List<SaverForm> forms = [];
    readonly Stopwatch since = Stopwatch.StartNew();
    readonly double graceSeconds;
    readonly Action onEnded;
    Point? mouseStart;
    bool ended;

    public string Module { get; }
    public bool IsRunning => !ended;

    SaverSession(Module module, double graceSeconds, Action onEnded)
    {
        Module = module.Id;
        this.graceSeconds = graceSeconds;
        this.onEnded = onEnded;
    }

    public static SaverSession Start(Module module, double graceSeconds, Action onEnded)
    {
        var s = new SaverSession(module, graceSeconds, onEnded);
        foreach (var screen in Screen.AllScreens)
        {
            var f = new SaverForm(s, screen, module);
            s.forms.Add(f);
            f.Show();
        }
        s.forms.FirstOrDefault(f => f.IsPrimary)?.Activate();
        s.mouseStart = Cursor.Position;
        Cursor.Hide();
        return s;
    }

    internal void OnInput(string why)
    {
        if (ended || since.Elapsed.TotalSeconds < graceSeconds) return;
        Debug.WriteLine($"saver ending: {why}");
        End();
    }

    internal void OnMouseMove()
    {
        var p = Cursor.Position;
        if (mouseStart is not Point s) { mouseStart = p; return; }
        if (Math.Abs(p.X - s.X) > MoveThreshold || Math.Abs(p.Y - s.Y) > MoveThreshold)
            OnInput("mouse move");
    }

    public void End()
    {
        if (ended) return;
        ended = true;
        foreach (var f in forms) f.Close();
        forms.Clear();
        Cursor.Show();
        onEnded();
    }
}

internal sealed class SaverForm : Form
{
    const int WM_ACTIVATEAPP = 0x001C;
    readonly SaverSession session;
    readonly Module module;
    readonly SaverSurface surface = new() { Dock = DockStyle.Fill };

    public bool IsPrimary { get; }

    public SaverForm(SaverSession session, Screen screen, Module module)
    {
        this.session = session;
        this.module = module;
        IsPrimary = screen.Primary;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        BackColor = Color.Black;
        Text = "After Dork";
        Controls.Add(surface);

        KeyDown += (_, _) => session.OnInput("key");
        foreach (Control c in new Control[] { this, surface })
        {
            c.MouseDown += (_, _) => session.OnInput("click");
            c.MouseWheel += (_, _) => session.OnInput("wheel");
            c.MouseMove += (_, _) => session.OnMouseMove();
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        surface.Show(module, preview: false);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_ACTIVATEAPP && m.WParam == IntPtr.Zero)
            session.OnInput("deactivated");
        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        surface.View = null;
        base.OnFormClosed(e);
    }
}
