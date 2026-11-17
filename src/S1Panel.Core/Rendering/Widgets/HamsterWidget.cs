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

using S1Panel.Core.Rendering.Layouts;
using SkiaSharp;

namespace S1Panel.Core.Rendering.Widgets;

/// <summary>
/// A hamster in a wheel that runs as hard as the CPU works: snacking when idle, jogging, then sprinting, and
/// curled up asleep late at night. Counts the day's laps. The wheel turns at most 28° per frame so its
/// motion still reads correctly at one frame per second.
/// </summary>
public sealed class HamsterWidget : IWidget
{
    private static readonly SKColor Fur = new(0xE3, 0xA0, 0x5B);
    private static readonly SKColor FurDark = new(0xB8, 0x74, 0x3A);
    private static readonly SKColor Belly = new(0xF7, 0xDE, 0xBB);
    private static readonly SKColor Pink = new(0xF2, 0x9C, 0xA8);
    private static readonly SKColor Wheel = new(0x8C, 0x9C, 0xB8);

    private DateTime? _last;
    private double _angle;       // degrees, clockwise
    private double _lapsToday;
    private DateOnly _day;

    public string Name => "hamster";
    public string Description => "An animated hamster whose wheel spins with the CPU load";

    internal enum Pace { Sleeping, Snacking, Jogging, Sprinting }

    internal static Pace PaceFor(double cpu, int hour) =>
        hour < 6 && cpu < 30 ? Pace.Sleeping :
        cpu < 8 ? Pace.Snacking :
        cpu < 60 ? Pace.Jogging : Pace.Sprinting;

    /// <summary>Wheel turn per second, in degrees: 0 when not running, 6..28 when running.</summary>
    internal static double DegreesPerSecond(double cpu, Pace pace) =>
        pace is Pace.Jogging or Pace.Sprinting ? 6 + Math.Clamp(cpu, 0, 100) * 0.22 : 0;

    internal double LapsToday => _lapsToday;

