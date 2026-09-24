using System.Globalization;
using AfterDork.Graphics;

namespace AfterDork.App;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var a = ScrArgs.Parse(args, Environment.ProcessPath ?? "AfterDork.exe");
        switch (a.Mode)
        {
            case ScrMode.Render:
                return RenderCommand(a);
            case ScrMode.Password:
                return 0;
        }

        ApplicationConfiguration.Initialize();
        var module = ResolveModule(a.ModuleId);

        switch (a.Mode)
        {
            case ScrMode.Saver:
            {
                // Windows occasionally launches the saver twice; one is enough.
                using var mutex = new Mutex(true, @"Local\AfterDork.Saver", out bool first);
                if (!first) return 0;
                SaverSession.Start(module, graceSeconds: 0.5, onEnded: Application.ExitThread);
                Application.Run();
                return 0;
            }
            case ScrMode.Preview:
                using (var preview = new PreviewWindow(new IntPtr(a.Hwnd), module)) preview.Run();
                return 0;
            case ScrMode.SnapshotPanel:
                return SnapshotPanel(a, module);
            default:  // ControlPanel, Configure
            {
                using var mutex = new Mutex(true, @"Local\AfterDork.ControlPanel", out bool first);
                if (!first) return 0;
                Application.Run(new ControlPanelForm(a.Mode == ScrMode.Configure || a.ModuleId is not null ? module.Id : null));
                return 0;
            }
        }
    }

    /// <summary>--module / .scr name, else the module last set as screen saver, else the first.</summary>
    static Module ResolveModule(string? id)
    {
        foreach (var candidate in new[] { id, AppState.CurrentModule })
            if (candidate is not null && ModuleCatalog.Find(candidate) is { IsAvailable: true } m) return m;
        return ModuleCatalog.All.First(m => m.IsAvailable);
    }

    /// <summary>AfterDork.exe --render &lt;Module&gt; w h frames outPrefix [--preview] — like `make previews`.</summary>
    static int RenderCommand(ScrArgs a)
    {
        Native.AttachConsole(-1);
        var r = a.Rest;
        if (r.Length < 5 || ModuleCatalog.Find(r[0]) is not { IsAvailable: true } m)
        {
            Console.Error.WriteLine("usage: AfterDork.exe --render <Module> <w> <h> <frames> <outPrefix> [--preview]");
            return 2;
        }
        var res = Harness.Render(m, int.Parse(r[1], CultureInfo.InvariantCulture), int.Parse(r[2], CultureInfo.InvariantCulture),
                                 int.Parse(r[3], CultureInfo.InvariantCulture), r[4], preview: r.Contains("--preview"), drawEvery: 0);
        Console.WriteLine($"previewed {res.Frames} frames -> {string.Join(", ", res.Files)}");
        return 0;
    }

    /// <summary>--snapshot-panel &lt;png&gt;: the control panel after 45 preview frames (Mac --snapshot).</summary>
    static int SnapshotPanel(ScrArgs a, Module module)
    {
        if (a.Rest.Length < 1) return 2;
        using var form = new ControlPanelForm(module.Id);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        form.ShowInTaskbar = false;
        form.Show();
        Application.DoEvents();
        var view = form.Preview.View;
        if (view is not null) for (int i = 0; i < 45; i++) view.AnimateOneFrame();
        form.Preview.Invalidate();
        Application.DoEvents();
        using var bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        var root = form.Controls[0];
        root.DrawToBitmap(bmp, new Rectangle(Point.Empty, root.Size));
        bmp.Save(a.Rest[0], System.Drawing.Imaging.ImageFormat.Png);
        form.Close();
        return 0;
    }
}
