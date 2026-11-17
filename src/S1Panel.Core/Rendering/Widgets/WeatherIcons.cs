// S1 Panel - front LCD and LED strip control for the AceMagic S1
// Copyright (C) 2026 dopaz
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. It is distributed WITHOUT ANY WARRANTY; see the
// GNU General Public License in LICENSE for details.
//
// Developed with Claude Code (Claude Opus 5.5).

using S1Panel.Core.Services;
using SkiaSharp;

namespace S1Panel.Core.Rendering.Widgets;

/// <summary>Weather icons drawn in code (sun, moon, clouds, rain, snow, storm, fog), matching the panel's style.</summary>
internal static class WeatherIcons
{
    private static readonly SKColor Sun = new(0xFF, 0xC8, 0x3D);
    private static readonly SKColor MoonColor = new(0xE8, 0xEC, 0xF6);
    private static readonly SKColor CloudLight = new(0xE6, 0xEB, 0xF3);
    private static readonly SKColor CloudDark = new(0x9A, 0xA6, 0xBA);
    private static readonly SKColor RainColor = new(0x4C, 0xB4, 0xFF);
    private static readonly SKColor Bolt = new(0xFF, 0xD2, 0x4C);

    /// <summary>Draws the icon centred on <paramref name="c"/> within a box of <paramref name="size"/> pixels.</summary>
    public static void Draw(SKCanvas canvas, WeatherKind kind, bool isDay, SKPoint c, float size)
    {
        float s = size / 64f; // designed on a 64px grid
        switch (kind)
        {
            case WeatherKind.Clear:
                if (isDay) SunGlyph(canvas, c, 14 * s, rays: true);
                else MoonGlyph(canvas, c, 15 * s);
                break;
            case WeatherKind.PartlyCloudy:
                if (isDay) SunGlyph(canvas, new SKPoint(c.X - 9 * s, c.Y - 9 * s), 11 * s, rays: true);
                else MoonGlyph(canvas, new SKPoint(c.X - 9 * s, c.Y - 10 * s), 11 * s);
                Cloud(canvas, new SKPoint(c.X + 4 * s, c.Y + 6 * s), s, CloudLight);
                break;
            case WeatherKind.Cloudy:
                Cloud(canvas, new SKPoint(c.X + 7 * s, c.Y - 6 * s), s * 0.75f, CloudDark);
                Cloud(canvas, new SKPoint(c.X - 2 * s, c.Y + 4 * s), s, CloudLight);
                break;
            case WeatherKind.Fog:
                Cloud(canvas, new SKPoint(c.X, c.Y - 6 * s), s, CloudDark);
                using (var p = Stroke(CloudLight, 3 * s))
                {
                    canvas.DrawLine(c.X - 20 * s, c.Y + 12 * s, c.X + 16 * s, c.Y + 12 * s, p);
                    canvas.DrawLine(c.X - 14 * s, c.Y + 19 * s, c.X + 22 * s, c.Y + 19 * s, p);
                }
                break;
            case WeatherKind.Drizzle:
            case WeatherKind.Rain:
            case WeatherKind.Showers:
                if (kind == WeatherKind.Showers && isDay)
                    SunGlyph(canvas, new SKPoint(c.X - 11 * s, c.Y - 12 * s), 9 * s, rays: true);
                Cloud(canvas, new SKPoint(c.X, c.Y - 6 * s), s, CloudLight);
                using (var p = Stroke(RainColor, (kind == WeatherKind.Drizzle ? 2 : 3) * s))
                {
                    int drops = kind == WeatherKind.Drizzle ? 3 : 4;
                    for (int i = 0; i < drops; i++)
                    {
                        float x = c.X - 13 * s + i * (26f / (drops - 1)) * s;
                        canvas.DrawLine(x, c.Y + 10 * s, x - 4 * s, c.Y + (kind == WeatherKind.Drizzle ? 16 : 22) * s, p);
                    }
                }
                break;
            case WeatherKind.Snow:
                Cloud(canvas, new SKPoint(c.X, c.Y - 6 * s), s, CloudLight);
                using (var p = new SKPaint { IsAntialias = true, Color = SKColors.White })
                {
                    for (int i = 0; i < 4; i++)
                        canvas.DrawCircle(c.X - 13 * s + i * 8.7f * s, c.Y + (i % 2 == 0 ? 14 : 20) * s, 2.6f * s, p);
                }
                break;
            case WeatherKind.Thunder:
                Cloud(canvas, new SKPoint(c.X, c.Y - 8 * s), s, CloudDark);
                using (var b = new SKPathBuilder())
                {
                    b.MoveTo(c.X + 2 * s, c.Y + 2 * s);
                    b.LineTo(c.X - 7 * s, c.Y + 15 * s);
                    b.LineTo(c.X, c.Y + 15 * s);
                    b.LineTo(c.X - 4 * s, c.Y + 26 * s);
                    b.LineTo(c.X + 9 * s, c.Y + 10 * s);
                    b.LineTo(c.X + 2 * s, c.Y + 10 * s);
                    b.Close();
                    using var path = b.Detach();
                    using var p = new SKPaint { IsAntialias = true, Color = Bolt };
                    canvas.DrawPath(path, p);
                }
                break;
        }
    }

    private static SKPaint Stroke(SKColor color, float width) =>
        new() { IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round };

    public static void SunGlyph(SKCanvas canvas, SKPoint c, float r, bool rays)
    {
        using var glow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, r * 0.5f);
        using var p = new SKPaint { IsAntialias = true, Color = Sun.WithAlpha(110), MaskFilter = glow };
        canvas.DrawCircle(c, r * 1.1f, p);
        p.MaskFilter = null;
        p.Color = Sun;
        canvas.DrawCircle(c, r * 0.72f, p);
        if (!rays)
            return;
        using var ray = Stroke(Sun, Math.Max(1.5f, r * 0.16f));
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            canvas.DrawLine(c.X + cos * r * 1.05f, c.Y + sin * r * 1.05f, c.X + cos * r * 1.4f, c.Y + sin * r * 1.4f, ray);
        }
    }

    public static void MoonGlyph(SKCanvas canvas, SKPoint c, float r)
    {
        // crescent: a disc with a background-coloured disc bitten out, done with a clip so it works on any background
        canvas.Save();
        using (var b = new SKPathBuilder())
        {
            b.AddCircle(c.X + r * 0.55f, c.Y - r * 0.35f, r * 0.85f);
            using var bite = b.Detach();
            canvas.ClipPath(bite, SKClipOperation.Difference, antialias: true);
        }
        using var p = new SKPaint { IsAntialias = true, Color = MoonColor };
        canvas.DrawCircle(c, r, p);
        canvas.Restore();
    }

    private static void Cloud(SKCanvas canvas, SKPoint c, float s, SKColor color)
    {
        using var p = new SKPaint { IsAntialias = true, Color = color };
        canvas.DrawCircle(c.X - 9 * s, c.Y + 2 * s, 8 * s, p);
        canvas.DrawCircle(c.X + 1 * s, c.Y - 4 * s, 11 * s, p);
        canvas.DrawCircle(c.X + 11 * s, c.Y + 2 * s, 8 * s, p);
        canvas.DrawRoundRect(SKRect.Create(c.X - 17 * s, c.Y + 1 * s, 36 * s, 9 * s), 4.5f * s, 4.5f * s, p);
    }
}
