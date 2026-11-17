using S1Panel.Core.Rendering.Layouts;
using SkiaSharp;

namespace S1Panel.Core.Rendering.Widgets;

/// <summary>
/// A little robot whose mood follows the CPU: relaxed and looking around when idle, focused when busy, steaming
/// when flat out, asleep late at night. It blinks now and then and waves for the first minute of every hour.
/// Animation is driven by the frame time, so it's deterministic and only the robot's face/arms change per second.
/// </summary>
public sealed class RobotWidget : IWidget
{
    private static readonly SKColor Metal = new(0x6F, 0x80, 0x9E);
    private static readonly SKColor MetalDark = new(0x48, 0x55, 0x6E);
    private static readonly SKColor Screen = new(0x0C, 0x12, 0x1E);
    private static readonly SKColor Eye = new(0x2E, 0xD3, 0xF5);

    public string Name => "robot";
    public string Description => "An animated robot buddy whose mood follows the CPU load";

    public enum Mood { Sleeping, Relaxed, Working, Busy, FlatOut }

    internal static Mood MoodFor(double cpu, int hour) =>
        hour < 6 && cpu < 30 ? Mood.Sleeping :
        cpu < 15 ? Mood.Relaxed :
        cpu < 50 ? Mood.Working :
        cpu < 85 ? Mood.Busy : Mood.FlatOut;

    public void Draw(SKCanvas c, SKRect area, LayoutContext ctx)
    {
        var t = ctx.Stats.Time;
        long sec = t.Ticks / TimeSpan.TicksPerSecond;
        double cpu = ctx.Stats.CpuPercent;
        var mood = MoodFor(cpu, t.Hour);
        bool waving = t.Minute == 0 && mood != Mood.Sleeping;

        float cx = area.MidX;
        float y0 = area.Top + (area.Height - 160) / 2; // design is 160 tall
        SKColor load = cpu < 50 ? Metrics.Green : cpu < 85 ? Metrics.Amber : new SKColor(0xFF, 0x5A, 0x5A);

        using var p = new SKPaint { IsAntialias = true };

        // Antenna with a load-coloured bulb that pulses.
        p.Color = MetalDark;
        p.StrokeWidth = 3;
        c.DrawLine(cx, y0 + 18, cx, y0 + 30, p);
        bool bright = mood == Mood.Sleeping ? sec % 4 == 0 : sec % 2 == 0;
        Gfx.Dot(c, cx, y0 + 14, 5, bright ? load : load.WithAlpha(110));

        // Arms (behind the body).
        Arm(c, cx - 34, y0 + 102, left: true, waving && sec % 2 == 0, waving);
        Arm(c, cx + 34, y0 + 102, left: false, false, false);

        // Head with ear bolts, face screen.
        Shaded(c, new SKRect(cx - 44, y0 + 30, cx + 44, y0 + 88), 12);
        p.Color = MetalDark;
        c.DrawRoundRect(new SKRect(cx - 50, y0 + 48, cx - 44, y0 + 70), 2, 2, p);
        c.DrawRoundRect(new SKRect(cx + 44, y0 + 48, cx + 50, y0 + 70), 2, 2, p);
        var face = new SKRect(cx - 35, y0 + 37, cx + 35, y0 + 81);
        p.Color = Screen;
        c.DrawRoundRect(face, 8, 8, p);

        Eyes(c, cx, y0 + 54, mood, sec);
        Mouth(c, cx, y0 + 71, mood, sec);

        // Neck and body with a 5-segment load meter.
        p.Color = MetalDark;
        c.DrawRect(cx - 8, y0 + 88, 16, 7, p);
        Shaded(c, new SKRect(cx - 34, y0 + 94, cx + 34, y0 + 136), 10);
        var panel = new SKRect(cx - 24, y0 + 103, cx + 24, y0 + 117);
        p.Color = Screen;
        c.DrawRoundRect(panel, 4, 4, p);
        int lit = (int)Math.Ceiling(Math.Clamp(cpu, 0, 100) / 20);
        for (int i = 0; i < 5; i++)
        {
            p.Color = i < lit ? (i < 2 ? Metrics.Green : i < 4 ? Metrics.Amber : new SKColor(0xFF, 0x5A, 0x5A)) : Gfx.Track;
            c.DrawRoundRect(SKRect.Create(panel.Left + 4 + i * 8.4f, panel.Top + 4, 6, 6), 1.5f, 1.5f, p);
        }
        p.Color = MetalDark;
        c.DrawCircle(cx - 12, y0 + 127, 3, p);
        c.DrawCircle(cx, y0 + 127, 3, p);
        c.DrawCircle(cx + 12, y0 + 127, 3, p);

        Extras(c, cx, y0, mood, sec);

        string caption = mood switch
        {
            Mood.Sleeping => "Sleeping",
            Mood.Relaxed => waving ? "Hello!" : "Relaxing",
            Mood.Working => "Working",
            Mood.Busy => "Busy!",
            _ => "Flat out!",
        };
        Gfx.TextCentered(c, $"{caption} · CPU {cpu:0}%", cx, y0 + 154, 11, Gfx.SemiBold, mood == Mood.Sleeping ? Gfx.Muted : load, area.Width - 12);
    }

