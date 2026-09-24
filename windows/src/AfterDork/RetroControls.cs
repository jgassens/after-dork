using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AfterDork.App;

using Graphics = System.Drawing.Graphics;

// The 1996 look, drawn by hand exactly as ControlPanel/main.swift draws it:
// two-pixel Win95 bevels, face-grey chrome, navy selection. Every control
// paints in logical points (the Mac's coordinate space, y down) and scales
// by the monitor DPI.

internal static class Retro
{
    public static readonly Color Face = Color.FromArgb(199, 199, 199);
    public static readonly Color FaceLight = Color.White;
    public static readonly Color FaceShadow = Color.FromArgb(128, 128, 128);
    public static readonly Color FaceDark = Color.FromArgb(51, 51, 51);
    public static readonly Color SelectNavy = Color.FromArgb(0, 0, 128);
    public static readonly Color LogoPurple = Color.FromArgb(140, 26, 140);

    public const string FontFamily = "Segoe UI";

    static readonly Dictionary<(float, bool), Font> fonts = [];

    public static Font Font(float size, bool bold = false)
    {
        if (!fonts.TryGetValue((size, bold), out var f))
            fonts[(size, bold)] = f = new Font(FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        return f;
    }

    /// <summary>bevel(ctx, rect, sunken) from main.swift:18-35.</summary>
    public static void Bevel(Graphics g, RectangleF r, bool sunken)
    {
        Color tl = sunken ? FaceShadow : FaceLight;
        Color tl2 = sunken ? FaceDark : Face;
        Color br = sunken ? FaceLight : FaceDark;
        Color br2 = sunken ? Face : FaceShadow;
        using var b1 = new SolidBrush(tl);
        using var b2 = new SolidBrush(br);
        using var b3 = new SolidBrush(tl2);
        using var b4 = new SolidBrush(br2);
        g.FillRectangle(b1, r.X, r.Y, r.Width, 1);
        g.FillRectangle(b1, r.X, r.Y, 1, r.Height);
        g.FillRectangle(b2, r.X, r.Bottom - 1, r.Width, 1);
        g.FillRectangle(b2, r.Right - 1, r.Y, 1, r.Height);
        g.FillRectangle(b3, r.X + 1, r.Y + 1, r.Width - 2, 1);
        g.FillRectangle(b3, r.X + 1, r.Y + 1, 1, r.Height - 2);
        g.FillRectangle(b4, r.X + 1, r.Bottom - 2, r.Width - 2, 1);
        g.FillRectangle(b4, r.Right - 2, r.Y + 1, 1, r.Height - 2);
    }

    static readonly StringFormat typographic = MakeFormat();
    static StringFormat MakeFormat()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        return f;
    }

    /// <summary>retroText: draws with the top-left of the line box at (x, y).</summary>
    public static void Text(Graphics g, string s, float x, float y, float size = 12, bool bold = true, Color? color = null)
    {
        using var b = new SolidBrush(color ?? Color.Black);
        g.DrawString(s, Font(size, bold), b, x, y + 1, typographic);
    }

    public static SizeF Measure(Graphics g, string s, float size, bool bold) =>
        g.MeasureString(s, Font(size, bold), PointF.Empty, typographic);

    public static void Prepare(Graphics g, float scale)
    {
        g.ScaleTransform(scale, scale);
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }
}

/// <summary>A control that paints itself in logical points.</summary>
internal abstract class RetroControl : Control
{
    protected RetroControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Retro.Face;
    }

    protected float S => DeviceDpi / 96f;
    protected float W => Width / S;
    protected float H => Height / S;
    protected PointF Logical(Point p) => new(p.X / S, p.Y / S);

    protected override void OnPaint(PaintEventArgs e)
    {
        Retro.Prepare(e.Graphics, S);
        PaintRetro(e.Graphics);
    }

    protected abstract void PaintRetro(Graphics g);
}

internal sealed class RetroButton : RetroControl
{
    bool pressed;
    public string Title { get; }
    public event Action? Clicked;

    public RetroButton(string title) => Title = title;

    protected override void PaintRetro(Graphics g)
    {
        using (var face = new SolidBrush(Retro.Face)) g.FillRectangle(face, 0, 0, W, H);
        Retro.Bevel(g, new RectangleF(0, 0, W, H), pressed);
        var sz = Retro.Measure(g, Title, 12, true);
        float off = pressed ? 1 : 0;
        Retro.Text(g, Title, (W - sz.Width) / 2 + off, (H - sz.Height) / 2 - 1 + off, 12, true);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!pressed) return;
        pressed = false;
        Invalidate();
        if (ClientRectangle.Contains(e.Location)) Clicked?.Invoke();
    }

    /// <summary>Programmatic click (tests, keyboard).</summary>
    public void PerformClick() => Clicked?.Invoke();
}

internal sealed class RetroCheckbox : RetroControl
{
    public string Label { get; }
    public bool Value { get; set; }
    public event Action<bool>? Changed;

    public RetroCheckbox(string label, bool value)
    {
        Label = label;
        Value = value;
    }

