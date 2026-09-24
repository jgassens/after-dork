using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

// Test driver for exercising the real Windows UI: screen captures, synthetic
// mouse/keyboard input, window lookup, and triggering the screen saver.
// Coordinates are physical screen pixels (the driver is per-monitor DPI aware).
//
//   uidriver screens
//   uidriver capture <out.png> [x y w h]
//   uidriver window <title-substring>          -> hwnd, window rect, client rect (screen coords), dpi
//   uidriver children <hwnd>                   -> child windows
//   uidriver click <x> <y> [right]
//   uidriver move <x> <y>
//   uidriver drag <x1> <y1> <x2> <y2> [steps]
//   uidriver key <VK name or hex>              e.g. SPACE, ESCAPE, 0x41
//   uidriver wheel
//   uidriver startsaver                        -> WM_SYSCOMMAND SC_SCREENSAVE
//   uidriver close <title-substring>
//   uidriver pixel <x> <y>

ApplicationConfiguration.Initialize();
if (args.Length == 0) { Console.Error.WriteLine("see source for usage"); return 2; }
int I(int i) => int.Parse(args[i]);

switch (args[0])
{
    case "screens":
        foreach (var s in Screen.AllScreens)
            Console.WriteLine($"{s.DeviceName} bounds={R(s.Bounds)} primary={s.Primary}");
        return 0;

    case "capture":
    {
        var rect = args.Length >= 6 ? new Rectangle(I(2), I(3), I(4), I(5)) : SystemInformation.VirtualScreen;
        using var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
        bmp.Save(args[1], ImageFormat.Png);
        Console.WriteLine($"captured {R(rect)} -> {args[1]}");
        return 0;
    }

    case "pixel":
    {
        using var bmp = new Bitmap(1, 1);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(I(1), I(2), 0, 0, new Size(1, 1));
        var c = bmp.GetPixel(0, 0);
        Console.WriteLine($"{c.R},{c.G},{c.B}");
        return 0;
    }

    case "window":
    case "close":
    {
        var found = FindWindows(args[1]);
        if (found.Count == 0) { Console.WriteLine("not found"); return 1; }
        foreach (var h in found)
        {
            if (args[0] == "close") { PostMessage(h, 0x0010, 0, 0); Console.WriteLine($"closed {h}"); continue; }
            GetWindowRect(h, out var wr);
            GetClientRect(h, out var cr);
            var pt = new POINT();
            ClientToScreen(h, ref pt);
            Console.WriteLine($"hwnd={h} title=\"{Title(h)}\" window=({wr.L},{wr.T},{wr.R - wr.L},{wr.B - wr.T}) client=({pt.X},{pt.Y},{cr.R},{cr.B}) dpi={GetDpiForWindow(h)}");
        }
        return 0;
    }

    case "children":
    {
        var parent = new IntPtr(long.Parse(args[1]));
        EnumChildWindows(parent, (h, _) =>
        {
            GetWindowRect(h, out var wr);
            var cls = new StringBuilder(256);
            GetClassName(h, cls, 256);
            Console.WriteLine($"hwnd={h} class={cls} title=\"{Title(h)}\" rect=({wr.L},{wr.T},{wr.R - wr.L},{wr.B - wr.T}) visible={IsWindowVisible(h)}");
            return true;
        }, IntPtr.Zero);
        return 0;
    }

    case "pidwindows":
    {
        uint pid = uint.Parse(args[1]);
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p != pid) return true;
            GetWindowRect(h, out var wr);
            var cls = new StringBuilder(256);
            GetClassName(h, cls, 256);
            Console.WriteLine($"top hwnd={h} class={cls} title=\"{Title(h)}\" rect=({wr.L},{wr.T},{wr.R - wr.L},{wr.B - wr.T}) visible={IsWindowVisible(h)} parent={GetParent(h)}");
            return true;
        }, IntPtr.Zero);
        return 0;
    }

    case "move":
        SetCursorPos(I(1), I(2));
        Mouse(0x0001, 0);  // a real move event at the new position
        return 0;

    case "click":
    {
        SetCursorPos(I(1), I(2));
        Thread.Sleep(30);
        bool right = args.Length > 3 && args[3] == "right";
        Mouse(right ? 0x0008u : 0x0002u, 0);
        Thread.Sleep(40);
        Mouse(right ? 0x0010u : 0x0004u, 0);
        return 0;
    }

    case "drag":
    {
        int x1 = I(1), y1 = I(2), x2 = I(3), y2 = I(4), steps = args.Length > 5 ? I(5) : 12;
        SetCursorPos(x1, y1);
        Thread.Sleep(30);
        Mouse(0x0002, 0);
        for (int i = 1; i <= steps; i++)
        {
            Thread.Sleep(15);
            SetCursorPos(x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps);
            Mouse(0x0001, 0);
        }
        Thread.Sleep(30);
        Mouse(0x0004, 0);
        return 0;
    }

    case "wheel":
        Mouse(0x0800, 120);
        return 0;

    case "key":
    {
        ushort vk = args[1].StartsWith("0x") ? Convert.ToUInt16(args[1], 16) : (ushort)Enum.Parse<Keys>(args[1], true);
        Key(vk, false);
        Thread.Sleep(30);
        Key(vk, true);
        return 0;
    }

    case "on-trace":
    {
        // on-trace <file> <text> <delayMs> <VK> : wait until <file> contains <text>
        // (appearing after this command starts), then press <VK> after delayMs.
        string file = args[1], text = args[2];
        int skip = File.Exists(file) ? File.ReadAllText(file).Length : 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 30)
        {
            string all = File.Exists(file) ? ReadShared(file) : "";
            if (all.Length > skip && all[skip..].Contains(text)) break;
            Thread.Sleep(5);
        }
        Thread.Sleep(I(3));
        ushort vk = (ushort)Enum.Parse<Keys>(args[4], true);
        Key(vk, false);
        Thread.Sleep(30);
        Key(vk, true);
        Console.WriteLine($"pressed {args[4]} {I(3)} ms after \"{text}\"");
        return 0;
    }

    case "startsaver":
        // DefWindowProc starts the configured screen saver on SC_SCREENSAVE.
        // startsaver [window-title]: send it to that window (else the desktop).
        var target = args.Length > 1 ? FindWindows(args[1]).FirstOrDefault() : GetDesktopWindow();
        PostMessage(target == IntPtr.Zero ? GetDesktopWindow() : target, 0x0112, 0xF140, 0);
        return 0;

    default:
        Console.Error.WriteLine($"unknown command {args[0]}");
        return 2;
}