    private static void Shaded(SKCanvas c, SKRect r, float radius)
    {
        using var shader = SKShader.CreateLinearGradient(new SKPoint(0, r.Top), new SKPoint(0, r.Bottom), [Metal, MetalDark], SKShaderTileMode.Clamp);
        using var p = new SKPaint { IsAntialias = true, Shader = shader };
        c.DrawRoundRect(r, radius, radius, p);
        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = new SKColor(0x9A, 0xA9, 0xC4, 120) };
        c.DrawRoundRect(Gfx.Inset(r, 0.5f), radius, radius, edge);
    }

    /// <summary>Shoulder at (x, y). Waving raises the left arm, alternating between two angles.</summary>
    private static void Arm(SKCanvas c, float x, float y, bool left, bool waveUp, bool waving)
    {
        float dir = left ? -1 : 1;
        var hand = waving ? new SKPoint(x + dir * (waveUp ? 22 : 14), y - (waveUp ? 30 : 34)) : new SKPoint(x + dir * 14, y + 26);
        using var p = new SKPaint { IsAntialias = true, Color = MetalDark, StrokeWidth = 6, StrokeCap = SKStrokeCap.Round };
        c.DrawLine(x, y, hand.X, hand.Y, p);
        p.Color = Metal;
        c.DrawCircle(hand, 5.5f, p);
    }

    private static void Eyes(SKCanvas c, float cx, float y, Mood mood, long sec)
    {
        using var p = new SKPaint { IsAntialias = true, Color = Eye };
        if (mood == Mood.Sleeping)
        {
            p.Style = SKPaintStyle.Stroke;
            p.StrokeWidth = 2.5f;
            p.StrokeCap = SKStrokeCap.Round;
            foreach (float ex in new[] { cx - 15, cx + 15 })
                c.DrawArc(new SKRect(ex - 7, y - 6, ex + 7, y + 4), 20, 140, false, p);
            return;
        }
        if (mood == Mood.FlatOut)
        {
            // > <  squeezed eyes
            p.Style = SKPaintStyle.Stroke;
            p.StrokeWidth = 3;
            p.StrokeCap = SKStrokeCap.Round;
            c.DrawLine(cx - 21, y - 5, cx - 11, y, p); c.DrawLine(cx - 11, y, cx - 21, y + 5, p);
            c.DrawLine(cx + 21, y - 5, cx + 11, y, p); c.DrawLine(cx + 11, y, cx + 21, y + 5, p);
            return;
        }

        // Look around every few seconds when relaxed; straight ahead when working.
        uint h = Hash(sec / 3);
        float dx = mood == Mood.Relaxed ? (int)(h % 3) * 4 - 4 : 0;
        float dy = mood == Mood.Relaxed && h % 7 == 0 ? -3 : 0;
        bool blink = Hash(sec) % 9 == 0;
        float eh = blink ? 3 : mood == Mood.Busy ? 10 : 14;

        using var glow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3);
        foreach (float ex in new[] { cx - 15 + dx, cx + 15 + dx })
        {
            var r = SKRect.Create(ex - 6, y + dy - eh / 2, 12, eh);
            p.MaskFilter = glow;
            p.Color = Eye.WithAlpha(110);
            c.DrawRoundRect(r, 3, 3, p);
            p.MaskFilter = null;
            p.Color = Eye;
            c.DrawRoundRect(r, 3, 3, p);
        }
    }

    private static void Mouth(SKCanvas c, float cx, float y, Mood mood, long sec)
    {
        using var p = new SKPaint { IsAntialias = true, Color = Eye, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, StrokeCap = SKStrokeCap.Round };
        switch (mood)
        {
            case Mood.Sleeping:
                p.Style = SKPaintStyle.Fill;
                c.DrawCircle(cx, y + 1, sec % 2 == 0 ? 2.5f : 3.5f, p);
                break;
            case Mood.Relaxed:
                c.DrawArc(new SKRect(cx - 10, y - 8, cx + 10, y + 4), 20, 140, false, p);
                break;
            case Mood.Working:
                c.DrawLine(cx - 8, y, cx + 8, y, p);
                break;
            default:
                // zigzag grimace, wiggling each second
                using (var b = new SKPathBuilder())
                {
                    float off = sec % 2 == 0 ? 0 : 2;
                    b.MoveTo(cx - 12, y + off);
                    for (int i = 1; i <= 6; i++)
                        b.LineTo(cx - 12 + i * 4, y + ((i % 2 == 0) ? off : 3 - off));
                    using var path = b.Detach();
                    c.DrawPath(path, p);
                }
                break;
        }
    }

    /// <summary>Zzz when asleep, a sweat drop when busy, steam puffs when flat out.</summary>
    private static void Extras(SKCanvas c, float cx, float y0, Mood mood, long sec)
    {
        switch (mood)
        {
            case Mood.Sleeping:
            {
                int step = (int)(sec % 3);
                for (int i = 0; i <= step; i++)
                    Gfx.TextAt(c, "z", cx + 46 + i * 9, y0 + 34 - i * 10, 10 + i * 3, Gfx.SemiBold, Gfx.Muted.WithAlpha((byte)(230 - i * 40)), SKTextAlign.Left);
                break;
            }
            case Mood.Busy:
            {
                using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0x8F, 0xD3, 0xFF) };
                float dy = sec % 2 == 0 ? 0 : 4;
                using var b = new SKPathBuilder();
                b.MoveTo(cx + 40, y0 + 34 + dy);
                b.QuadTo(cx + 46, y0 + 44 + dy, cx + 40, y0 + 46 + dy);
                b.QuadTo(cx + 34, y0 + 44 + dy, cx + 40, y0 + 34 + dy);
                using var drop = b.Detach();
                c.DrawPath(drop, p);
                break;
            }
            case Mood.FlatOut:
            {
                using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0xDD, 0xE4, 0xF0, 150) };
                bool phase = sec % 2 == 0;
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = cx + side * 54, y = y0 + 52;
                    c.DrawCircle(x + side * (phase ? 2 : 6), y - (phase ? 6 : 14), phase ? 5 : 7, p);
                    c.DrawCircle(x + side * (phase ? 8 : 12), y - (phase ? 16 : 26), phase ? 4 : 5, p);
                }
                break;
            }
        }
    }

    internal static uint Hash(long n)
    {
        ulong x = (ulong)n * 0x9E3779B97F4A7C15UL;
        x ^= x >> 31;
        x *= 0xBF58476D1CE4E5B9UL;
        return (uint)(x >> 32);
    }
}
