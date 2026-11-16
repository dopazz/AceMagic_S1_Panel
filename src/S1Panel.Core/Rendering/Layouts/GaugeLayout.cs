using SkiaSharp;

namespace S1Panel.Core.Rendering.Layouts;

/// <summary>
/// A large central CPU-load gauge with the time inside it, and four readings at the sides
/// (stock app's "TDP" layout arrangement, drawn from scratch).
/// </summary>
public sealed class GaugeLayout : ILayout
{
    public string Name => "gauge";
    public string Description => "Large central CPU gauge with clock, four readings at the sides";

    public void Draw(SKCanvas c, LayoutContext ctx)
    {
        var s = ctx.Stats;
        int w = ctx.Width, h = ctx.Height;
        Gfx.Background(c, w, h);

        Metric[] side = [Metrics.Temp(s, ctx.Fahrenheit), Metrics.DiskUsed(s), Metrics.NetDown(s), Metrics.MemUsage(s)];
        var center = ctx.Portrait ? new SKPoint(w / 2f, h / 2f) : new SKPoint(w / 2f, h / 2f + 1);
        float r = ctx.Portrait ? 72 : 76;

        Brackets(c, center, r, ctx.Portrait);
        BigGauge(c, center, r, Metrics.CpuLoad(s), s.Time, ctx.Clock);

        if (!ctx.Portrait)
        {
            // left column: [0] top, [2] bottom; right column: [1] top, [3] bottom
            Reading(c, 42, 52, side[0]);
            Reading(c, 42, 128, side[2]);
            Reading(c, w - 42, 52, side[1]);
            Reading(c, w - 42, 128, side[3]);
        }
        else
        {
            Reading(c, 44, 36, side[0]);
            Reading(c, w - 44, 36, side[1]);
            Reading(c, 44, h - 26, side[2]);
            Reading(c, w - 44, h - 26, side[3]);
        }
    }

    private static void BigGauge(SKCanvas c, SKPoint center, float r, Metric m, DateTime t, ClockStyle clock)
    {
        // Disc with a soft rim
        using (var shader = SKShader.CreateRadialGradient(new SKPoint(center.X, center.Y - r * 0.5f), r * 1.5f,
                   [new SKColor(0x2B, 0x34, 0x47), new SKColor(0x12, 0x17, 0x21)], SKShaderTileMode.Clamp))
        using (var disc = new SKPaint { Shader = shader, IsAntialias = true })
            c.DrawCircle(center, r - 2, disc);
        using (var rim = new SKPaint { Color = Gfx.CardEdge, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
            c.DrawCircle(center, r - 2, rim);

        Gfx.Arc(c, center, r - 10, 7, 135, 270, m.Fraction, Metrics.Cyan);

        float cx = center.X, cy = center.Y;
        Gfx.TextCentered(c, m.Label, cx, cy - 26, 11, Gfx.Regular, Gfx.Muted);
        Gfx.TextCentered(c, m.ValueWithUnit, cx, cy + 6, 32, Gfx.SemiBold, Gfx.Text, r * 1.2f);
        using (var line = new SKPaint { Color = Gfx.CardEdge, IsAntialias = true, StrokeWidth = 1 })
            c.DrawLine(cx - r * 0.45f, cy + 15, cx + r * 0.45f, cy + 15, line);
        Gfx.TimeCentered(c, t, clock, cx, cy + 35, 18, Gfx.Regular, Gfx.Text, r * 1.1f);
        Gfx.TextCentered(c, Gfx.Date(t), cx, cy + 48, 9, Gfx.Regular, Gfx.Muted);
    }

    /// <summary>Faint concentric arcs framing the gauge, like the stock design's side flourishes.</summary>
    private static void Brackets(SKCanvas c, SKPoint center, float r, bool portrait)
    {
        using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = Gfx.CardEdge };
        foreach (float extra in new[] { 8f, 14f })
        {
            float rr = r + extra;
            var oval = new SKRect(center.X - rr, center.Y - rr, center.X + rr, center.Y + rr);
            if (portrait)
            {
                c.DrawArc(oval, 230, 80, false, p);
                c.DrawArc(oval, 50, 80, false, p);
            }
            else
            {
                c.DrawArc(oval, 140, 80, false, p);
                c.DrawArc(oval, -40, 80, false, p);
            }
            p.Color = Gfx.CardEdge.WithAlpha(120);
        }
    }

    private static void Reading(SKCanvas c, float cx, float valueBaseline, Metric m)
    {
        Gfx.TextCentered(c, m.Label, cx, valueBaseline - 22, 10, Gfx.Regular, Gfx.Muted);
        Gfx.TextCentered(c, m.ValueWithUnit, cx, valueBaseline, 20, Gfx.SemiBold, Gfx.Text, 76);
        using var p = new SKPaint { Color = m.Color, IsAntialias = true, StrokeWidth = 2, StrokeCap = SKStrokeCap.Round };
        c.DrawLine(cx - 14, valueBaseline + 8, cx + 14, valueBaseline + 8, p);
    }
}