static string ReadShared(string path)
{
    try
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }
    catch (IOException) { return ""; }
}

static string R(Rectangle r) => $"({r.X},{r.Y},{r.Width},{r.Height})";

static List<IntPtr> FindWindows(string titlePart)
{
    var list = new List<IntPtr>();
    EnumWindows((h, _) =>
    {
        if (IsWindowVisible(h) && Title(h).Contains(titlePart, StringComparison.OrdinalIgnoreCase)) list.Add(h);
        return true;
    }, IntPtr.Zero);
    return list;
}

static string Title(IntPtr h)
{
    var sb = new StringBuilder(512);
    GetWindowText(h, sb, sb.Capacity);
    return sb.ToString();
}

static void Mouse(uint flags, int data)
{
    var inp = new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data } } };
    SendInput(1, [inp], Marshal.SizeOf<INPUT>());
}

static void Key(ushort vk, bool up)
{
    var inp = new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u } } };
    SendInput(1, [inp], Marshal.SizeOf<INPUT>());
}

[DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
[DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
[DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
[DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
[DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
[DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, nint w, nint l);
[DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);

delegate bool EnumProc(IntPtr h, IntPtr l);

[StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
[StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
struct INPUT { public uint type; public InputUnion u; }

[StructLayout(LayoutKind.Explicit)]
struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
struct MOUSEINPUT { public int dx, dy; public int mouseData; public uint dwFlags, time; public IntPtr dwExtraInfo; }

[StructLayout(LayoutKind.Sequential)]
struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
