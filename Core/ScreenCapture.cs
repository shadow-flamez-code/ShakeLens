// ============================================================================
//  ShakeLens - cropping, scaling and PNG encoding
// ============================================================================

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShakeLens.Core;

internal static class ScreenCapture
{
    public static BitmapSource? Capture(out int width, out int height, out int left, out int top)
        => Native.CaptureScreen(out width, out height, out left, out top);

    /// <summary>Crops using physical pixel coordinates.</summary>
    public static BitmapSource Crop(BitmapSource source, Int32Rect area)
    {
        int x = Math.Max(0, area.X);
        int y = Math.Max(0, area.Y);
        int width = Math.Min(area.Width, source.PixelWidth - x);
        int height = Math.Min(area.Height, source.PixelHeight - y);
        if (width <= 0 || height <= 0) return source;

        var cropped = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
        cropped.Freeze();
        return cropped;
    }

    public static BitmapSource Scale(BitmapSource source, int maxEdge)
    {
        int longest = Math.Max(source.PixelWidth, source.PixelHeight);
        if (maxEdge <= 0 || longest <= maxEdge) return source;

        double scale = (double)maxEdge / longest;
        var transform = new ScaleTransform(scale, scale);
        var scaled = new TransformedBitmap(source, transform);
        scaled.Freeze();
        return scaled;
    }

    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static void SavePng(BitmapSource source, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(file);
    }
}
