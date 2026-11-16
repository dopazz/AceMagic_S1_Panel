using S1Panel.Core.Rendering.Layouts;
using S1Panel.Core.Services;
using SkiaSharp;

namespace S1Panel.Core.Rendering.Widgets;

/// <summary>
/// Top: the day's sun arc from sunrise to sunset, with the sun's current position and the times.
/// Bottom: today's moon, drawn at its real phase, with name, illumination and days to the next full/new moon.
/// Computed locally (<see cref="Astronomy"/>); the location only needs resolving once.
/// </summary>
public sealed class SunMoonWidget : IWidget, IPendingWork
{
    private static readonly SKColor Day = new(0xFF, 0xC8, 0x3D);
    private static readonly SKColor MoonLit = new(0xEE, 0xF1, 0xF8);
    private static readonly SKColor MoonDark = new(0x2A, 0x31, 0x42);

    public string Name => "sun-moon";
    public string Description => "Sunrise, sunset, daylight and the moon phase";
    public bool Pending { get; private set; }

    public void Draw(SKCanvas c, SKRect area, LayoutContext ctx)
    {
        var env = ctx.Env ?? WidgetEnvironment.Default;
        var now = ctx.Stats.Time;
        float W = area.Width, H = area.Height;

        var location = LocationService.Get(env.Location);
        Pending = location.Status == LocationStatus.Resolving;
        if (location.Location is { } loc)
            SunArc(c, area, ctx, Astronomy.Sun(loc.Latitude, loc.Longitude, DateOnly.FromDateTime(now)), now);
        else
            WeatherWidget.Message(c, SKRect.Create(area.Left, area.Top, W, H * 0.55f),
                location.Status == LocationStatus.Resolving ? "Finding your location…" : "Sunrise/sunset need\na location (Settings)");

        MoonBlock(c, area, Astronomy.Moon(now.ToUniversalTime()), southern: location.Location?.Latitude < 0);
    }

    private static void SunArc(SKCanvas c, SKRect area, LayoutContext ctx, SunDay sun, DateTime now)
    {
        float W = area.Width, H = area.Height;
        var center = new SKPoint(area.MidX, area.Top + H * 0.43f);
        float r = Math.Min(W * 0.36f, H * 0.32f);
        var oval = new SKRect(center.X - r, center.Y - r, center.X + r, center.Y + r);

        using var track = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = Gfx.Track, PathEffect = SKPathEffect.CreateDash([4, 4], 0) };
        c.DrawArc(oval, 180, 180, false, track);
        using (var horizon = new SKPaint { Color = Gfx.CardEdge, StrokeWidth = 1 })
            c.DrawLine(area.Left + 8, center.Y, area.Right - 8, center.Y, horizon);

