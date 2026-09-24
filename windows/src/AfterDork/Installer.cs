using Microsoft.Win32;

namespace AfterDork.App;

/// <summary>
/// "Set Screen Saver": the Windows twin of installAndSelectLegacy. Copies the
/// app into %LOCALAPPDATA%\Programs\AfterDork, drops a &lt;Module&gt;.scr there
/// (a copy of AfterDork.exe — the launcher inside finds AfterDork.dll next
/// to it and the file name picks the module), and points Windows at it.
/// </summary>
internal static class Installer
{
    const string DesktopKey = @"Control Panel\Desktop";
    const string PolicyKey = @"Software\Policies\Microsoft\Windows\Control Panel\Desktop";

    public static string InstallDir =>
        Environment.GetEnvironmentVariable("AFTERDORK_INSTALL_DIR") is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AfterDork");

    public static string ScrPath(string moduleId) => Path.Combine(InstallDir, moduleId + ".scr");

    public sealed record Result(bool Ok, string? Warning, string ScrPath);

    public static Result SetScreenSaver(Module module)
    {
        EnsureInstalled();
        var scr = ScrPath(module.Id);
        CopyIfChanged(Path.Combine(InstallDir, "AfterDork.exe"), scr);

        using (var key = Registry.CurrentUser.CreateSubKey(DesktopKey))
        {
            key.SetValue("SCRNSAVE.EXE", scr, RegistryValueKind.String);
            key.SetValue("ScreenSaveActive", "1", RegistryValueKind.String);
        }
        Native.SystemParametersInfo(Native.SPI_SETSCREENSAVEACTIVE, 1, IntPtr.Zero,
                                    Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
        AppState.CurrentModule = module.Id;
        return new Result(true, PolicyWarning(), scr);
    }

    /// <summary>The currently configured screen saver path, if any.</summary>
    public static string? CurrentScreenSaver()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DesktopKey);
        return key?.GetValue("SCRNSAVE.EXE") as string;
    }

    /// <summary>Group Policy can pin or disable the screen saver; say so rather than fail silently.</summary>
    static string? PolicyWarning()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PolicyKey)
                        ?? Registry.LocalMachine.OpenSubKey(PolicyKey);
        if (key is null) return null;
        if (key.GetValue("SCRNSAVE.EXE") is string forced && forced.Length > 0)
            return "Group Policy picks the screen saver on this PC.";
        if (key.GetValue("ScreenSaveActive") is string active && active == "0")
            return "Group Policy has disabled screen savers.";
        return null;
    }

    /// <summary>Copies the running app's files into InstallDir (no-op when already running from there).</summary>
    public static void EnsureInstalled()
    {
        var src = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
        var dst = Path.GetFullPath(InstallDir).TrimEnd('\\');
        Directory.CreateDirectory(dst);
        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) return;
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            if (rel.EndsWith(".scr", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            CopyIfChanged(file, target);
        }
    }

    static void CopyIfChanged(string from, string to)
    {
        var a = new FileInfo(from);
        var b = new FileInfo(to);
        if (b.Exists && b.Length == a.Length && b.LastWriteTimeUtc == a.LastWriteTimeUtc) return;
        try
        {
            File.Copy(from, to, overwrite: true);
        }
        catch (IOException) when (b.Exists)
        {
            // In use (e.g. that saver is running right now); keep the old copy.
        }
    }
}

/// <summary>Small bits of app state kept next to the settings file.</summary>
internal static class AppState
{
    static string Dir => Path.GetDirectoryName(Settings.FilePath)!;
    static string ModuleFile => Path.Combine(Dir, "current-module.txt");
    static string UpdateFile => Path.Combine(Dir, "last-update-check.txt");

    /// <summary>The module chosen with Set Screen Saver (used by a bare AfterDork.exe /s).</summary>
    public static string? CurrentModule
    {
        get
        {
            try { return ModuleCatalog.Find(File.ReadAllText(ModuleFile).Trim())?.Id; }
            catch (IOException) { return null; }
        }
        set
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(ModuleFile, value ?? "");
        }
    }

    public static DateTime? LastUpdateCheck
    {
        get
        {
            try { return DateTime.Parse(File.ReadAllText(UpdateFile).Trim(), null, System.Globalization.DateTimeStyles.RoundtripKind); }
            catch (Exception e) when (e is IOException or FormatException) { return null; }
        }
        set
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(UpdateFile, value?.ToString("o") ?? "");
        }
    }
}
