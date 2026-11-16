using SkiaSharp;

namespace S1Panel.Core.Rendering.Layouts;

/// <summary>
/// Clock face: tick ring with a moving seconds marker, large time and date in the middle (the colon never blinks
/// here: the seconds marker already shows time passing),
/// four readings in the corners, each with a matching coloured arc segment on the ring
/// (stock app's "time" layout arrangement, drawn from scratch).
/// </summary>
public sealed class ClockLayout : ILayout
{
    public string Name => "clock";
    public string Description => "Clock face with seconds ring and four coloured corner readings";

    public void Draw(SKCanvas c, LayoutContext ctx)
    {
        var s = ctx.Stats;
        int w = ctx.Width, h = ctx.Height;
        Gfx.Background(c, w, h);

        var center = new SKPoint(w / 2f, h / 2f);
        float r = ctx.Portrait ? 74 : 76;

        // Corner metrics and their arc positions (Skia angles: 0 = 3 o'clock, clockwise)
        (Metric M, float Angle)[] corners =
        [
            (Metrics.Temp(s, ctx.Fahrenheit), 225),  // top-left
            (Metrics.NetDown(s), 315),  // top-right
            (Metrics.CpuLoad(s), 135),  // bottom-left
            (Metrics.MemUsage(s), 45),   // bottom-right
        ];

        Ticks(c, center, r - 8);
        foreach (var (m, angle) in corners)
            Gfx.Arc(c, center, r, 3, angle - 25, 50, m.Fraction, m.Color);

        // seconds marker
        double a = (s.Time.Second * 6 - 90) * Math.PI / 180;
        Gfx.Dot(c, center.X + (float)Math.Cos(a) * (r - 8), center.Y + (float)Math.Sin(a) * (r - 8), 2.2f, Gfx.Text);

        Gfx.TimeCentered(c, s.Time, ctx.Clock with { ColonHidden = false }, center.X, center.Y + 14, 46, Gfx.Light, Gfx.Text, r * 1.7f);
        Gfx.TextCentered(c, Gfx.Date(s.Time), center.X, center.Y + 34, 12, Gfx.Regular, Gfx.Muted);

        float lx = ctx.Portrait ? 40 : 38, rx = w - lx;
        float top = ctx.Portrait ? 30 : 32, bottom = ctx.Portrait ? h - 22 : h - 32;
        Corner(c, lx, top, corners[0].M);
        Corner(c, rx, top, corners[1].M);
        Corner(c, lx, bottom, corners[2].M);
        Corner(c, rx, bottom, corners[3].M);
    }

    private static void Ticks(SKCanvas c, SKPoint center, float r)
    {
        using var p = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        for (int i = 0; i < 60; i++)
        {
            bool major = i % 5 == 0;
            double a = i * 6 * Math.PI / 180;
            float inner = r - (major ? 6 : 3);
            p.StrokeWidth = major ? 1.5f : 1;
            p.Color = major ? Gfx.Muted.WithAlpha(170) : Gfx.Muted.WithAlpha(70);
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            c.DrawLine(center.X + cos * inner, center.Y + sin * inner, center.X + cos * r, center.Y + sin * r, p);
        }
    }

    private static void Corner(SKCanvas c, float cx, float valueBaseline, Metric m)
    {
        Gfx.TextCentered(c, m.ValueWithUnit, cx, valueBaseline, 19, Gfx.SemiBold, Gfx.Text, 72);
        Gfx.TextCentered(c, m.Label, cx, valueBaseline + 14, 9.5f, Gfx.Regular, m.Color);
    }
}
