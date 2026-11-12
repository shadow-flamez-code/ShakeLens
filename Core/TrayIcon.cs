// ============================================================================
//  ShakeLens - the notification area icon
// ============================================================================

using System.Runtime.InteropServices;

namespace ShakeLens.Core;

internal sealed class TrayIcon : IDisposable
{
    private const int NIM_ADD = 0;
    private const int NIM_MODIFY = 1;
    private const int NIM_DELETE = 2;

    private const int NIF_MESSAGE = 1;
    private const int NIF_ICON = 2;
    private const int NIF_TIP = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex,
                                             IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public const int CallbackMessage = 0x8000 + 21;   // WM_APP + 21

    private IntPtr _hIcon;
    private IntPtr _hIconSmall;
    private NOTIFYICONDATA _data;
    private bool _added;

    public TrayIcon(IntPtr owner, string tip)
    {
        _hIcon = IntPtr.Zero;
        _hIconSmall = IntPtr.Zero;

        _data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = owner,
            uID = 1,
            uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP,
            uCallbackMessage = CallbackMessage,
            szTip = tip
        };
    }

    /// <summary>Uses the icon that is embedded in the executable.</summary>
    public bool Add(IntPtr iconHandle)
    {
        _hIcon = iconHandle;
        _data.hIcon = iconHandle;
        _added = Shell_NotifyIcon(NIM_ADD, ref _data);
        return _added;
    }

    public void Update(string tip)
    {
        _data.szTip = tip;
        Shell_NotifyIcon(NIM_MODIFY, ref _data);
    }

    public void Remove()
    {
        if (!_added) return;
        Shell_NotifyIcon(NIM_DELETE, ref _data);
        _added = false;
    }

    public void Dispose()
    {
        Remove();
        if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
    }
}
