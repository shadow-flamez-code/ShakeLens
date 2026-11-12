// ============================================================================
//  ShakeLens - the full screen selection overlay
// ============================================================================

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ShakeLens.Core;

namespace ShakeLens.Views;

public partial class OverlayWindow : Window
{
    private BitmapSource? _screen;
    private double _scale = 1.0;
    private int _screenLeft;
    private int _screenTop;
    private DispatcherTimer? _timer;

    private IReadOnlyList<Cursor> _cursorFrames = Array.Empty<Cursor>();
    private int _cursorIndex;
    private int _cursorDelay = 80;
    private DispatcherTimer? _cursorTimer;
    private DispatcherTimer? _uiTimer;

    private Point _dragStart;
    private Point _dragCurrent;
    private bool _dragging;
    private bool _hasSelection;

    private Rect _selection;
    private BitmapSource? _cropped;

    private bool _closing;

    public OverlayWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseAnimated(); };
    }

    // ---------------------------------------------------------------- setup --

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_screen is null) { Close(); return; }
        ScreenImage.Source = _screen;
        SelectionImage.Source = _screen;

        // fade in
        Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fade);

        // the glow around the selection breathes
        var breathe = new DoubleAnimation(0.25, 0.55, TimeSpan.FromMilliseconds(1400))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase()
        };
        GlowBorder.BeginAnimation(OpacityProperty, breathe);

        // the hint chip floats gently
        var drift = new DoubleAnimation(0, -6, TimeSpan.FromMilliseconds(1800))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase()
        };
        HintChip.BeginAnimation(TranslateTransform.YProperty, drift);

        Root.MouseLeftButtonDown += OnBackgroundMouseDown;
        Root.MouseMove += OnBackgroundMouseMove;
        Root.MouseLeftButtonUp += OnBackgroundMouseUp;
        Root.MouseRightButtonDown += (_, _) => CloseAnimated();

        // A timer keeps the overlay responsive.  It watches for the escape
        // key (the window itself cannot take the keyboard focus) and drives
        // the animations.
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _uiTimer.Tick += OnUiTick;
        _uiTimer.Start();
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        if (_closing) return;

        if (Native.IsEscapeDown())
        {
            CloseAnimated();
            return;
        }

        if (_dragging)
        {
            (int screenX, int screenY) = Native.CursorPosition();
            _dragCurrent = new Point(
                (screenX - _screenLeft) / _scale,
                (screenY - _screenTop) / _scale);
            UpdateSelectionVisual();
        }
        else if (_hasSelection)
        {
            // keep the glow and the marching highlights moving
            InvalidateAnimatedArea();
        }
    }

    private void InvalidateAnimatedArea()
    {
        Rect area = BuildRect(_dragStart, _dragCurrent);
        if (area.Width < 1 || area.Height < 1) return;

        Native.InvalidateRegion(new WindowInteropHelper(this).Handle,
            (int)area.X - 30, (int)area.Y - 30,
            (int)(area.X + area.Width) + 30, (int)(area.Y + area.Height) + 30);
    }

    public void ShowFor(BitmapSource screen, double scale, int screenLeft, int screenTop)
    {
        _screen = screen;
        _scale = scale;
        _screenLeft = screenLeft;
        _screenTop = screenTop;
        Show();
        Activate();
    }

    /// <summary>Uses an animated pointer while the overlay is open.</summary>
    public void SetCursor(AnimatedCursor cursor)
    {
        _cursorFrames = cursor.Frames;
        _cursorDelay = cursor.DelayMs;

        if (_cursorFrames.Count > 0)
        {
            Cursor = _cursorFrames[0];
            _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_cursorDelay) };
            _cursorTimer.Tick += (_, _) => AdvanceCursor();
            _cursorTimer.Start();
        }
    }

    private void AdvanceCursor()
    {
        if (_cursorFrames.Count == 0) return;
        _cursorIndex = (_cursorIndex + 1) % _cursorFrames.Count;
        Cursor = _cursorFrames[_cursorIndex];
    }

    // --------------------------------------------------------------- mouse --

    private void OnBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        // clicks on the toolbar belong to the toolbar
        if (e.OriginalSource is DependencyObject source && Toolbar.IsAncestorOf(source)) return;

        _dragging = true;
        _hasSelection = false;
        _dragStart = e.GetPosition(Root);
        _dragCurrent = _dragStart;

        // A timer drives the drag: WPF's mouse capture is unreliable on
        // layered (transparent) windows, but the raw button state is not.
        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += OnDragTick;
        }
        _timer.Start();
        UpdateSelectionVisual();
    }

    private void OnDragTick(object? sender, EventArgs e)
    {
        if (!_dragging) { _timer?.Stop(); return; }

        (int screenX, int screenY) = Native.CursorPosition();
        _dragCurrent = new Point(
            (screenX - _screenLeft) / _scale,
            (screenY - _screenTop) / _scale);
        UpdateSelectionVisual();

        if (!Native.IsMouseButtonDown(0x01)) FinishDrag();
    }

    private void FinishDrag()
    {
        _dragging = false;
        _timer?.Stop();

        Rect area = BuildRect(_dragStart, _dragCurrent);
        if (area.Width < 12 || area.Height < 12)
        {
            CloseAnimated();
            return;
        }

        _selection = area;
        _hasSelection = true;

        // crop the frozen screen (the image is in physical pixels)
        var pixelRect = new Int32Rect(
            (int)Math.Round(area.X * _scale),
            (int)Math.Round(area.Y * _scale),
            (int)Math.Round(area.Width * _scale),
            (int)Math.Round(area.Height * _scale));
        _cropped = ScreenCapture.Crop(_screen!, pixelRect);

        ShowToolbar();
    }

    private void OnBackgroundMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragCurrent = e.GetPosition(Root);
        UpdateSelectionVisual();
    }

    private void OnBackgroundMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        FinishDrag();
    }

    private static Rect BuildRect(Point a, Point b) => new(
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Abs(a.X - b.X),
        Math.Abs(a.Y - b.Y));

    private void UpdateSelectionVisual()
    {
        Rect area = BuildRect(_dragStart, _dragCurrent);
        if (area.Width < 1 || area.Height < 1)
        {
            SelectionImage.Visibility = Visibility.Collapsed;
            SelectionBorder.Visibility = Visibility.Collapsed;
            GlowBorder.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionImage.Visibility = Visibility.Visible;
        SelectionImage.Clip = new RectangleGeometry(area);

        SelectionBorder.Visibility = Visibility.Visible;
        SelectionBorder.Width = area.Width;
        SelectionBorder.Height = area.Height;
        SelectionBorder.Margin = new Thickness(area.X, area.Y, 0, 0);

        GlowBorder.Visibility = Visibility.Visible;
        GlowBorder.Width = area.Width;
        GlowBorder.Height = area.Height;
        GlowBorder.Margin = new Thickness(area.X, area.Y, 0, 0);
    }

    // ------------------------------------------------------------- toolbar --

    private void ShowToolbar()
    {
        Toolbar.Visibility = Visibility.Visible;
        Toolbar.Opacity = 0;
        ToolbarSlide.BeginAnimation(TranslateTransform.YProperty, null);
        ToolbarSlide.Y = 14;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Toolbar.BeginAnimation(OpacityProperty, fade);

        var slide = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ToolbarSlide.BeginAnimation(TranslateTransform.YProperty, slide);

        // place the toolbar right below the selection
        Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = Toolbar.DesiredSize.Width;
        double height = Toolbar.DesiredSize.Height;

        double x = _selection.X + _selection.Width / 2 - width / 2;
        double y = _selection.Y + _selection.Height + 16;

        if (y + height > ActualHeight - 12)
        {
            double above = _selection.Y - height - 16;
            y = above >= 12 ? above : Math.Max(12, ActualHeight - height - 12);
        }
        x = Math.Max(12, Math.Min(x, ActualWidth - width - 12));
        y = Math.Max(12, Math.Min(y, ActualHeight - height - 12));

        Toolbar.Margin = new Thickness(x, y, 0, 0);

        HintChip.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------------- actions --

    private void OnLensClick(object sender, RoutedEventArgs e)
    {
        if (_cropped is null) return;
        try
        {
            BitmapSource scaled = ScreenCapture.Scale(_cropped, App.Settings.UploadSize);
            byte[] png = ScreenCapture.EncodePng(scaled);
            if (!GoogleLens.Open(png, App.Settings, out string error))
                ShowToast(error);
            else
                CloseAnimated();
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message);
        }
    }

    private void OnCopyImageClick(object sender, RoutedEventArgs e)
    {
        if (_cropped is null) return;
        try
        {
            Clipboard.SetImage(_cropped);
            ShowToast("Image copied to the clipboard");
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message);
        }
    }

    private async void OnCopyTextClick(object sender, RoutedEventArgs e)
    {
        if (_cropped is null) return;
        SetBusy(true);
        try
        {
            BitmapSource scaled = ScreenCapture.Scale(_cropped, 2000);
            byte[] png = ScreenCapture.EncodePng(scaled);

            (bool ok, string text, string error) = await Task.Run(() =>
            {
                bool success = OcrService.Recognize(png, out string result, out string message);
                return (success, result, message);
            });

            if (ok)
            {
                try { Clipboard.SetText(text); } catch { /* ignore */ }
                App.ShowResult("Text on screen", text, false);
                CloseAnimated();
            }
            else
            {
                ShowToast("No text found");
            }
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnAskAiClick(object sender, RoutedEventArgs e)
    {
        if (_cropped is null) return;
        SetBusy(true);
        try
        {
            BitmapSource scaled = ScreenCapture.Scale(_cropped, 1400);
            byte[] png = ScreenCapture.EncodePng(scaled);

            AiReply reply = await AiService.AskAsync(png, App.Settings.AiPrompt, App.Settings);
            if (reply.Ok)
            {
                App.ShowResult("AI answer", reply.Text, false);
                CloseAnimated();
            }
            else
            {
                App.ShowResult("AI answer", reply.Error, true);
                CloseAnimated();
            }
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseAnimated();

    private void SetBusy(bool busy)
    {
        LensButton.IsEnabled = !busy;
        CopyTextButton.IsEnabled = !busy;
        CopyImageButton.IsEnabled = !busy;
        AskAiButton.IsEnabled = !busy;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
    }

    // --------------------------------------------------------------- toast --

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        Toast.Opacity = 0;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
        Toast.BeginAnimation(OpacityProperty, fade);

        var hide = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260))
        {
            BeginTime = TimeSpan.FromMilliseconds(1900)
        };
        hide.Completed += (_, _) => Toast.Visibility = Visibility.Collapsed;
        Toast.BeginAnimation(OpacityProperty, hide);
    }

    // -------------------------------------------------------------- closing --

    /// <summary>Closes the overlay, used by the escape key handler.</summary>
    public void Cancel() => CloseAnimated();

    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;

        _cursorTimer?.Stop();
        _timer?.Stop();
        _uiTimer?.Stop();

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
