using SkiaSharp;

namespace S1Panel.Core.Lcd;

/// <summary>Converts between drawable SKBitmaps (in logical orientation) and the native BE RGB565 framebuffer.</summary>
public static class FrameConverter
{
    private const int W = LcdPanel.Width;
    private const int H = LcdPanel.Height;

    /// <summary>Creates a blank canvas bitmap of the right size for the orientation.</summary>
    public static SKBitmap CreateCanvasBitmap(Orientation o)
    {
        var (w, h) = o.LogicalSize();
        return new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
    }

    /// <summary>Maps a native framebuffer pixel to the logical (drawn) pixel it shows.</summary>
    internal static (int X, int Y) NativeToLogical(Orientation o, int nx, int ny) => o switch
    {
        Orientation.Landscape => (nx, ny),
        Orientation.LandscapeFlipped => (W - 1 - nx, H - 1 - ny),
        // Matches the original software: translate(0,170) then rotate(-90deg); logical (x,y) -> native (y, 169-x).
        Orientation.Portrait => (H - 1 - ny, nx),
        Orientation.PortraitFlipped => (ny, W - 1 - nx),
        _ => throw new ArgumentOutOfRangeException(nameof(o)),
    };

    /// <summary>Renders a logical bitmap into a native frame (108800 bytes, BE RGB565).</summary>
    // 4x4 Bayer matrix (0..15). Added as t/2 to 5-bit channels (step 8) and t/4 to the 6-bit channel (step 4).
    private static readonly int[] Bayer4 = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    public static void ToNative(SKBitmap logical, Orientation o, Span<byte> frame, bool dither = true)
    {
        if (frame.Length != LcdPanel.FrameBytes)
            throw new ArgumentException($"Frame must be {LcdPanel.FrameBytes} bytes.");

        var (lw, lh) = o.LogicalSize();
        SKBitmap src = logical;
        bool owned = false;
        if (logical.Width != lw || logical.Height != lh || logical.ColorType != SKColorType.Rgba8888)
        {
            src = new SKBitmap(new SKImageInfo(lw, lh, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var c = new SKCanvas(src);
            c.Clear(SKColors.Black);
            c.DrawBitmap(logical, new SKRect(0, 0, lw, lh), new SKSamplingOptions(SKFilterMode.Linear));
            owned = true;
        }

        try
        {
            ReadOnlySpan<byte> px = src.GetPixelSpan();
            int stride = src.RowBytes;
            int i = 0;
            for (int ny = 0; ny < H; ny++)
            {
                for (int nx = 0; nx < W; nx++)
                {
                    var (lx, ly) = NativeToLogical(o, nx, ny);
                    int s = ly * stride + lx * 4;
                    // Premultiplied alpha over black == the premultiplied RGB values themselves.
                    // Ordered dither keyed on the logical pixel: hides 565 banding in gradients, and since the
                    // pattern is fixed per pixel it never makes an unchanged area look changed.
                    int t = dither ? Bayer4[(ly & 3) * 4 + (lx & 3)] : 0;
                    int r = Math.Min(255, px[s] + (t >> 1)), g = Math.Min(255, px[s + 1] + (t >> 2)), b = Math.Min(255, px[s + 2] + (t >> 1));
                    ushort c565 = (ushort)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
                    frame[i++] = (byte)(c565 >> 8);
                    frame[i++] = (byte)c565;
                }
            }
        }
        finally
        {
            if (owned)
                src.Dispose();
        }
    }

    /// <summary>
    /// Decodes a native frame back to a logical-orientation bitmap, i.e. exactly what you would see on the panel
    /// (including RGB565 colour loss). Used for previews and tests.
    /// </summary>
    public static SKBitmap FromNative(ReadOnlySpan<byte> frame, Orientation o)
    {
        var bmp = CreateCanvasBitmap(o);
        Span<byte> px = bmp.GetPixelSpan();
        int stride = bmp.RowBytes;
        int i = 0;
        for (int ny = 0; ny < H; ny++)
        {
            for (int nx = 0; nx < W; nx++)
            {
                int c = (frame[i] << 8) | frame[i + 1];
                i += 2;
                var (lx, ly) = NativeToLogical(o, nx, ny);
                int d = ly * stride + lx * 4;
                int r = (c >> 11) & 0x1F, g = (c >> 5) & 0x3F, b = c & 0x1F;
                px[d] = (byte)((r << 3) | (r >> 2));
                px[d + 1] = (byte)((g << 2) | (g >> 4));
                px[d + 2] = (byte)((b << 3) | (b >> 2));
                px[d + 3] = 0xFF;
            }
        }
        return bmp;
    }
}
