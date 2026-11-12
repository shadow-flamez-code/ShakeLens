// ============================================================================
//  ShakeLens - application shell
//
//  Lives in the notification area, listens for the shake gesture and shows
//  the selection overlay.
// ============================================================================

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShakeLens.Core;
using ShakeLens.Views;

namespace ShakeLens;

public partial class App : Application
{
    public static Settings Settings { get; private set; } = new();

    private static App? _instance;

    private HwndSource? _messageWindow;
    private TrayIcon? _tray;
    private IntPtr _trayIconHandle = IntPtr.Zero;
    private OverlayWindow? _overlay;
    private ResultWindow? _result;
    private SettingsWindow? _settingsWindow;
    private TrayMenuWindow? _trayMenu;
    private CancellationTokenSource? _cancellation;
    private Cursor? _appCursor;
    private AnimatedCursor? _animatedCursor;
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = this;

        if (e.Args.Contains("--selftest"))
        {
            try { SelfTest(); } catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "shakelens_selftest.txt"), ex.ToString()); }
            Shutdown();
            return;
        }

        // only one copy may run, otherwise the tray icon and the hotkey are
        // registered twice
        _singleInstance = new Mutex(true, "ShakeLens.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("ShakeLens is already running. Look for it in the notification area.",
                            "ShakeLens", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Settings = Core.Settings.Load();
        Settings.AutoStart = Settings.IsAutoStartEnabled();

        // a crash in the tray app should not disappear without a word
        DispatcherUnhandledException += (_, e) =>
        {
            Log("unhandled: " + e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log("fatal: " + e.ExceptionObject);
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _animatedCursor = AnimatedCursor.Load();
        _appCursor = _animatedCursor is { Frames.Count: > 0 } ? _animatedCursor.Frames[0] : null;

        CreateMessageWindow();
        CreateTrayIcon();
        RegisterHotkey();
        InstallKeyboardHook();
        StartDetection();

        if (e.Args.Contains("--settings"))
        {
            Dispatcher.BeginInvoke(new Action(ShowSettings));
        }
    }

    // ------------------------------------------------------------- plumbing --

    /// <summary>
    /// Feeds a synthetic shake into the detector and writes the result to the
    /// temp folder.  Handy when the gesture needs to be tuned.
    /// </summary>
    private static void SelfTest()
    {
        string log = Path.Combine(Path.GetTempPath(), "shakelens_selftest.txt");
        var lines = new List<string>();

        // (label, amplitude in px, half stroke in ms, sample interval in ms)
        var scenarios = new (string Name, int Amplitude, int HalfStrokeMs, int IntervalMs)[]
        {
            ("human 4 Hz, 75 px half stroke, 3 ms sampling", 75, 125, 3),
            ("human 3 Hz, 75 px half stroke, 3 ms sampling", 75, 167, 3),
            ("human 4 Hz, 40 px half stroke, 3 ms sampling", 40, 125, 3),
            ("human 4 Hz, 75 px half stroke, 16 ms sampling", 75, 125, 16),
            ("human 4 Hz, 75 px half stroke, 32 ms sampling", 75, 125, 32),
            ("flick 6 Hz, 120 px half stroke, 3 ms sampling", 120, 83, 3),
            ("tiny wobble 12 px, 5 Hz, 3 ms sampling", 12, 100, 3),
            ("slow drag (no shake)", 200, 600, 3)
        };

        foreach (var level in new[] { ShakeDetector.Sensitivity.Low, ShakeDetector.Sensitivity.Medium, ShakeDetector.Sensitivity.High })
        {
            foreach (var scenario in scenarios)
            {
                var detector = new ShakeDetector(level);
                double time = 0;
                double x = 500;
                int sign = 1;
                bool fired = false;
                int steps = (int)(3000 / scenario.IntervalMs);

                for (int i = 0; i < steps && !fired; i++)
                {
                    time += scenario.IntervalMs / 1000.0;
                    // move smoothly towards the next end point
                    x += sign * scenario.Amplitude;
                    sign = -sign;
                    fired = detector.Push(time, x, 500);
                }
                lines.Add($"{level,-6} {scenario.Name,-48} -> {(fired ? "DETECTED" : "missed")}");
            }
            lines.Add(string.Empty);
        }

        File.WriteAllLines(log, lines);
    }

    private void CreateMessageWindow()
    {
        var parameters = new HwndSourceParameters("ShakeLensMessageWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = 0,
            ExtendedWindowStyle = 0x0080   // WS_EX_TOOLWINDOW: no taskbar button
        };
        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shake_trace.log"),
            msg + " lp=" + lParam.ToInt64() + " wm=" + wParam.ToInt64() + Environment.NewLine);
        if (msg == TrayIcon.CallbackMessage)
        {
            int mouse = (int)lParam.ToInt64();
            if (mouse == 0x0205)          // WM_RBUTTONUP
            {
                ShowTrayMenu();
                handled = true;
            }
            else if (mouse == 0x0202)     // WM_LBUTTONUP
            {
                ShowSettings();
                handled = true;
            }
            else if (mouse == 0x0203)     // WM_LBUTTONDBLCLK
            {
                ShowSettings();
                handled = true;
            }
        }
        else if (msg == 0x0312)          // WM_HOTKEY
        {
            TriggerSelection();
            handled = true;
        }
        else if (msg == RegisterWindowMessage("TaskbarCreated"))
        {
            _tray?.Remove();
            CreateTrayIcon();
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    private void CreateTrayIcon()
    {
        _trayIconHandle = LoadTrayIcon();
        _tray = new TrayIcon(_messageWindow!.Handle, "ShakeLens - shake the mouse or press Win+Shift+G");
        _tray.Add(_trayIconHandle);
    }

    private IntPtr LoadTrayIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ShakeLens.ico"));
            if (stream is not null)
            {
                var decoder = BitmapDecoder.Create(stream.Stream,
                    BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

                // the icon holds several sizes, the tray wants the 32 px one
                BitmapFrame frame = decoder.Frames[0];
                foreach (BitmapFrame candidate in decoder.Frames)
                {
                    if (candidate.PixelWidth == 32) { frame = candidate; break; }
                }
                frame.Freeze();
                return Native.CreateHIcon(frame);
            }
        }
        catch { /* fall through */ }
        return IntPtr.Zero;
    }

    private void RegisterHotkey()
    {
        Native.RegisterHotkey(_messageWindow!.Handle, 1, 0x0008 | 0x0004, 0x47, OnHotkey);   // MOD_WIN | MOD_SHIFT + G  // MOD_CONTROL|MOD_SHIFT + G
    }

    private void OnHotkey(IntPtr hwnd, int id, uint modifiers, uint key) => TriggerSelection();

    // ------------------------------------------------------------ keyboard --

    private Native.LowLevelKeyboardProc? _keyboardProc;
    private IntPtr _keyboardHook;

    /// <summary>
    /// Watches the keyboard system wide.  The escape key closes the overlay
    /// and the result panel, no matter which window has the focus.
    /// </summary>
    private void InstallKeyboardHook()
    {
        _keyboardProc = (nCode, wParam, lParam) =>
        {
            if (nCode == 0 && (wParam == (IntPtr)0x0100 || wParam == (IntPtr)0x0101))
            {
                int key = Marshal.ReadInt32(lParam);
                if (key == 0x1B)      // VK_ESCAPE
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_overlay is not null) _overlay.Cancel();
                        else if (_result is not null) _result.Close();
                    }));
                }
            }
            return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        };

        _keyboardHook = Native.InstallKeyboardHook(_keyboardProc);
    }

    // ------------------------------------------------------------ detection --

    private void StartDetection()
    {
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        Native.BeginHighResolutionTimer();
        _ = Task.Run(async () =>
        {
            var detector = new ShakeDetector(Settings.Level);
            var clock = Stopwatch.StartNew();
            double cooldown = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    (int x, int y) = Native.CursorPosition();
                    double now = clock.Elapsed.TotalSeconds;
                    bool busy = Native.IsMouseButtonDown(0x01) || Native.IsMouseButtonDown(0x02);

                    if (busy || _overlay is not null || !Settings.ShakeEnabled)
                    {
                        detector.Reset();
                    }
                    else if (now > cooldown && detector.Push(now, x, y))
                    {
                        cooldown = now + 2.0;
                        Dispatcher.Invoke(TriggerSelection);
                    }

                    await Task.Delay(3, token).ConfigureAwait(false);                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "shake_error.log"),
                            ex + Environment.NewLine + Environment.NewLine);
                    }
                    catch { /* ignore */ }
                }
            }
        }, token);
    }

    // ------------------------------------------------------------ selection --

    private void TriggerSelection()
    {
        if (_overlay is not null) return;

        BitmapSource? screen = ScreenCapture.Capture(out int deviceWidth, out int deviceHeight,
                                                    out int deviceLeft, out int deviceTop);
        if (screen is null) return;

        double dipWidth = SystemParameters.VirtualScreenWidth;
        double dipHeight = SystemParameters.VirtualScreenHeight;
        double scale = dipWidth > 0 ? deviceWidth / dipWidth : 1.0;

        _overlay = new OverlayWindow();
        if (_animatedCursor is not null) _overlay.SetCursor(_animatedCursor);
        else if (_appCursor is not null) _overlay.Cursor = _appCursor;
        _overlay.Left = SystemParameters.VirtualScreenLeft;
        _overlay.Top = SystemParameters.VirtualScreenTop;
        _overlay.Width = dipWidth;
        _overlay.Height = dipHeight;
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.ShowFor(screen, scale, deviceLeft, deviceTop);
    }

    // ---------------------------------------------------------------- tray --

    private void ShowTrayMenu()
    {
        try
        {
            var menu = new TrayMenuWindow(new[]
            {
                new MenuEntry("\uE721", "Search the whole screen with Lens", () => LensWholeScreen()),
                new MenuEntry("-", "", null),
                new MenuEntry("\uE73E", "React to cursor shakes",
                    () => { Settings.ShakeEnabled = !Settings.ShakeEnabled; Settings.Save(); },
                    Settings.ShakeEnabled),
                new MenuEntry("\uE713", "Settings...", () => ShowSettings()),
                new MenuEntry("\uE73E", "Start with Windows",
                    () =>
                    {
                        bool enabled = !Settings.AutoStart;
                        Settings.SetAutoStart(enabled);
                        Settings.AutoStart = enabled;
                        Settings.Save();
                    },
                    Settings.AutoStart),
                new MenuEntry("-", "", null),
                new MenuEntry("\uE8BB", "Exit", () => Shutdown())
            });

            Log("opening the tray menu");
            menu.Show();
            _trayMenu = menu;
            menu.Closed += (_, _) => _trayMenu = null;
            Log("menu shown, visible=" + menu.IsVisible);
        }
        catch (Exception ex)
        {
            Log("tray menu: " + ex);
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "shake_error.log"),
                DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }
        catch { }
    }

    private void LensWholeScreen()
    {
        BitmapSource? screen = ScreenCapture.Capture(out _, out _, out _, out _);
        if (screen is null) return;

        BitmapSource scaled = ScreenCapture.Scale(screen, Settings.UploadSize);
        byte[] png = ScreenCapture.EncodePng(scaled);
        if (!GoogleLens.Open(png, Settings, out string error))
        {
            MessageBox.Show(error, "ShakeLens", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static void ShowResult(string title, string text, bool isError)
    {
        _instance?.PresentResult(title, text, isError);
    }

    private void PresentResult(string title, string text, bool isError)
    {
        _result?.Close();
        _result = new ResultWindow();
        _result.Present(null, title, text, isError);
        _result.Closed += (_, _) => _result = null;
    }

    private void ShowSettings()
    {
        try
        {
            _trayMenu?.Hide();
            if (_settingsWindow is not null)
            {
                _settingsWindow.Activate();
                return;
            }
            _settingsWindow = new SettingsWindow(Settings);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
        catch (Exception ex)
        {
            Log("settings: " + ex);
        }
    }

    // ------------------------------------------------------------- shutdown --

    protected override void OnExit(ExitEventArgs e)
    {
        _cancellation?.Cancel();
        Native.EndHighResolutionTimer();
        Native.RemoveKeyboardHook(_keyboardHook);
        _tray?.Dispose();
        base.OnExit(e);
    }
}
