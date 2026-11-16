using SkiaSharp;

namespace S1Panel.Core.Rendering.Layouts;

/// <summary>Shared drawing helpers and palette for the layouts.</summary>
/// <summary>How to draw the time: 12/24-hour, and whether the colon is in its "off" blink phase.</summary>
public readonly record struct ClockStyle(bool H12 = false, bool ColonHidden = false);

internal static class Gfx
{
    public static readonly SKTypeface Light = Face(SKFontStyleWeight.Light);
    public static readonly SKTypeface Regular = Face(SKFontStyleWeight.Normal);
    public static readonly SKTypeface SemiBold = Face(SKFontStyleWeight.SemiBold);

    public static readonly SKColor BgTop = new(0x1B, 0x22, 0x30);
    public static readonly SKColor BgBottom = new(0x10, 0x14, 0x1D);
    public static readonly SKColor Card = new(0x22, 0x2A, 0x3A);
    public static readonly SKColor CardEdge = new(0x33, 0x3D, 0x52);
    public static readonly SKColor Track = new(0x2C, 0x35, 0x48);
    public static readonly SKColor Text = new(0xF2, 0xF5, 0xFA);
    public static readonly SKColor Muted = new(0x93, 0x9E, 0xB4);

    [ThreadStatic] private static float? _textScale;

    /// <summary>Current text size multiplier (set per frame by <see cref="LayoutExtensions.Render"/>).</summary>
    public static float TextScale
    {
        get => _textScale ?? 1f;
        set => _textScale = value;
    }

    /// <summary>
    /// Applies <see cref="TextScale"/> to a design font size. Small text (labels, units, dates, up to 14 px) gets the
    /// full increase; the effect tapers off to nothing at 32 px, so big digits keep their size and still fit.
    /// </summary>
    public static float Scaled(float size)
    {
        float k = TextScale;
        if (k == 1f) return size;
        float taper = Math.Clamp((32f - size) / (32f - 14f), 0f, 1f);
        return size * (1 + (k - 1) * taper);
    }

    public static SKFont Font(SKTypeface face, float size) =>
        new(face, Scaled(size)) { Subpixel = true, Edging = SKFontEdging.Antialias };

    private static SKTypeface Face(SKFontStyleWeight weight) =>
        SKTypeface.FromFamilyName("Segoe UI", new SKFontStyle(weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright))
        ?? SKTypeface.Default;

