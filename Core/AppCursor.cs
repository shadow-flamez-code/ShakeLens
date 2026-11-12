// ============================================================================
//  ShakeLens - the pointer
//
//  The bundled pointer is an animated cursor: 30 frames of a 32 px arrow with
//  an azure aura.  The frames live in the executable as resources, a cursor
//  in %APPDATA%\ShakeLens\cursor.cur replaces them when it is present.
// ============================================================================

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShakeLens.Core;

public sealed class AnimatedCursor
{
    public IReadOnlyList<Cursor> Frames { get; }
    public int DelayMs { get; }

    public AnimatedCursor(IReadOnlyList<Cursor> frames, int delayMs)
    {
        Frames = frames;
        DelayMs = Math.Max(16, delayMs);
    }

    public bool IsAnimated => Frames.Count > 1;

    // ------------------------------------------------------------- loading --

    private sealed class FrameInfo
    {
        public string File { get; set; } = "";
        public int Delay { get; set; } = 80;
        public int[] Hotspot { get; set; } = new[] { 0, 0 };
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public static AnimatedCursor? Load()
    {
        // a cursor in the data folder always wins
        string custom = Path.Combine(Settings.Folder, "cursor.cur");
        if (File.Exists(custom))
        {
            Cursor? single = LoadCursorFile(custom);
            if (single is not null) return new AnimatedCursor(new[] { single }, 80);
        }

        return LoadBundledFrames();
    }

