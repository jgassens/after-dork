using System.Diagnostics;
using System.Media;
using AfterDork.Graphics;

namespace AfterDork.App;

using Graphics = System.Drawing.Graphics;

/// <summary>
/// The After Dork control panel — a port of ControlPanel/main.swift. The
/// window is 720×540 logical points; every control sits at the Mac frame
/// coordinates (y down) and is scaled for the monitor's DPI.
/// </summary>
internal sealed class ControlPanelForm : Form
{
    const float WinW = 720, WinH = 540;
    static readonly CGSize PreviewSize = new(478, 298);

    readonly Dictionary<string, Dictionary<string, object>> settings = Settings.AllSettings();
    readonly List<(Control control, RectangleF frame)> layout = [];
    readonly List<Control> optionViews = [];
    readonly RootPanel root = new();
    readonly RetroList moduleList;
    readonly SaverSurface preview = new();
    readonly RetroLabel statusLabel = new("Welcome to After Dork.", 11);
    readonly RetroButton setButton = new("Set Screen Saver");
    readonly RetroButton demoButton = new("Demo");
    readonly RetroButton quitButton = new("Quit");
    readonly RetroButton updatesButton = new("Updates…");
    int currentIdx;
    SaverSession? demo;

    // Monitor group
    int displaySleepNow;
    RetroSlider? saverSlider, sleepSlider, brightSlider;
    RetroLabel? saverValue, sleepValue, brightValue, brightTitle;
    SystemSettings.IBrightness? brightness;

    static IReadOnlyList<Module> Catalog => ModuleCatalog.All;

    public ControlPanelForm(string? initialModule = null)
    {
        Text = $"After Dork {Updater.CurrentVersion.ToString(3)}";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Retro.Face;
        Icon = AppIcon.Load();
        KeyPreview = true;

        root.Dock = DockStyle.Fill;
        Controls.Add(root);

        moduleList = new RetroList(Catalog.Select(m => m.Display).ToList());
        Place(moduleList, 14, 36, 194, Catalog.Count * 20 + 4);
        Place(preview, 226, 18, 478, 298);
        Place(updatesButton, WinW - 448, 508, 110, 26);
        Place(demoButton, WinW - 330, 508, 68, 26);
        Place(quitButton, WinW - 254, 508, 68, 26);
        Place(setButton, WinW - 178, 508, 164, 26);
        Place(statusLabel, 14, 512, 252, 18);

        moduleList.SelectedChanged += i =>
        {
            currentIdx = i;
            RebuildOptions();
            RecreatePreview();
            Status($"{Catalog[i].Display} selected.");
        };
        setButton.Clicked += SetScreenSaver;
        demoButton.Clicked += StartDemo;
        quitButton.Clicked += Close;
        updatesButton.Clicked += () => _ = CheckForUpdates(manual: true);

        BuildMonitorGroup();

        int start = initialModule is null ? 0 : Math.Max(0, Catalog.ToList().FindIndex(m => m.Id == initialModule));
        currentIdx = start;
        if (start != 0) moduleList.Select(start);
        RebuildOptions();
        RelayoutAll();
    }

    // MARK: - Layout

    float S => DeviceDpi / 96f;

    Rectangle Px(RectangleF f) =>
        Rectangle.FromLTRB((int)Math.Round(f.Left * S), (int)Math.Round(f.Top * S),
                           (int)Math.Round(f.Right * S), (int)Math.Round(f.Bottom * S));

    void Place(Control c, float x, float y, float w, float h)
    {
        var f = new RectangleF(x, y, w, h);
        layout.Add((c, f));
        c.Bounds = Px(f);
        root.Controls.Add(c);
    }

    void Unplace(Control c)
    {
        layout.RemoveAll(e => e.control == c);
        root.Controls.Remove(c);
        c.Dispose();
    }

