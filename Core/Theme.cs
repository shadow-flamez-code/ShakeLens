// ============================================================================
//  ShakeLens - colours, fonts and metrics
//
//  The palette is deliberately neutral and follows the Windows accent colour
//  when one is available, so the app blends into the system instead of
//  fighting it.
// ============================================================================

using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace ShakeLens.Core;

internal static class Theme
{
    // Surfaces - a cool, desaturated dark grey rather than pure black.
    public static readonly Color Scrim = Color.FromRgb(0x14, 0x18, 0x21);
    public static readonly Color Surface = Color.FromRgb(0x1E, 0x23, 0x2E);
    public static readonly Color SurfaceHigh = Color.FromRgb(0x2A, 0x30, 0x3D);
    public static readonly Color Outline = Color.FromRgb(0x39, 0x41, 0x52);
    public static readonly Color Text = Color.FromRgb(0xEC, 0xEF, 0xF5);
    public static readonly Color TextDim = Color.FromRgb(0x9A, 0xA4, 0xB6);
    public static readonly Color Danger = Color.FromRgb(0xF2, 0x6D, 0x6D);

    public static Color Accent { get; private set; } = Color.FromRgb(0x4C, 0x8D, 0xFF);
    public static Color AccentSoft { get; private set; } = Color.FromRgb(0x8A, 0xB4, 0xFF);

    static Theme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AccentColor") is int value)
            {
                // stored as ABGR
                var c = Color.FromArgb(0xFF, (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
                Accent = c;
                AccentSoft = Color.FromRgb(
                    (byte)Math.Min(255, c.R + 60),
                    (byte)Math.Min(255, c.G + 60),
                    (byte)Math.Min(255, c.B + 60));
            }
        }
        catch { /* keep the default */ }
    }

    public static Brush AccentBrush => Freeze(new SolidColorBrush(Accent));
    public static Brush AccentSoftBrush => Freeze(new SolidColorBrush(AccentSoft));
    public static Brush SurfaceBrush => Freeze(new SolidColorBrush(Surface));
    public static Brush SurfaceHighBrush => Freeze(new SolidColorBrush(SurfaceHigh));
    public static Brush OutlineBrush => Freeze(new SolidColorBrush(Outline));
    public static Brush TextBrush => Freeze(new SolidColorBrush(Text));
    public static Brush TextDimBrush => Freeze(new SolidColorBrush(TextDim));
    public static Brush DangerBrush => Freeze(new SolidColorBrush(Danger));
    public static Brush ScrimBrush => Freeze(new SolidColorBrush(Scrim));

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    public static FontFamily IconFont => new("Segoe MDL2 Assets");
    public static FontFamily UiFont => new("Segoe UI Variable Display, Segoe UI");
}