    public void Draw(SKCanvas c, SKRect area, LayoutContext ctx)
    {
        var t = ctx.Stats.Time;
        long sec = t.Ticks / TimeSpan.TicksPerSecond;
        double cpu = ctx.Stats.CpuPercent;
        var pace = PaceFor(cpu, t.Hour);
        Advance(t, DegreesPerSecond(cpu, pace));

        float cx = area.MidX;
        float y0 = area.Top + (area.Height - 160) / 2; // design is 160 tall
        var hub = new SKPoint(cx, y0 + 62);
        const float R = 52;

        // Stand (behind the wheel) and floor.
        using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0x4A, 0x57, 0x70), StrokeWidth = 5, StrokeCap = SKStrokeCap.Round };
        c.DrawLine(hub.X, hub.Y, hub.X - 30, y0 + 124, p);
        c.DrawLine(hub.X, hub.Y, hub.X + 30, y0 + 124, p);
        p.Color = Gfx.CardEdge;
        p.StrokeWidth = 2;
        c.DrawLine(area.Left + 14, y0 + 125, area.Right - 14, y0 + 125, p);

        // Wheel: rim, rungs around the rim, six spokes and one coloured rung so the turning is easy to follow.
        p.Color = Wheel.WithAlpha(60);
        p.Style = SKPaintStyle.Fill;
        c.DrawCircle(hub, R, p);
        p.Style = SKPaintStyle.Stroke;
        p.Color = Wheel;
        p.StrokeWidth = 3;
        c.DrawCircle(hub, R, p);
        p.StrokeWidth = 1.5f;
        c.DrawCircle(hub, R - 7, p);
        for (int i = 0; i < 24; i++)
        {
            double a = (_angle + i * 15) * Math.PI / 180;
            var (cos, sin) = ((float)Math.Cos(a), (float)Math.Sin(a));
            p.Color = i == 0 ? Metrics.Amber : Wheel.WithAlpha(150);
            p.StrokeWidth = i == 0 ? 3 : 1.5f;
            c.DrawLine(hub.X + cos * (R - 7), hub.Y + sin * (R - 7), hub.X + cos * R, hub.Y + sin * R, p);
            if (i % 4 == 0)
            {
                p.Color = Wheel.WithAlpha(170);
                p.StrokeWidth = 1.5f;
                c.DrawLine(hub.X, hub.Y, hub.X + cos * (R - 7), hub.Y + sin * (R - 7), p);
            }
        }
        p.Style = SKPaintStyle.Fill;
        p.Color = Wheel;
        c.DrawCircle(hub, 4, p);

        // The hamster stands on the inside bottom of the wheel.
        var feet = new SKPoint(cx, hub.Y + R - 8);
        switch (pace)
        {
            case Pace.Sleeping: Sleeping(c, feet, sec); break;
            case Pace.Snacking: Snacking(c, feet, sec); break;
            default: Running(c, feet, sec, pace == Pace.Sprinting); break;
        }

        string caption = pace switch
        {
            Pace.Sleeping => "Sleeping",
            Pace.Snacking => "Snack break",
            Pace.Jogging => "Jogging",
            _ => "Sprinting!",
        };
        SKColor tone = pace == Pace.Sprinting ? Metrics.Orange : pace == Pace.Sleeping ? Gfx.Muted : Metrics.Amber;
        Gfx.TextCentered(c, $"{caption} · CPU {cpu:0}%", cx, y0 + 142, 11, Gfx.SemiBold, tone, area.Width - 12);
        Gfx.TextCentered(c, $"{_lapsToday:#,0} laps today", cx, y0 + 156, 9.5f, Gfx.Regular, Gfx.Muted, area.Width - 12);
    }

    /// <summary>Turns the wheel for the time since the last frame (capped, so a stall doesn't cause a jump) and counts laps.</summary>
    private void Advance(DateTime now, double degPerSec)
    {
        var today = DateOnly.FromDateTime(now);
        if (today != _day)
        {
            _day = today;
            _lapsToday = 0;
        }
        if (_last is { } last)
        {
            double dt = Math.Clamp((now - last).TotalSeconds, 0, 1.5);
            double turn = degPerSec * dt;
            _angle = (_angle + turn) % 360;
            _lapsToday += turn / 360;
        }
        _last = now;
    }

    private static void Running(SKCanvas c, SKPoint feet, long sec, bool fast)
    {
        bool stride = sec % 2 == 0;
        float bob = stride ? 0 : -2;
        using var p = new SKPaint { IsAntialias = true };

        // speed lines behind
        if (fast)
        {
            p.Color = Gfx.Muted.WithAlpha(150);
            p.StrokeWidth = 2;
            p.StrokeCap = SKStrokeCap.Round;
            for (int i = 0; i < 3; i++)
            {
                float y = feet.Y - 20 + i * 7 + bob;
                float len = stride ? 10 + i * 2 : 14 - i * 2;
                c.DrawLine(feet.X - 34 - len, y, feet.X - 30, y, p);
            }
        }

        // legs (back and front pairs swap each frame)
        p.Color = FurDark;
        float back = stride ? -12 : -4, front = stride ? 14 : 6;
        c.DrawOval(SKRect.Create(feet.X + back - 4, feet.Y - 4, 9, 5), p);
        c.DrawOval(SKRect.Create(feet.X + front - 4, feet.Y - 4, 9, 5), p);

        Body(c, new SKPoint(feet.X - 2, feet.Y - 15 + bob), stretch: 1.12f);
        Head(c, new SKPoint(feet.X + 15, feet.Y - 20 + bob), eyesClosed: false, cheeks: false);
    }

    private static void Snacking(SKCanvas c, SKPoint feet, long sec)
    {
        bool chew = sec % 2 == 0;
        using var p = new SKPaint { IsAntialias = true, Color = FurDark };
        c.DrawOval(SKRect.Create(feet.X - 12, feet.Y - 4, 9, 5), p);
        c.DrawOval(SKRect.Create(feet.X + 4, feet.Y - 4, 9, 5), p);

        // sitting up: rounder, taller body
        Body(c, new SKPoint(feet.X - 3, feet.Y - 16), stretch: 0.95f);
        var head = new SKPoint(feet.X + 9, feet.Y - 27);
        Head(c, head, eyesClosed: Hash(sec) % 6 == 0, cheeks: chew);

        // a sunflower seed held at the mouth
        c.Save();
        c.RotateDegrees(-30, head.X + 12, head.Y + 8);
        p.Color = new SKColor(0x3B, 0x34, 0x30);
        c.DrawOval(SKRect.Create(head.X + 8, head.Y + 5, 9, 5), p);
        p.Color = new SKColor(0xD9, 0xD3, 0xC7);
        c.DrawOval(SKRect.Create(head.X + 10, head.Y + 6.5f, 5, 2), p);
        c.Restore();
    }

    private static void Sleeping(SKCanvas c, SKPoint feet, long sec)
    {
        // curled-up ball, breathing slowly
        float breath = sec % 4 < 2 ? 0 : 1.5f;
        var center = new SKPoint(feet.X, feet.Y - 11 - breath / 2);
        using var p = new SKPaint { IsAntialias = true, Color = Fur };
        c.DrawOval(SKRect.Create(center.X - 22, center.Y - 12 - breath / 2, 44, 24 + breath), p);
        p.Color = Belly;
        c.DrawOval(SKRect.Create(center.X + 2, center.Y - 2, 16, 11), p);
        p.Color = Pink;
        c.DrawCircle(center.X - 10, center.Y - 11, 4, p);
        p.Color = FurDark;
        p.Style = SKPaintStyle.Stroke;
        p.StrokeWidth = 1.5f;
        p.StrokeCap = SKStrokeCap.Round;
        c.DrawArc(SKRect.Create(center.X + 6, center.Y - 7, 8, 5), 10, 160, false, p); // closed eye
        p.Style = SKPaintStyle.Fill;
        p.Color = Pink;
        c.DrawCircle(center.X + 19, center.Y + 1, 1.8f, p);

        int step = (int)(sec % 3);
        for (int i = 0; i <= step; i++)
            Gfx.TextAt(c, "z", center.X + 18 + i * 8, center.Y - 18 - i * 9, 9 + i * 3, Gfx.SemiBold, Gfx.Muted.WithAlpha((byte)(230 - i * 40)), SKTextAlign.Left);
    }

    private static void Body(SKCanvas c, SKPoint center, float stretch)
    {
        float w = 36 * stretch, h = 26 / stretch;
        using var p = new SKPaint { IsAntialias = true, Color = Fur };
        c.DrawOval(SKRect.Create(center.X - w / 2, center.Y - h / 2, w, h), p);
        p.Color = Belly;
        c.DrawOval(SKRect.Create(center.X - w * 0.2f, center.Y, w * 0.55f, h * 0.45f), p);
        p.Color = Pink;
        c.DrawCircle(center.X - w / 2 + 1, center.Y + 1, 2, p); // tiny tail
    }

    private static void Head(SKCanvas c, SKPoint center, bool eyesClosed, bool cheeks)
    {
        using var p = new SKPaint { IsAntialias = true, Color = Fur };
        // ear
        c.DrawCircle(center.X - 4, center.Y - 9, 5, p);
        p.Color = Pink;
        c.DrawCircle(center.X - 4, center.Y - 9, 2.6f, p);
        // head and cheeks
        p.Color = Fur;
        c.DrawCircle(center, 11, p);
        p.Color = Belly;
        float cheek = cheeks ? 7.5f : 6;
        c.DrawOval(SKRect.Create(center.X - 1, center.Y + 1, cheek * 1.6f, cheek), p);
        // eye
        if (eyesClosed)
        {
            p.Color = new SKColor(0x2A, 0x1E, 0x18);
            p.Style = SKPaintStyle.Stroke;
            p.StrokeWidth = 1.5f;
            c.DrawLine(center.X + 2, center.Y - 2, center.X + 7, center.Y - 2, p);
            p.Style = SKPaintStyle.Fill;
        }
        else
        {
            p.Color = new SKColor(0x1A, 0x12, 0x10);
            c.DrawCircle(center.X + 4.5f, center.Y - 2.5f, 2.6f, p);
            p.Color = SKColors.White;
            c.DrawCircle(center.X + 5.3f, center.Y - 3.4f, 0.9f, p);
        }
        // nose and whiskers
        p.Color = Pink;
        c.DrawCircle(center.X + 11, center.Y + 1, 1.8f, p);
        p.Color = Gfx.Text.WithAlpha(120);
        p.StrokeWidth = 0.8f;
        c.DrawLine(center.X + 10, center.Y + 2, center.X + 18, center.Y, p);
        c.DrawLine(center.X + 10, center.Y + 3, center.X + 18, center.Y + 4, p);
    }

    private static uint Hash(long n) => RobotWidget.Hash(n);
}
