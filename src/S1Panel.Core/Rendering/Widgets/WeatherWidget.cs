using S1Panel.Core.Rendering.Layouts;
using S1Panel.Core.Services;
using SkiaSharp;

namespace S1Panel.Core.Rendering.Widgets;

/// <summary>
/// Current conditions (icon, temperature, description, today's high/low) and the next three days.
/// Data comes from <see cref="WeatherService"/> in the background; this only draws what's cached.
/// </summary>
public sealed class WeatherWidget : IWidget, IPendingWork
{
    public string Name => "weather";
    public string Description => "Current weather and a 3-day forecast (Open-Meteo)";
    public bool Pending { get; private set; }

    public void Draw(SKCanvas c, SKRect area, LayoutContext ctx)
    {
        var env = ctx.Env ?? WidgetEnvironment.Default;
        var location = LocationService.Get(env.Location);
        Pending = location.Status == LocationStatus.Resolving;
        if (location.Location is not { } loc)
        {
            Message(c, area, location.Status == LocationStatus.Resolving ? "Finding your location…" :
                location.Status == LocationStatus.NeedsInput ? "Location needed:\nenter a ZIP code\nin Settings" : location.Message);
            return;
        }

        var weather = WeatherService.Get(loc);
        Pending = weather is { Data: null, Problem: null };
        if (weather.Data is not { } w)
        {
            Message(c, area, weather.Problem is null ? "Getting the weather…" : $"{weather.Problem}.\nRetrying…");
            return;
        }

        float W = area.Width, H = area.Height;
        string Temp(double celsius) => $"{Math.Round(ctx.Fahrenheit ? celsius * 9 / 5 + 32 : celsius):0}";
        string unit = ctx.Fahrenheit ? "°F" : "°C";

        // Header: place (or "Weather"), plus age if the data is getting old.
        var age = DateTime.UtcNow - w.FetchedUtc;
        string header = loc.Name?.Split(',')[0] ?? "Weather";
        if (age > TimeSpan.FromMinutes(75))
            header += $" · {(int)age.TotalHours}h old";
        Gfx.TextCentered(c, header, area.MidX, area.Top + H * 0.11f, 11, Gfx.Regular, Gfx.Muted, W - 12);

        // Current: icon left, temperature right.
        var (kind, text) = WeatherService.Describe(w.Code);
        float iconSize = Math.Min(W, H) * 0.42f;
        WeatherIcons.Draw(c, kind, w.IsDay, new SKPoint(area.Left + W * 0.28f, area.Top + H * 0.34f), iconSize);
        Gfx.ValueUnitCentered(c, Temp(w.TempC), unit, area.Left + W * 0.7f, area.Top + H * 0.42f, 34, 12, Gfx.Text, Gfx.Muted, W * 0.48f);

        var today = w.Days.Count > 0 ? w.Days[0] : null;
        string line = today is null ? text : $"{text} · {Temp(today.MaxC)}°/{Temp(today.MinC)}°";
        Gfx.TextCentered(c, line, area.MidX, area.Top + H * 0.6f, 11.5f, Gfx.Regular, Gfx.Text, W - 10);

        // Next three days.
        var next = w.Days.Skip(1).Take(3).ToList();
        if (next.Count == 0)
            return;
        float colW = (W - 8) / next.Count, top = area.Top + H * 0.68f;
        using (var sep = new SKPaint { Color = Gfx.CardEdge, StrokeWidth = 1 })
            c.DrawLine(area.Left + 12, top - 2, area.Right - 12, top - 2, sep);
        for (int i = 0; i < next.Count; i++)
        {
            var d = next[i];
            float cx = area.Left + 4 + colW * (i + 0.5f);
            Gfx.TextCentered(c, d.Date.ToString("ddd"), cx, top + H * 0.07f, 10, Gfx.SemiBold, Gfx.Muted, colW - 4);
            WeatherIcons.Draw(c, WeatherService.Describe(d.Code).Kind, true, new SKPoint(cx, top + H * 0.15f), Math.Min(W, H) * 0.17f);
            Gfx.TextCentered(c, $"{Temp(d.MaxC)}°/{Temp(d.MinC)}°", cx, top + H * 0.27f, 10, Gfx.Regular, Gfx.Text, colW - 2);
        }
    }

    internal static void Message(SKCanvas c, SKRect area, string text)
    {
        string[] lines = text.Split('\n');
        float y = area.MidY - (lines.Length - 1) * 8;
        foreach (string line in lines)
        {
            Gfx.TextCentered(c, line, area.MidX, y + 4, 12, Gfx.Regular, Gfx.Muted, area.Width - 14);
            y += 16;
        }
    }
}