    void RelayoutAll()
    {
        ClientSize = new Size((int)Math.Round(WinW * S), (int)Math.Round(WinH * S));
        foreach (var (c, f) in layout) c.Bounds = Px(f);
        root.Invalidate();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        RelayoutAll();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RecreatePreview();
        if (AppState.LastUpdateCheck is not DateTime last || DateTime.UtcNow - last > TimeSpan.FromDays(1))
            _ = CheckForUpdates(manual: false);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        demo?.End();
        preview.View = null;
        base.OnFormClosed(e);
    }

    void Status(string s) => statusLabel.Value = s;

    // MARK: - Options

    Module Current => Catalog[currentIdx];

    double OptionValue(Module module, OptionSpec spec)
    {
        if (settings.TryGetValue(module.Id, out var d) && d.TryGetValue(spec.Key, out var v))
        {
            switch (v)
            {
                case double x: return x;
                case long l: return l;
                case int i: return i;
                case bool b: return b ? 1 : 0;
            }
        }
        return spec.Def;
    }

    void StoreOption(Module module, OptionSpec spec, double value)
    {
        if (!settings.TryGetValue(module.Id, out var m)) settings[module.Id] = m = [];
        m[spec.Key] = spec.Kind == OptionKind.Check ? value > 0.5
                    : spec.IsInt ? (object)(int)value
                    : value;
        Settings.Write(settings);
    }

    void RecreatePreview()
    {
        preview.View = null;  // stops the old simulation before building the new one
        if (!Current.IsAvailable) return;
        preview.Show(Current, preview: true, PreviewSize);
    }

    void RebuildOptions()
    {
        foreach (var v in optionViews) Unplace(v);
        optionViews.Clear();
        var module = Current;
        float y = 352;
        foreach (var spec in module.Options)
        {
            var label = new RetroLabel(spec.Label + ":", 12);
            Place(label, 238, y, 130, 18);
            optionViews.Add(label);
            if (spec.Kind == OptionKind.Slider)
            {
                var valueLabel = new RetroLabel("", 11);
                var slider = new RetroSlider(spec.Min, spec.Max, OptionValue(module, spec), spec.IsInt)
                {
                    Name = $"opt:{spec.Key}",
                };
                valueLabel.Value = spec.Format(slider.Value);
                slider.LiveChanged += v => valueLabel.Value = spec.Format(v);
                slider.Committed += v =>
                {
                    StoreOption(module, spec, v);
                    RecreatePreview();
                };
                Place(slider, 380, y - 2, 226, 22);
                Place(valueLabel, 620, y, 70, 18);
                optionViews.Add(slider);
                optionViews.Add(valueLabel);
            }
            else
            {
                var check = new RetroCheckbox("On", OptionValue(module, spec) > 0.5) { Name = $"opt:{spec.Key}" };
                check.Changed += v =>
                {
                    StoreOption(module, spec, v ? 1 : 0);
                    RecreatePreview();
                };
                Place(check, 380, y - 2, 226, 22);
                optionViews.Add(check);
            }
            y += 34;
        }
    }

    // MARK: - Buttons

