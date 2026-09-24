using System.Runtime.InteropServices;

namespace AfterDork.App;

internal static partial class Native
{
    // MARK: - Windows

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll")]
    public static partial IntPtr SetParent(IntPtr child, IntPtr newParent);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageTime();

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    // MARK: - Blitting

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [LibraryImport("gdi32.dll")]
    public static partial int SetDIBitsToDevice(IntPtr hdc, int xDest, int yDest, uint w, uint h, int xSrc, int ySrc,
                                                uint startScan, uint lines, IntPtr bits, ref BITMAPINFOHEADER bmi, uint colorUse);

    // MARK: - Timers

    [LibraryImport("winmm.dll")]
    public static partial uint timeBeginPeriod(uint ms);

    [LibraryImport("winmm.dll")]
    public static partial uint timeEndPeriod(uint ms);

    // MARK: - System parameters

    public const uint SPI_GETSCREENSAVETIMEOUT = 0x000E;
    public const uint SPI_SETSCREENSAVETIMEOUT = 0x000F;
    public const uint SPI_GETSCREENSAVEACTIVE = 0x0010;
    public const uint SPI_SETSCREENSAVEACTIVE = 0x0011;
    public const uint SPIF_UPDATEINIFILE = 0x01;
    public const uint SPIF_SENDCHANGE = 0x02;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint uiParam, ref int pvParam, uint winIni);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint uiParam, IntPtr pvParam, uint winIni);

    // MARK: - Power

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadACValueIndex(IntPtr rootKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadDCValueIndex(IntPtr rootKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr LocalFree(IntPtr mem);

    public static readonly Guid GUID_VIDEO_SUBGROUP = new("7516b95f-f776-4464-8c53-06167f40cc99");
    public static readonly Guid GUID_VIDEO_POWERDOWN_TIMEOUT = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");

    // MARK: - DDC/CI monitor brightness (dxva2)

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint min, out uint cur, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetMonitorBrightness(IntPtr hMonitor, uint brightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    public const uint MONITOR_DEFAULTTOPRIMARY = 1;

    // MARK: - Console (so --render prints when launched from a terminal)

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(int processId);
}
