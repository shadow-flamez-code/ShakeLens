// ============================================================================
//  ShakeLens - Win32 interop used by the app
// ============================================================================

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShakeLens.Core;

internal static class Native
{
    // ------------------------------------------------------------- capture --

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public int bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateBitmap(int cx, int cy, int planes, int bitsPerPixel, IntPtr bits);
    [DllImport("user32.dll")] private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;
    private const uint DIB_RGB_COLORS = 0;

    /// <summary>Grabs the whole virtual desktop as a 32bpp BGRA bitmap.</summary>
    public static BitmapSource? CaptureScreen(out int width, out int height, out int left, out int top)
    {
        // the raw metrics are physical pixels, SystemParameters are DIPs
        left = GetSystemMetrics(76);      // SM_XVIRTUALSCREEN
        top = GetSystemMetrics(77);       // SM_YVIRTUALSCREEN
        width = GetSystemMetrics(78);     // SM_CXVIRTUALSCREEN
        height = GetSystemMetrics(79);    // SM_CYVIRTUALSCREEN
        if (width <= 0 || height <= 0)
        {
            width = (int)SystemParameters.VirtualScreenWidth;
            height = (int)SystemParameters.VirtualScreenHeight;
            left = (int)SystemParameters.VirtualScreenLeft;
            top = (int)SystemParameters.VirtualScreenTop;
        }
        if (width <= 0 || height <= 0) return null;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);

        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };

        IntPtr dib = CreateDIBSection(screenDc, ref info, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero)
        {
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
            return null;
        }

        IntPtr previous = SelectObject(memDc, dib);
        bool ok = BitBlt(memDc, 0, 0, width, height, screenDc, left, top, SRCCOPY | CAPTUREBLT);
        SelectObject(memDc, previous);

        BitmapSource? result = null;
        if (ok)
        {
            var buffer = new byte[width * height * 4];
            Marshal.Copy(bits, buffer, 0, buffer.Length);
            result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, buffer, width * 4);
            result.Freeze();
        }

        DeleteObject(dib);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);
        return result;
    }

    /// <summary>Turns a bitmap into an HICON that the shell can show in the tray.</summary>
    public static IntPtr CreateHIcon(BitmapSource source)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);

        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };

        IntPtr dib = CreateDIBSection(screenDc, ref info, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero)
        {
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
            return IntPtr.Zero;
        }

        var buffer = new byte[width * height * 4];
        source.CopyPixels(buffer, width * 4, 0);
        Marshal.Copy(buffer, 0, bits, buffer.Length);

        IntPtr mask = CreateBitmap(width, height, 1, 1, IntPtr.Zero);
        var icon = new ICONINFO
        {
            fIcon = true,
            hbmColor = dib,
            hbmMask = mask
        };
        IntPtr handle = CreateIconIndirect(ref icon);

        DeleteObject(mask);
        DeleteObject(dib);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);
        return handle;
    }

    // --------------------------------------------------------------- mouse --

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);

    /// <summary>
    /// Windows rounds timers to 15.6 ms by default, which is far too coarse
    /// for a short gesture.  Raising the resolution makes the sampling
    /// accurate.
    /// </summary>
    public static void BeginHighResolutionTimer() => timeBeginPeriod(1);
    public static void EndHighResolutionTimer() => timeEndPeriod(1);

    public static (int X, int Y) CursorPosition()
    {
        if (GetCursorPos(out POINT p)) return (p.X, p.Y);
        return (0, 0);
    }

    public static bool IsMouseButtonDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>True while the escape key is held down.</summary>
    public static bool IsEscapeDown() => (GetAsyncKeyState(0x1B) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, ref RECT rect, bool erase);

    /// <summary>Repaints only the given part of the window.</summary>
    public static void InvalidateRegion(IntPtr window, int left, int top, int right, int bottom)
    {
        RECT region = new RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        InvalidateRect(window, ref region, false);
    }

    // ------------------------------------------------------------- hotkeys --

    public delegate void HotkeyHandler(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static bool RegisterHotkey(IntPtr hWnd, int id, uint modifiers, uint key, HotkeyHandler handler)
    {
        IntPtr previous = Marshal.GetFunctionPointerForDelegate(handler);
        // keep the delegate alive for the lifetime of the registration
        HotkeyRegistry.Keep(previous);
        return RegisterHotKey(hWnd, id, modifiers | 0x4000 /* MOD_NOREPEAT */, key);
    }

    public static void UnregisterHotkey(IntPtr hWnd, int id) => UnregisterHotKey(hWnd, id);

    private static class HotkeyRegistry
    {
        private static readonly List<IntPtr> Kept = new();
        public static void Keep(IntPtr p) { lock (Kept) Kept.Add(p); }
    }

    // ---------------------------------------------------------- keyboard --

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
                                                  IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Installs a global keyboard hook.  GetAsyncKeyState only reports the
    /// keys that this thread received a message for, so the escape key has
    /// to be watched with a low level hook instead.
    /// </summary>
    public static IntPtr InstallKeyboardHook(LowLevelKeyboardProc callback)
    {
        IntPtr module = GetModuleHandle(null);
        return SetWindowsHookEx(13, callback, module, 0);   // WH_KEYBOARD_LL
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    public static void RemoveKeyboardHook(IntPtr hook)
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
    }

    // ---------------------------------------------------------- window api --

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref bool value, int size);

    public static void UseDarkTitleBar(Window window)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            bool dark = true;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(bool));   // Win10 2004+
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(bool));   // older builds
        }
        catch { /* not available on older Windows */ }
    }

    public static void UseRoundedCorners(Window window)
    {
        try
        {
            int preference = 2;  // DWMWCP_ROUND
            DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 33, ref preference, sizeof(int));
        }
        catch { /* not available on older Windows */ }
    }
}