    protected override void PaintRetro(Graphics g)
    {
        using (var face = new SolidBrush(Retro.Face)) g.FillRectangle(face, 0, 0, W, H);
        var box = new RectangleF(0, (H - 14) / 2, 14, 14);
        g.FillRectangle(Brushes.White, box);
        Retro.Bevel(g, box, sunken: true);
        if (Value)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            using var pen = new Pen(Color.Black, 2) { LineJoin = LineJoin.Miter };
            g.DrawLines(pen, [
                new PointF(box.Left + 3, box.Top + box.Height / 2),
                new PointF(box.Left + box.Width / 2 - 1, box.Bottom - 4),
                new PointF(box.Right - 3, box.Top + 3),
            ]);
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
        }
        Retro.Text(g, Label, 22, (H - 15) / 2, 12, bold: false);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Toggle();
    }

    public void Toggle()
    {
        Value = !Value;
        Invalidate();
        Changed?.Invoke(Value);
    }
}

internal sealed class RetroSlider : RetroControl
{
    public double Min { get; }
    public double Max { get; }
    public bool IsInt { get; }
    public double Value { get; set; }
    public bool LastEventWasDrag { get; private set; }
    public event Action<double>? LiveChanged;
    public event Action<double>? Committed;
    bool tracking;

    public RetroSlider(double min, double max, double value, bool isInt)
    {
        Min = min;
        Max = max;
        IsInt = isInt;
        Value = value;
    }

    protected override void PaintRetro(Graphics g)
    {
        using (var face = new SolidBrush(Retro.Face)) g.FillRectangle(face, 0, 0, W, H);
        float midY = H / 2;
        var track = new RectangleF(4, midY - 2, W - 8, 4);
        using (var shadow = new SolidBrush(Retro.FaceShadow)) g.FillRectangle(shadow, track);
        var t = track;
        t.Inflate(1, 1);
        Retro.Bevel(g, t, sunken: true);
        double frac = Max > Min ? (Value - Min) / (Max - Min) : 0;
        frac = Math.Clamp(frac, 0, 1);
        var thumb = new RectangleF(4 + (float)((W - 8 - 12) * frac), midY - 9, 12, 18);
        using (var face = new SolidBrush(Retro.Face)) g.FillRectangle(face, thumb);
        Retro.Bevel(g, thumb, sunken: false);
    }

    void Apply(Point p)
    {
        var lp = Logical(p);
        double f = Math.Clamp((lp.X - 4) / (W - 8), 0, 1);
        double v = Min + f * (Max - Min);
        if (IsInt) v = Math.Round(v, MidpointRounding.AwayFromZero);
        Value = v;
        Invalidate();
        LiveChanged?.Invoke(v);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        tracking = true;
        LastEventWasDrag = false;
        Apply(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!tracking) return;
        LastEventWasDrag = true;
        Apply(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!tracking) return;
        tracking = false;
        Committed?.Invoke(Value);
    }

    /// <summary>Programmatic drag-and-release to a value (tests).</summary>
    public void SetAndCommit(double v, bool asDrag = true)
    {
        Value = IsInt ? Math.Round(v, MidpointRounding.AwayFromZero) : v;
        LastEventWasDrag = asDrag;
        Invalidate();
        LiveChanged?.Invoke(Value);
        Committed?.Invoke(Value);
    }
}

internal sealed class RetroList : RetroControl
{
    const float RowH = 20;
    public IReadOnlyList<string> Items { get; }
    public int Selected { get; private set; }
    public event Action<int>? SelectedChanged;

    public RetroList(IReadOnlyList<string> items) => Items = items;

    protected override void PaintRetro(Graphics g)
    {
        g.FillRectangle(Brushes.White, 0, 0, W, H);
        Retro.Bevel(g, new RectangleF(0, 0, W, H), sunken: true);
        for (int i = 0; i < Items.Count; i++)
        {
            var row = new RectangleF(2, 2 + i * RowH, W - 4, RowH);
            bool sel = i == Selected;
            if (sel)
                using (var navy = new SolidBrush(Retro.SelectNavy)) g.FillRectangle(navy, row);
            Retro.Text(g, Items[i], 8, row.Y + 3, 12, bold: false, sel ? Color.White : Color.Black);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int row = (int)Math.Floor((Logical(e.Location).Y - 2) / RowH);
        Select(row);
    }

    public void Select(int row)
    {
        if (row < 0 || row >= Items.Count || row == Selected) return;
        Selected = row;
        Invalidate();
        SelectedChanged?.Invoke(row);
    }
}

/// <summary>An NSTextField(labelWithString:) look-alike on face grey.</summary>
internal sealed class RetroLabel : RetroControl
{
    string text;
    readonly float size;
    readonly bool bold, right;
    Color color;

    public RetroLabel(string text, float size, bool bold = false, bool right = false, Color? color = null)
    {
        this.text = text;
        this.size = size;
        this.bold = bold;
        this.right = right;
        this.color = color ?? Color.Black;
    }

    public string Value
    {
        get => text;
        set { if (text != value) { text = value; Invalidate(); } }
    }

    protected override void PaintRetro(Graphics g)
    {
        using (var face = new SolidBrush(Retro.Face)) g.FillRectangle(face, 0, 0, W, H);
        float x = 0;
        if (right) x = W - Retro.Measure(g, text, size, bold).Width;
        Retro.Text(g, text, x, 0, size, bold, color);
    }
}