    void SetScreenSaver()
    {
        var module = Current;
        try
        {
            Settings.Write(settings);
            var r = Installer.SetScreenSaver(module);
            SystemSounds.Beep.Play();
            Status(r.Warning is null ? $"✓ {module.Display} is now your screen saver." : $"✓ {module.Display} set. {r.Warning}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Status($"Couldn't set screen saver: {e.Message}");
        }
    }

    void StartDemo()
    {
        if (demo is { IsRunning: true } || !Current.IsAvailable) return;
        Status($"Demonstrating {Current.Display}…");
        Settings.Write(settings);
        demo = SaverSession.Start(Current, graceSeconds: 0.7, onEnded: () =>
        {
            Status("Demo ended.");
            Activate();
        });
    }

    async Task CheckForUpdates(bool manual)
    {
        if (manual) Status("Checking for updates…");
        var r = await Updater.CheckAsync();
        if (IsDisposed) return;
        if (r.Outcome != Updater.Outcome.Failed) AppState.LastUpdateCheck = DateTime.UtcNow;
        switch (r.Outcome)
        {
            case Updater.Outcome.UpdateAvailable:
                Status($"After Dork {r.Latest?.ToString(3)} is available.");
                var answer = MessageBox.Show(this,
                    $"After Dork {r.Latest?.ToString(3)} is available — you have {r.Current.ToString(3)}.\n\nOpen the download page?",
                    "Software Update", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer == DialogResult.Yes && r.PageUrl is { Length: > 0 } url)
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                break;
            case Updater.Outcome.NoWindowsBuild when manual:
                Status("No Windows release published yet.");
                break;
            case Updater.Outcome.UpToDate when manual:
                Status($"You're up to date ({r.Current.ToString(3)}).");
                break;
            case Updater.Outcome.Failed when manual:
                Status("Couldn't reach the update server.");
                break;
        }
    }

    // MARK: - Monitor group

    void BuildMonitorGroup()
    {
        RetroLabel Label(string text, float x, float y, float w, bool right = false)
        {
            var l = new RetroLabel(text, 11, right: right);
            Place(l, x, y, w, 16);
            return l;
        }

        displaySleepNow = SystemSettings.ReadDisplaySleepMinutes() ?? 0;
        int idleMinutes = SystemSettings.ReadIdleMinutes();

        Label("Start saver after:", 22, 220, 110);
        saverValue = Label(SystemSettings.FormatMinutes(idleMinutes), 132, 220, 70, right: true);
        saverSlider = new RetroSlider(0, Math.Max(60, idleMinutes), idleMinutes, true) { Name = "mon:saver" };
        saverSlider.LiveChanged += v => saverValue.Value = SystemSettings.FormatMinutes((int)v);
        saverSlider.Committed += v =>
        {
            int m = (int)v;
            if (!SystemSettings.WriteIdleMinutes(m))
            {
                Status("Couldn't change the screen saver delay.");
                return;
            }
            if (m != 0 && displaySleepNow != 0 && displaySleepNow <= m)
                Status($"Careful: display sleeps first ({SystemSettings.FormatMinutes(displaySleepNow)}).");
            else
                Status(m == 0 ? "Screen saver will never start." : $"Screen saver starts after {SystemSettings.FormatMinutes(m)}.");
        };
        Place(saverSlider, 22, 238, 178, 20);

        Label("Display sleep after:", 22, 258, 110);
        sleepValue = Label(SystemSettings.FormatMinutes(displaySleepNow), 132, 258, 70, right: true);
        sleepSlider = new RetroSlider(0, Math.Max(60, displaySleepNow), displaySleepNow, true) { Name = "mon:sleep" };
        sleepSlider.LiveChanged += v => sleepValue.Value = SystemSettings.FormatMinutes((int)v);
        sleepSlider.Committed += v =>
        {
            int m = (int)v;
            if (m == displaySleepNow) return;
            if (SystemSettings.WriteDisplaySleepMinutes(m))
            {
                displaySleepNow = m;
                Status(m == 0 ? "Display will never sleep." : $"Display sleeps after {SystemSettings.FormatMinutes(m)} (all power sources).");
            }
            else
            {
                sleepSlider.Value = displaySleepNow;
                sleepSlider.Invalidate();
                sleepValue.Value = SystemSettings.FormatMinutes(displaySleepNow);
                Status("Display sleep unchanged.");
            }
        };
        Place(sleepSlider, 22, 276, 178, 20);

        brightTitle = Label("Brightness:", 22, 296, 110);
        brightValue = Label("n/a", 132, 296, 70, right: true);
        brightness = SystemSettings.DetectBrightness();
        if (brightness?.Read() is int b0)
        {
            if (brightness.IsBuiltIn && Screen.AllScreens.Length > 1) brightTitle.Value = "Laptop brightness:";
            static string Fmt(double v) => (int)v == 0 ? "Min" : $"{(int)v}%";
            brightValue.Value = Fmt(b0);
            brightSlider = new RetroSlider(0, 100, b0, true) { Name = "mon:bright" };
            int lastSent = b0;
            // Live drag tracks like the brightness keys, floored at 2% so a
            // stray click can't plunge the panel to its minimum.
            brightSlider.LiveChanged += v =>
            {
                int p = Math.Max((int)v, 2);
                if (p != lastSent) { brightness.Set(p); lastSent = p; }
                brightValue.Value = Fmt(v);
            };
            brightSlider.Committed += v =>
            {
                if ((int)v == 0 && !brightSlider.LastEventWasDrag)
                {
                    brightSlider.Value = 2;
                    brightSlider.Invalidate();
                    brightness.Set(2);
                    lastSent = 2;
                    brightValue.Value = Fmt(2);
                    Status("Drag to 0 for minimum brightness.");
                    return;
                }
                brightness.Set((int)v);
                lastSent = (int)v;
                Status((int)v == 0 ? "Brightness at minimum." : $"Brightness {(int)v}%.");
            };
            Place(brightSlider, 22, 314, 178, 20);
        }
    }

    // MARK: - Test / automation hooks (used by --snapshot-panel and the UI self-test)

    internal RetroList ModuleList => moduleList;
    internal SaverSurface Preview => preview;
    internal RetroButton SetButton => setButton;
    internal RetroButton DemoButton => demoButton;
    internal RetroButton QuitButton => quitButton;
    internal RetroButton UpdatesButton => updatesButton;
    internal string StatusText => statusLabel.Value;
    internal SaverSession? Demo => demo;
    internal RetroSlider? SaverSlider => saverSlider;
    internal RetroSlider? SleepSlider => sleepSlider;
    internal RetroSlider? BrightSlider => brightSlider;
    internal string? SaverValueText => saverValue?.Value;
    internal string? SleepValueText => sleepValue?.Value;
    internal string? BrightValueText => brightValue?.Value;
    internal IEnumerable<Control> OptionControls => optionViews;

    /// <summary>The static chrome: labels, bezel, etched groups, logo (main.swift:205-258).</summary>
    sealed class RootPanel : Panel
    {
        public RootPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Retro.Face;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Retro.Prepare(g, DeviceDpi / 96f);
            using var face = new SolidBrush(Retro.Face);
            g.FillRectangle(face, 0, 0, WinW, WinH);

            Retro.Text(g, "Module:", 14, 16);
            Retro.Bevel(g, new RectangleF(222, 14, 486, 306), sunken: true);

            Etched(g, new RectangleF(222, 334, 486, 152), new RectangleF(232, 326, 90, 16), "Settings", 236, 325);
            Etched(g, new RectangleF(14, 214, 194, 146), new RectangleF(24, 206, 66, 16), "Monitor", 28, 205);
            Retro.Text(g, "Sleep applies on AC and battery.", 22, 338, 10, bold: false, Retro.FaceShadow);

            Retro.Text(g, "AFTER", 16, 370, 30, bold: true, Retro.LogoPurple);
            Retro.Text(g, "DORK", 16, 402, 30, bold: true);
            Retro.Text(g, "Chemistry screen savers", 16, 442, 11, bold: false);
            Retro.Text(g, $"v{Updater.CurrentVersion.ToString(3)} © 1996 Gassensmith Labs", 16, 458, 11, bold: false);
            Retro.Text(g, "All molecules biblically accurate.", 16, 474, 11, bold: false);

            using var shadow = new SolidBrush(Retro.FaceShadow);
            g.FillRectangle(shadow, 8, 498, 704, 1);
            g.FillRectangle(Brushes.White, 8, 499, 704, 1);
        }

        static void Etched(Graphics g, RectangleF r, RectangleF gap, string caption, float tx, float ty)
        {
            using var shadow = new Pen(Retro.FaceShadow, 1);
            using var light = new Pen(Color.White, 1);
            g.DrawRectangle(shadow, r.X, r.Y, r.Width - 1, r.Height - 1);
            g.DrawRectangle(light, r.X + 1, r.Y + 1, r.Width - 1, r.Height - 1);
            using var face = new SolidBrush(Retro.Face);
            g.FillRectangle(face, gap);
            Retro.Text(g, caption, tx, ty, 12, bold: false);
        }
    }
}

internal static class AppIcon
{
    public static Icon? Load()
    {
        using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("AfterDork.ico");
        return s is null ? null : new Icon(s);
    }
}
