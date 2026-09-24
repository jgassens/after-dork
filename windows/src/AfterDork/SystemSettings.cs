using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace AfterDork.App;

/// <summary>
/// The control panel's Monitor group: when the screen saver starts, when the
/// display sleeps, and backlight brightness — the Windows equivalents of the
/// Mac's idleTime preference, pmset displaysleep and DisplayServices.
/// </summary>
internal static class SystemSettings
{
    const uint Update = Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE;

    // MARK: - Screen saver timeout (0 = never)

    public static int ReadIdleMinutes()
    {
        int active = 0, seconds = 0;
        Native.SystemParametersInfo(Native.SPI_GETSCREENSAVEACTIVE, 0, ref active, 0);
        if (active == 0) return 0;
        Native.SystemParametersInfo(Native.SPI_GETSCREENSAVETIMEOUT, 0, ref seconds, 0);
        return seconds / 60;
    }

    public static bool WriteIdleMinutes(int minutes)
    {
        if (minutes <= 0)
            return Native.SystemParametersInfo(Native.SPI_SETSCREENSAVEACTIVE, 0, IntPtr.Zero, Update);
        return Native.SystemParametersInfo(Native.SPI_SETSCREENSAVETIMEOUT, (uint)(minutes * 60), IntPtr.Zero, Update)
            && Native.SystemParametersInfo(Native.SPI_SETSCREENSAVEACTIVE, 1, IntPtr.Zero, Update);
    }

    // MARK: - Display sleep (0 = never), for the active power source

    public static bool OnBattery =>
        Native.GetSystemPowerStatus(out var s) && s.ACLineStatus == 0;

    public static int? ReadDisplaySleepMinutes(bool? battery = null)
    {
        if (Native.PowerGetActiveScheme(IntPtr.Zero, out var schemePtr) != 0) return null;
        try
        {
            var scheme = Marshal.PtrToStructure<Guid>(schemePtr);
            var sub = Native.GUID_VIDEO_SUBGROUP;
            var setting = Native.GUID_VIDEO_POWERDOWN_TIMEOUT;
            uint seconds;
            uint rc = battery ?? OnBattery
                ? Native.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out seconds)
                : Native.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out seconds);
            return rc == 0 ? (int)(seconds / 60) : null;
        }
        finally
        {
            Native.LocalFree(schemePtr);
        }
    }

    /// <summary>Sets display sleep on both AC and battery (like pmset -a). No admin needed on Windows.</summary>
    public static bool WriteDisplaySleepMinutes(int minutes) =>
        RunPowercfg($"/change monitor-timeout-ac {minutes}") && RunPowercfg($"/change monitor-timeout-dc {minutes}");

    static bool RunPowercfg(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(10000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    public static string FormatMinutes(int m) => m == 0 ? "Never" : $"{m} min";

    // MARK: - Brightness

    /// <summary>A display whose backlight we can drive, 0–100.</summary>
    internal interface IBrightness
    {
        /// <summary>True for a laptop's built-in panel (WMI).</summary>
        bool IsBuiltIn { get; }
        int? Read();
        bool Set(int percent);
    }

    /// <summary>Prefers the built-in panel (WMI), else the primary monitor over DDC/CI; null if neither answers.</summary>
    public static IBrightness? DetectBrightness()
    {
        var w = new WmiBrightness();
        if (w.Read() is not null) return w;
        var d = DdcBrightness.ForPrimary();
        if (d?.Read() is not null) return d;
        d?.Dispose();
        return null;
    }

    sealed class WmiBrightness : IBrightness
    {
        public bool IsBuiltIn => true;

        public int? Read()
        {
            try
            {
                using var s = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject o in s.Get())
                    using (o) return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException)
            {
            }
            return null;
        }

        public bool Set(int percent)
        {
            try
            {
                using var mc = new ManagementClass("root\\WMI", "WmiMonitorBrightnessMethods", null);
                foreach (ManagementObject o in mc.GetInstances())
                {
                    using (o) o.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)Math.Clamp(percent, 0, 100)]);
                    return true;
                }
            }
            catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException)
            {
            }
            return false;
        }
    }

    sealed class DdcBrightness : IBrightness, IDisposable
    {
        readonly Native.PHYSICAL_MONITOR[] monitors;
        public bool IsBuiltIn => false;

        DdcBrightness(Native.PHYSICAL_MONITOR[] m) => monitors = m;

        public static DdcBrightness? ForPrimary()
        {
            try
            {
                var hmon = Native.MonitorFromWindow(IntPtr.Zero, Native.MONITOR_DEFAULTTOPRIMARY);
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out uint n) || n == 0) return null;
                var arr = new Native.PHYSICAL_MONITOR[n];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(hmon, n, arr)) return null;
                return new DdcBrightness(arr);
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                return null;
            }
        }

        public int? Read()
        {
            var h = monitors[0].hPhysicalMonitor;
            if (!Native.GetMonitorBrightness(h, out uint min, out uint cur, out uint max) || max <= min) return null;
            return (int)Math.Round(100.0 * (cur - min) / (max - min));
        }

        public bool Set(int percent)
        {
            var h = monitors[0].hPhysicalMonitor;
            if (!Native.GetMonitorBrightness(h, out uint min, out _, out uint max)) return false;
            return Native.SetMonitorBrightness(h, (uint)Math.Round(min + (max - min) * Math.Clamp(percent, 0, 100) / 100.0));
        }

        public void Dispose() => Native.DestroyPhysicalMonitors((uint)monitors.Length, monitors);
    }
}