        if (sun.Sunrise is { } rise && sun.Sunset is { } set)
        {
            double f = (now - rise).TotalSeconds / (set - rise).TotalSeconds;
            if (f is > 0 and < 1)
            {
                using var done = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, Color = Day.WithAlpha(200), StrokeCap = SKStrokeCap.Round };
                c.DrawArc(oval, 180, (float)(180 * f), false, done);
                double a = Math.PI * (1 + f);
                WeatherIcons.SunGlyph(c, new SKPoint(center.X + (float)Math.Cos(a) * r, center.Y + (float)Math.Sin(a) * r), 7, rays: false);
                var left = set - now;
                Gfx.TextCentered(c, $"{Hm(left)} of light left", center.X, center.Y - r * 0.18f, 9, Gfx.Regular, Gfx.Muted, r * 1.25f);
            }
            else
            {
                var nextRise = f >= 1 ? rise.AddDays(1) : rise; // close enough for "in Xh"
                Gfx.TextCentered(c, $"Sunrise in {Hm(nextRise - now)}", center.X, center.Y - r * 0.18f, 9, Gfx.Regular, Gfx.Muted, r * 1.25f);
            }
            float ty = center.Y + 15;
            Gfx.TextCentered(c, "↑ " + Time(rise, ctx), area.Left + W * 0.25f, ty, 11, Gfx.SemiBold, Gfx.Text, W * 0.48f);
            Gfx.TextCentered(c, "↓ " + Time(set, ctx), area.Left + W * 0.75f, ty, 11, Gfx.SemiBold, Gfx.Text, W * 0.48f);
            if (sun.DayLength is { } len)
                Gfx.TextCentered(c, $"Day {Hm(len)}", center.X, center.Y - r * 0.5f, 10, Gfx.SemiBold, Day, r * 1.3f);
        }
        else
        {
            Gfx.TextCentered(c, sun.AlwaysUp ? "Sun up all day" : "Sun down all day", center.X, center.Y - r * 0.4f, 11, Gfx.Regular, Gfx.Text);
        }
    }

    private static void MoonBlock(SKCanvas c, SKRect area, MoonInfo moon, bool southern)
    {
        float W = area.Width, H = area.Height;
        float r = Math.Min(W, H) * 0.11f;
        var center = new SKPoint(area.Left + W * 0.22f, area.Top + H * 0.8f);
        DrawMoon(c, center, r, moon, southern);

        float tx = area.Left + W * 0.4f;
        float maxW = area.Right - tx - 6;
        Gfx.TextAt(c, moon.Name, tx, center.Y - r * 0.45f, 11.5f, Gfx.SemiBold, Gfx.Text, SKTextAlign.Left, maxW);
        Gfx.TextAt(c, $"{moon.Illumination * 100:0}% lit", tx, center.Y + r * 0.35f, 10, Gfx.Regular, Gfx.Muted, SKTextAlign.Left, maxW);
        double toFull = (moon.NextFullUtc - DateTime.UtcNow).TotalDays, toNew = (moon.NextNewUtc - DateTime.UtcNow).TotalDays;
        string next = toFull < toNew ? $"Full in {Days(toFull)}" : $"New in {Days(toNew)}";
        Gfx.TextAt(c, next, tx, center.Y + r * 1.15f, 10, Gfx.Regular, Gfx.Muted, SKTextAlign.Left, maxW);
    }

    /// <summary>Dark disc + lit half + terminator ellipse. Waxing is lit on the right (northern hemisphere).</summary>
    internal static void DrawMoon(SKCanvas c, SKPoint center, float r, MoonInfo moon, bool southern)
    {
        using var p = new SKPaint { IsAntialias = true, Color = MoonDark };
        c.DrawCircle(center, r, p);

        c.Save();
        if (southern)
            c.Scale(-1, 1, center.X, center.Y);
        // lit half: right half when waxing, left when waning
        c.Save();
        c.ClipRect(moon.Waxing ? new SKRect(center.X, center.Y - r, center.X + r, center.Y + r) : new SKRect(center.X - r, center.Y - r, center.X, center.Y + r));
        p.Color = MoonLit;
        c.DrawCircle(center, r, p);
        c.Restore();
        // terminator: an ellipse of width r*|cos| — dark for crescents, lit for gibbous
        float k = (float)Math.Abs(Math.Cos(2 * Math.PI * moon.Phase));
        p.Color = moon.Illumination < 0.5 ? MoonDark : MoonLit;
        c.DrawOval(new SKRect(center.X - r * k, center.Y - r, center.X + r * k, center.Y + r), p);
        c.Restore();

        using var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = MoonLit.WithAlpha(70) };
        c.DrawCircle(center, r, rim);
    }

    private static string Time(DateTime t, LayoutContext ctx) =>
        ctx.Clock.H12 ? t.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture) : t.ToString("HH:mm");

    private static string Hm(TimeSpan t) => t.TotalMinutes < 60 ? $"{Math.Max(0, (int)t.TotalMinutes)}m" : $"{(int)t.TotalHours}h {t.Minutes:00}m";

    private static string Days(double d) => d < 1 ? "<1 day" : $"{Math.Round(d):0} day{(Math.Round(d) == 1 ? "" : "s")}";
}
