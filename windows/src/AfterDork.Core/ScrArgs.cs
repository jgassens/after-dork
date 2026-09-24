namespace AfterDork;

public enum ScrMode
{
    /// <summary>The After Dork control panel (no arguments to AfterDork.exe).</summary>
    ControlPanel,
    /// <summary>/s — run full screen.</summary>
    Saver,
    /// <summary>/p &lt;hwnd&gt; — draw inside the Screen Saver Settings preview.</summary>
    Preview,
    /// <summary>/c[:hwnd] — the Settings button; opens the control panel on this module.</summary>
    Configure,
    /// <summary>/a — legacy Windows 9x password change; nothing to do.</summary>
    Password,
    /// <summary>--render … — headless PNG frames.</summary>
    Render,
    /// <summary>--snapshot-panel &lt;png&gt; — render the control panel to a file.</summary>
    SnapshotPanel,
}

/// <summary>
/// Parses the command line Windows hands a screen saver. Windows is loose
/// about the format: "/p 1234", "/p:1234", "-P 1234", upper or lower case.
/// A .scr started with no arguments (Explorer's "Configure") means /c.
/// </summary>
public sealed record ScrArgs(ScrMode Mode, string? ModuleId, long Hwnd, string[] Rest)
{
    public static ScrArgs Parse(string[] args, string processPath)
    {
        var list = args.ToList();
        string? module = null;
        int mi = list.FindIndex(a => a.Equals("--module", StringComparison.OrdinalIgnoreCase));
        if (mi >= 0 && mi + 1 < list.Count)
        {
            module = list[mi + 1];
            list.RemoveRange(mi, 2);
        }
        module ??= ModuleFromPath(processPath);
        bool isScr = Path.GetExtension(processPath).Equals(".scr", StringComparison.OrdinalIgnoreCase);

        if (list.Count == 0)
            return new ScrArgs(isScr ? ScrMode.Configure : ScrMode.ControlPanel, module, 0, []);

        string first = list[0];
        if (first.Equals("--render", StringComparison.OrdinalIgnoreCase))
            return new ScrArgs(ScrMode.Render, module, 0, list.Skip(1).ToArray());
        if (first.Equals("--snapshot-panel", StringComparison.OrdinalIgnoreCase))
            return new ScrArgs(ScrMode.SnapshotPanel, module, 0, list.Skip(1).ToArray());

        if (first.Length >= 2 && (first[0] == '/' || first[0] == '-'))
        {
            char c = char.ToLowerInvariant(first[1]);
            string tail = first.Length > 2 ? first[2..].TrimStart(':', ' ') : "";
            if (tail.Length == 0 && list.Count > 1) tail = list[1];
            long.TryParse(tail, out long hwnd);
            switch (c)
            {
                case 's': return new ScrArgs(ScrMode.Saver, module, 0, []);
                case 'p' when hwnd != 0: return new ScrArgs(ScrMode.Preview, module, hwnd, []);
                case 'p': return new ScrArgs(ScrMode.Password, module, 0, []);  // malformed: do nothing
                case 'c': return new ScrArgs(ScrMode.Configure, module, hwnd, []);
                case 'a': return new ScrArgs(ScrMode.Password, module, hwnd, []);
            }
        }
        return new ScrArgs(isScr ? ScrMode.Configure : ScrMode.ControlPanel, module, 0, list.ToArray());
    }

    /// <summary>"…\OrbitalBox.scr" → "OrbitalBox"; AfterDork.exe → null.</summary>
    public static string? ModuleFromPath(string processPath) =>
        ModuleCatalog.Find(Path.GetFileNameWithoutExtension(processPath))?.Id;
}