    private static AnimatedCursor? LoadBundledFrames()
    {
        try
        {
            var infos = new List<FrameInfo>();
            int delay = 83;

            byte[]? manifest = ReadResource("Assets/Cursor/frames.json");
            if (manifest is not null)
            {
                var parsed = JsonSerializer.Deserialize<List<FrameInfo>>(manifest, JsonOptions);
                if (parsed is not null && parsed.Count > 0)
                {
                    infos = parsed;
                    delay = parsed[0].Delay;
                }
            }

            if (infos.Count == 0)
            {
                // no manifest, look for frame_00.png, frame_01.png ...
                for (int i = 0; i < 64; i++)
                {
                    string name = $"frame_{i:D2}.png";
                    if (ReadResource("Assets/Cursor/" + name) is null) break;
                    infos.Add(new FrameInfo { File = name, Delay = delay, Hotspot = new[] { 6, 1 } });
                }
            }

            if (infos.Count == 0) return null;

            var frames = new List<Cursor>();
            foreach (FrameInfo info in infos)
            {
                byte[]? png = ReadResource("Assets/Cursor/" + info.File);
                if (png is null) continue;

                int hotX = info.Hotspot.Length > 0 ? info.Hotspot[0] : 0;
                int hotY = info.Hotspot.Length > 1 ? info.Hotspot[1] : 0;

                var decoder = BitmapDecoder.Create(new MemoryStream(png),
                    BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource bitmap = decoder.Frames[0];
                bitmap.Freeze();

                Cursor? cursor = CreateCursor(bitmap, hotX, hotY);
                if (cursor is not null) frames.Add(cursor);
            }

            if (frames.Count == 0) return null;
            return new AnimatedCursor(frames, delay);
        }
        catch
        {
            return null;
        }
    }

    private static Cursor? LoadCursorFile(string path)
    {
        try
        {
            byte[]? data = File.ReadAllBytes(path);
            if (data is null) return null;

            CursorFrame? frame = PickFrame(data);
            if (frame is null) return null;

            var decoder = BitmapDecoder.Create(new MemoryStream(frame.Pixels),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource bitmap = decoder.Frames[0];
            bitmap.Freeze();

            return CreateCursor(bitmap, frame.HotspotX, frame.HotspotY);
        }
        catch
        {
            return null;
        }
    }

    private sealed record CursorFrame(byte[] Pixels, int HotspotX, int HotspotY);

    /// <summary>
    /// Reads a .cur file and returns the picture that matches the system
    /// cursor size.  Both the regular layout and the packed one (where the
    /// hotspot sits where the plane count would be) are understood.
    /// </summary>
    private static CursorFrame? PickFrame(byte[] data)
    {
        if (data.Length < 6) return null;
        int type = data[2] | (data[3] << 8);
        int count = data[4] | (data[5] << 8);
        if (type != 1 && type != 2) return null;

        bool standard = (data[10] | (data[11] << 8)) == 1;
        int stride = standard ? (type == 2 ? 20 : 16) : 16;

        int wanted = 32;
        CursorFrame? best = null;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < count; i++)
        {
            int offset = 6 + i * stride;
            if (offset + 16 > data.Length) break;

            int width = data[offset];
            int height = data[offset + 1];
            int hotX, hotY, bytes, imageOffset;

            if (standard && type == 2)
            {
                hotX = data[offset + 16] | (data[offset + 17] << 8);
                hotY = data[offset + 18] | (data[offset + 19] << 8);
                bytes = BitConverter.ToInt32(data, offset + 8);
                imageOffset = BitConverter.ToInt32(data, offset + 12);
            }
            else
            {
                hotX = data[offset + 4] | (data[offset + 5] << 8);
                hotY = data[offset + 6] | (data[offset + 7] << 8);
                bytes = BitConverter.ToInt32(data, offset + 8);
                imageOffset = BitConverter.ToInt32(data, offset + 12);
            }

            if (bytes <= 0 || imageOffset < 0 || imageOffset + bytes > data.Length) continue;

            int distance = Math.Abs(width - wanted);
            if (distance >= bestDistance) continue;

            var pixels = new byte[bytes];
            Array.Copy(data, imageOffset, pixels, 0, bytes);
            best = new CursorFrame(pixels, hotX, hotY);
            bestDistance = distance;
        }
        return best;
    }

    // ------------------------------------------------------------- helpers --

    /// <summary>
    /// WPF has no cursor constructor that takes a hotspot, so a small .cur
    /// file is built in memory and handed to the shell loader.
    /// </summary>
    private static Cursor? CreateCursor(BitmapSource source, int hotX, int hotY)
    {
        try
        {
            var converted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = width * 4;

            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            // the AND mask marks the transparent pixels
            int maskStride = ((width + 31) / 32) * 4;
            var mask = new byte[maskStride * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte alpha = pixels[y * stride + x * 4 + 3];
                    if (alpha < 128)
                    {
                        int bit = y * maskStride + x / 8;
                        mask[bit] |= (byte)(0x80 >> (x % 8));
                    }
                }
            }

            var xor = new byte[pixels.Length];
            for (int y = 0; y < height; y++)
                Array.Copy(pixels, (height - 1 - y) * stride, xor, y * stride, stride);

            var header = new byte[40];
            Array.Copy(BitConverter.GetBytes(40), 0, header, 0, 4);
            Array.Copy(BitConverter.GetBytes(width), 0, header, 4, 4);
            Array.Copy(BitConverter.GetBytes(height * 2), 0, header, 8, 4);
            Array.Copy(BitConverter.GetBytes(1), 0, header, 12, 4);
            Array.Copy(BitConverter.GetBytes(32), 0, header, 14, 4);
            Array.Copy(BitConverter.GetBytes(xor.Length + mask.Length), 0, header, 20, 4);

            int imageSize = header.Length + xor.Length + mask.Length;
            const int entrySize = 20;                 // cursor directory entry
            const int imageOffset = 6 + entrySize;    // right after the entry

            var blob = new byte[imageOffset + imageSize];
            Array.Copy(BitConverter.GetBytes((ushort)0), 0, blob, 0, 2);
            Array.Copy(BitConverter.GetBytes((ushort)2), 0, blob, 2, 2);
            Array.Copy(BitConverter.GetBytes((ushort)1), 0, blob, 4, 2);
            blob[6] = (byte)(width & 0xFF);
            blob[7] = (byte)(height & 0xFF);
            blob[8] = 0;
            blob[9] = 0;
            Array.Copy(BitConverter.GetBytes((ushort)1), 0, blob, 10, 2);
            Array.Copy(BitConverter.GetBytes((ushort)32), 0, blob, 12, 2);
            Array.Copy(BitConverter.GetBytes(imageSize), 0, blob, 14, 4);
            Array.Copy(BitConverter.GetBytes(imageOffset), 0, blob, 18, 4);
            Array.Copy(BitConverter.GetBytes((ushort)hotX), 0, blob, 22, 2);
            Array.Copy(BitConverter.GetBytes((ushort)hotY), 0, blob, 24, 2);
            Array.Copy(header, 0, blob, imageOffset, header.Length);
            Array.Copy(xor, 0, blob, imageOffset + header.Length, xor.Length);
            Array.Copy(mask, 0, blob, imageOffset + header.Length + xor.Length, mask.Length);

            var cursor = new Cursor(new MemoryStream(blob));
            return cursor;
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ReadResource(string path)
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/" + path));
            if (stream is null) return null;
            using var memory = new MemoryStream();
            stream.Stream.CopyTo(memory);
            byte[] bytes = memory.ToArray();

            // strip a byte order mark, the json parser does not like it
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                var trimmed = new byte[bytes.Length - 3];
                Array.Copy(bytes, 3, trimmed, 0, trimmed.Length);
                return trimmed;
            }
            return bytes;
        }
        catch
        {
            return null;
        }
    }
}