    public static void Background(SKCanvas c, int w, int h)
    {
        using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h), [BgTop, BgBottom], SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = shader };
        c.DrawRect(0, 0, w, h, p);
    }

    /// <summary>A rounded card with a soft vertical gradient and a 1px edge.</summary>
    public static void CardRect(SKCanvas c, SKRect r, float radius = 8)
    {
        using var shader = SKShader.CreateLinearGradient(new SKPoint(0, r.Top), new SKPoint(0, r.Bottom),
            [Card, Card.WithAlpha(200)], SKShaderTileMode.Clamp);
        using var fill = new SKPaint { Shader = shader, IsAntialias = true };
        c.DrawRoundRect(r, radius, radius, fill);
        using var edge = new SKPaint { Color = CardEdge, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        c.DrawRoundRect(Inset(r, 0.5f), radius, radius, edge);
    }

    /// <summary>Arc gauge: grey track plus coloured value arc (with a faint glow). Angles in degrees, 0 = 3 o'clock, clockwise.</summary>
    public static void Arc(SKCanvas c, SKPoint center, float radius, float thickness, float start, float sweep, double fraction, SKColor color, bool glow = true)
    {
        var oval = new SKRect(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius);
        using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = thickness, StrokeCap = SKStrokeCap.Round, Color = Track };
        c.DrawArc(oval, start, sweep, false, p);

        float value = (float)(sweep * Math.Clamp(fraction, 0, 1));
        if (Math.Abs(value) < 0.5f)
            return;
        if (glow)
        {
            using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, thickness * 0.6f);
            p.MaskFilter = blur;
            p.Color = color.WithAlpha(110);
            c.DrawArc(oval, start, value, false, p);
            p.MaskFilter = null;
        }
        p.Color = color;
        c.DrawArc(oval, start, value, false, p);
    }

    /// <summary>Draws text centred on (x, baselineY), shrinking it so it fits within maxWidth.</summary>
    public static void TextCentered(SKCanvas c, string text, float x, float baselineY, float size, SKTypeface face, SKColor color, float maxWidth = float.MaxValue)
    {
        using var font = Font(face, size);
        using var p = new SKPaint { Color = color, IsAntialias = true };
        float w = font.MeasureText(text, p);
        if (w > maxWidth)
            font.Size = font.Size * maxWidth / w;
        c.DrawText(text, x, baselineY, SKTextAlign.Center, font, p);
    }

    public static void TextAt(SKCanvas c, string text, float x, float baselineY, float size, SKTypeface face, SKColor color, SKTextAlign align,
        float maxWidth = float.MaxValue)
    {
        using var font = Font(face, size);
        using var p = new SKPaint { Color = color, IsAntialias = true };
        float w = font.MeasureText(text, p);
        if (w > maxWidth)
            font.Size = font.Size * maxWidth / w;
        c.DrawText(text, x, baselineY, align, font, p);
    }

    /// <summary>A number with a small unit after it (e.g. "118" + "°F"), centred and fitted as one unit.</summary>
    public static void ValueUnitCentered(SKCanvas c, string value, string unit, float cx, float baselineY, float size, float unitSize,
        SKColor valueColor, SKColor unitColor, float maxWidth)
    {
        using var vf = Font(SemiBold, size);
        using var uf = Font(Regular, unitSize);
        float gap = vf.Size * 0.06f;
        float vw = vf.MeasureText(value), uw = uf.MeasureText(unit);
        float total = vw + gap + uw;
        if (total > maxWidth)
        {
            float k = maxWidth / total;
            vf.Size *= k; uf.Size *= k; gap *= k; vw *= k; total = maxWidth;
        }
        float x = cx - total / 2;
        using var p = new SKPaint { Color = valueColor, IsAntialias = true };
        c.DrawText(value, x, baselineY, SKTextAlign.Left, vf, p);
        p.Color = unitColor;
        c.DrawText(unit, x + vw + gap, baselineY, SKTextAlign.Left, uf, p);
    }

    /// <summary>Text centred in a box both ways (uses cap height for vertical centring).</summary>
    public static void TextInBox(SKCanvas c, string text, SKRect box, float size, SKTypeface face, SKColor color)
    {
        using var font = Font(face, size);
        using var p = new SKPaint { Color = color, IsAntialias = true };
        float w = font.MeasureText(text, p);
        if (w > box.Width)
            font.Size = font.Size * box.Width / w;
        float capHeight = font.Size * 0.7f;
        c.DrawText(text, box.MidX, box.MidY + capHeight / 2, SKTextAlign.Center, font, p);
    }

    public static void Dot(SKCanvas c, float x, float y, float r, SKColor color)
    {
        using var p = new SKPaint { Color = color, IsAntialias = true };
        using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, r);
        p.MaskFilter = blur;
        p.Color = color.WithAlpha(140);
        c.DrawCircle(x, y, r * 1.6f, p);
        p.MaskFilter = null;
        p.Color = color;
        c.DrawCircle(x, y, r, p);
    }

    public static SKRect Inset(SKRect r, float d) => new(r.Left + d, r.Top + d, r.Right - d, r.Bottom - d);

    /// <summary>"13:05" (24 h) or ("1:05", "PM") (12 h). AM/PM is always English, as the panel shows it.</summary>
    public static (string Main, string? Suffix) TimeParts(DateTime t, bool clock12) =>
        clock12 ? (t.ToString("h:mm"), t.ToString("tt", System.Globalization.CultureInfo.InvariantCulture)) : (t.ToString("HH:mm"), null);

    /// <summary>
    /// Draws the time centred on <paramref name="cx"/>. In 12-hour mode a small AM/PM follows the digits on the
    /// same baseline, and the pair is centred as a unit. Shrinks to fit <paramref name="maxWidth"/>.
    /// </summary>
    public static void TimeCentered(SKCanvas c, DateTime t, ClockStyle clock, float cx, float baseline, float size, SKTypeface face,
        SKColor color, float maxWidth = float.MaxValue, bool shadow = false)
    {
        var (main, suffix) = TimeParts(t, clock.H12);
        using var mainFont = Font(face, size);
        using var sufFont = Font(SemiBold, size * 0.3f);
        float gap = mainFont.Size * 0.07f;
        float total = mainFont.MeasureText(main) + (suffix is null ? 0 : gap + sufFont.MeasureText(suffix));
        if (total > maxWidth)
        {
            float k = maxWidth / total;
            mainFont.Size *= k; sufFont.Size *= k; gap *= k; total = maxWidth;
        }

        float x = cx - total / 2;
        float sx = x + mainFont.MeasureText(main) + gap;

        // Colon "off" phase: draw hours and minutes exactly where they sit with the colon, so nothing shifts.
        int colon = main.IndexOf(':');
        void DrawMain(SKPaint paint, float dx, float dy)
        {
            if (!clock.ColonHidden || colon < 0)
            {
                c.DrawText(main, x + dx, baseline + dy, SKTextAlign.Left, mainFont, paint);
                return;
            }
            c.DrawText(main[..colon], x + dx, baseline + dy, SKTextAlign.Left, mainFont, paint);
            c.DrawText(main[(colon + 1)..], x + dx + mainFont.MeasureText(main[..(colon + 1)]), baseline + dy, SKTextAlign.Left, mainFont, paint);
        }

        if (shadow)
        {
            using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, mainFont.Size / 14);
            using var sp = new SKPaint { Color = new SKColor(0, 0, 0, 150), IsAntialias = true, MaskFilter = blur };
            DrawMain(sp, 1, 2);
            if (suffix is not null)
                c.DrawText(suffix, sx + 1, baseline + 2, SKTextAlign.Left, sufFont, sp);
        }
        using var p = new SKPaint { Color = color, IsAntialias = true };
        DrawMain(p, 0, 0);
        if (suffix is not null)
            c.DrawText(suffix, sx, baseline, SKTextAlign.Left, sufFont, p);
    }
    public static string Date(DateTime t) => t.ToString("ddd d MMM");
}
