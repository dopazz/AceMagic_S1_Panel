using S1Panel.Core.Sensors;
using SkiaSharp;

namespace S1Panel.Core.Rendering;

/// <summary>Built-in things to draw. Each method paints a whole logical-orientation canvas.</summary>
public static class Scenes
{
    private static readonly SKTypeface Regular = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
    private static readonly SKTypeface Bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

    public static readonly SKColor Background = new(0x0B, 0x0E, 0x14);
    private static readonly SKColor Track = new(0x2A, 0x30, 0x3C);
    private static readonly SKColor Dim = new(0x8A, 0x93, 0xA6);
    private static readonly SKColor Fg = new(0xEE, 0xF1, 0xF6);
    private static readonly SKColor CpuColor = new(0x4C, 0xC2, 0xFF);
    private static readonly SKColor MemColor = new(0xB3, 0x88, 0xFF);
    private static readonly SKColor DiskColor = new(0xFF, 0xB0, 0x4C);
    private static readonly SKColor NetColor = new(0x5C, 0xE0, 0x8C);

    /// <summary>Colour bars, gradients and an arrow pointing at the top edge, for checking orientation and colour.</summary>
    public static void TestPattern(SKCanvas c, int w, int h)
    {
        c.Clear(SKColors.Black);
        SKColor[] bars = [SKColors.White, SKColors.Yellow, SKColors.Cyan, SKColors.Lime, SKColors.Magenta, SKColors.Red, SKColors.Blue, SKColors.Black];
        float barH = h * 0.35f, bw = (float)w / bars.Length;
        using var p = new SKPaint { IsAntialias = false };
        for (int i = 0; i < bars.Length; i++)
        {
            p.Color = bars[i];
            c.DrawRect(i * bw, 0, bw + 1, barH, p);
        }

        // RGB ramps show 565 banding.
        SKColor[] ramps = [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.White];
        float rampH = h * 0.08f;
        for (int i = 0; i < ramps.Length; i++)
        {
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, 0), [SKColors.Black, ramps[i]], SKShaderTileMode.Clamp);
            p.Shader = shader;
            c.DrawRect(0, barH + i * rampH, w, rampH, p);
        }
        p.Shader = null;

        float top = barH + ramps.Length * rampH;
        using var font = new SKFont(Bold, Math.Min(w, h) * 0.11f);
        using var text = new SKPaint { Color = Fg, IsAntialias = true };
        c.DrawText($"{w}x{h}  TOP ^", w / 2f, top + (h - top) / 2f + font.Size / 3, SKTextAlign.Center, font, text);

        using var frame = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        c.DrawRect(0.5f, 0.5f, w - 1, h - 1, frame);
    }

    /// <summary>Draws an image scaled to fill (cover) or fit (contain) the canvas.</summary>
    public static void Image(SKCanvas c, int w, int h, SKBitmap img, bool cover)
    {
        c.Clear(SKColors.Black);
        float scale = cover ? Math.Max((float)w / img.Width, (float)h / img.Height) : Math.Min((float)w / img.Width, (float)h / img.Height);
        float dw = img.Width * scale, dh = img.Height * scale;
        var dest = SKRect.Create((w - dw) / 2, (h - dh) / 2, dw, dh);
        using var image = SKImage.FromBitmap(img);
        c.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    /// <summary>Centered text, auto-shrunk to fit. Use \n for line breaks.</summary>
    public static void Text(SKCanvas c, int w, int h, string message, SKColor fg, SKColor bg, float maxSize = 64)
    {
        c.Clear(bg);
        string[] lines = message.Replace("\\n", "\n").Split('\n');
        using var font = new SKFont(Bold, maxSize);
        using var paint = new SKPaint { Color = fg, IsAntialias = true };
        float widest = lines.Max(l => font.MeasureText(l, paint));
        float size = maxSize;
        if (widest > w * 0.92f)
            size = maxSize * (w * 0.92f / widest);
        size = Math.Min(size, h * 0.9f / lines.Length / 1.2f);
        font.Size = size;

        float lineH = size * 1.2f;
        float y = (h - lineH * lines.Length) / 2 + size;
        foreach (string line in lines)
        {
            c.DrawText(line, w / 2f, y, SKTextAlign.Center, font, paint);
            y += lineH;
        }
    }

    /// <summary>Clock + CPU / memory / disk / network dashboard. Lays itself out for portrait or landscape.</summary>
    public static void Dashboard(SKCanvas c, int w, int h, StatsSnapshot s, IReadOnlyList<double> cpuHistory, Layouts.ClockStyle clock = default)
    {
        c.Clear(Background);
        bool portrait = h > w;

        using var paint = new SKPaint { IsAntialias = true };
        using var big = new SKFont(Bold, portrait ? 46 : 52);
        using var small = Layouts.Gfx.Font(Regular, 13);
        using var label = Layouts.Gfx.Font(Bold, 12);

        // Clock block.
        float pad = 8;
        float clockW = portrait ? w : w * 0.44f;
        paint.Color = Fg;
        Layouts.Gfx.TimeCentered(c, s.Time, clock, clockW / 2, portrait ? 50 : 72, big.Size, Bold, Fg, clockW - 10);
        paint.Color = Dim;
        c.DrawText(s.Time.ToString("ddd d MMM"), clockW / 2, portrait ? 70 : 96, SKTextAlign.Center, small, paint);

        // Metric rows.
        float x0 = portrait ? pad : clockW + pad;
        float rowW = (portrait ? w : w - clockW) - pad * 2;
        float y = portrait ? 90 : 14;
        float rowH = portrait ? 38 : 34;

        Bar(c, x0, y, rowW, "CPU", $"{s.CpuPercent:0}%", s.CpuPercent, CpuColor, label, small);
        y += rowH;
        Bar(c, x0, y, rowW, "RAM", $"{s.MemoryUsedBytes / 1073741824.0:0.0} / {s.MemoryTotalBytes / 1073741824.0:0} GB", s.MemoryPercent, MemColor, label, small);
        y += rowH;
        Bar(c, x0, y, rowW, "DISK", $"{s.DiskFreeBytes / 1073741824.0:0} GB free", s.DiskPercent, DiskColor, label, small);
        y += rowH;

        // Network.
        paint.Color = NetColor;
        c.DrawText("NET", x0, y + 10, SKTextAlign.Left, label, paint);
        paint.Color = Fg;
        // the rates get whatever room the label leaves
        Layouts.Gfx.TextAt(c, $"↓ {Rate(s.NetDownBytesPerSec)}   ↑ {Rate(s.NetUpBytesPerSec)}", x0 + rowW, y + 10, 13, Regular, Fg,
            SKTextAlign.Right, rowW - label.MeasureText("NET") - 8);

        // CPU history sparkline fills the remaining space.
        float gx = portrait ? x0 : pad, gw = portrait ? rowW : clockW - pad * 2;
        float gy = portrait ? y + 24 : 112, gh = (portrait ? h - 22 : h - 8) - gy;
        if (gh > 12 && cpuHistory.Count > 1)
            Sparkline(c, SKRect.Create(gx, gy, gw, gh), cpuHistory, CpuColor);

        if (portrait)
        {
            paint.Color = Dim;
            c.DrawText($"up {(int)s.Uptime.TotalDays}d {s.Uptime.Hours}h {s.Uptime.Minutes}m", w / 2f, h - 6, SKTextAlign.Center, small, paint);
        }
    }

    private static void Bar(SKCanvas c, float x, float y, float w, string name, string value, double pct, SKColor color, SKFont label, SKFont small)
    {
        using var p = new SKPaint { IsAntialias = true, Color = color };
        c.DrawText(name, x, y + 10, SKTextAlign.Left, label, p);
        p.Color = Fg;
        c.DrawText(value, x + w, y + 10, SKTextAlign.Right, small, p);

        float by = y + 16, bh = 8;
        p.Color = Track;
        c.DrawRoundRect(x, by, w, bh, 4, 4, p);
        float fw = (float)(w * Math.Clamp(pct, 0, 100) / 100);
        if (fw > 1)
        {
            p.Color = color;
            c.DrawRoundRect(x, by, Math.Max(fw, bh), bh, 4, 4, p);
        }
    }

    private static void Sparkline(SKCanvas c, SKRect r, IReadOnlyList<double> values, SKColor color)
    {
        using var p = new SKPaint { IsAntialias = true, Color = Track, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        c.DrawRoundRect(r, 4, 4, p);

        using var builder = new SKPathBuilder();
        int n = values.Count;
        for (int i = 0; i < n; i++)
        {
            float x = r.Left + 2 + (r.Width - 4) * i / (n - 1);
            float y = r.Bottom - 2 - (float)(Math.Clamp(values[i], 0, 100) / 100 * (r.Height - 4));
            if (i == 0) builder.MoveTo(x, y); else builder.LineTo(x, y);
        }

        using var path = builder.Snapshot();
        builder.LineTo(r.Right - 2, r.Bottom - 2);
        builder.LineTo(r.Left + 2, r.Bottom - 2);
        builder.Close();
        using var fill = builder.Detach();
        p.Style = SKPaintStyle.Fill;
        p.Color = color.WithAlpha(60);
        c.DrawPath(fill, p);

        p.Style = SKPaintStyle.Stroke;
        p.StrokeWidth = 1.5f;
        p.Color = color;
        c.DrawPath(path, p);
    }

    private static string Rate(double bytesPerSec)
    {
        double bits = bytesPerSec * 8;
        return bits switch
        {
            >= 1e9 => $"{bits / 1e9:0.0} Gb/s",
            >= 1e6 => $"{bits / 1e6:0.0} Mb/s",
            >= 1e3 => $"{bits / 1e3:0} kb/s",
            _ => $"{bits:0} b/s",
        };
    }
}
