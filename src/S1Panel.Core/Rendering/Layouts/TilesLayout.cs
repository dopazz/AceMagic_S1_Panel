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

using SkiaSharp;

namespace S1Panel.Core.Rendering.Layouts;

/// <summary>
/// Clock + date, a large CPU-load dial and four ring-gauge tiles (SSD temp, memory, disk, network).
/// Same arrangement as the stock app's first layout, drawn from scratch.
/// </summary>
public sealed class TilesLayout : ILayout
{
    public string Name => "tiles";
    public string Description => "Clock, big CPU dial and four ring-gauge tiles";

    public void Draw(SKCanvas c, LayoutContext ctx)
    {
        var s = ctx.Stats;
        int w = ctx.Width, h = ctx.Height;
        Gfx.Background(c, w, h);

        Metric[] tiles = [Metrics.Temp(s, ctx.Fahrenheit), Metrics.MemUsage(s), Metrics.DiskUsed(s), Metrics.NetDown(s)];

        if (!ctx.Portrait)
        {
            ClockBlock(c, 68, 44, s.Time, ctx.Clock, 130);
            Dial(c, new SKPoint(68, 118), 40, Metrics.CpuLoad(s));
            TileGrid(c, new SKRect(140, 4, w - 4, h - 4), tiles);
        }
        else
        {
            ClockBlock(c, w / 2f, 44, s.Time, ctx.Clock, w - 12);
            Dial(c, new SKPoint(w / 2f, 116), 44, Metrics.CpuLoad(s));
            TileGrid(c, new SKRect(4, 170, w - 4, h - 4), tiles);
        }
    }

    private static void ClockBlock(SKCanvas c, float cx, float baseline, DateTime t, ClockStyle clock, float maxWidth)
    {
        Gfx.TimeCentered(c, t, clock, cx, baseline, 42, Gfx.Light, Gfx.Text, maxWidth);
        Gfx.TextCentered(c, Gfx.Date(t), cx, baseline + 18, 13, Gfx.Regular, Gfx.Muted);
    }

    /// <summary>Big round dial: dark disc, 300° arc, value and label in the middle.</summary>
    private static void Dial(SKCanvas c, SKPoint center, float r, Metric m)
    {
        using (var shader = SKShader.CreateRadialGradient(new SKPoint(center.X, center.Y - r * 0.4f), r * 1.3f,
                   [new SKColor(0x3A, 0x44, 0x58), new SKColor(0x14, 0x19, 0x24)], SKShaderTileMode.Clamp))
        using (var disc = new SKPaint { Shader = shader, IsAntialias = true })
            c.DrawCircle(center, r - 7, disc);

        Gfx.Arc(c, center, r, 6, 120, 300, m.Fraction, Metrics.Cyan);
        Gfx.TextCentered(c, m.ValueWithUnit, center.X, center.Y + 6, 24, Gfx.SemiBold, Gfx.Text, (r - 10) * 2);
        Gfx.TextCentered(c, m.Label, center.X, center.Y + 20, 9.5f, Gfx.Regular, Gfx.Muted);
    }

    private static void TileGrid(SKCanvas c, SKRect area, Metric[] metrics)
    {
        const float gap = 4;
        float tw = (area.Width - gap) / 2, th = (area.Height - gap) / 2;
        for (int i = 0; i < 4; i++)
        {
            var r = SKRect.Create(area.Left + (i % 2) * (tw + gap), area.Top + (i / 2) * (th + gap), tw, th);
            Tile(c, r, metrics[i]);
        }
    }

    private static void Tile(SKCanvas c, SKRect r, Metric m)
    {
        Gfx.CardRect(c, r);
        Gfx.Dot(c, r.Left + 9, r.Top + 10, 2.5f, m.Color);
        Gfx.TextAt(c, m.Label, r.Left + 16, r.Top + 14, 10, Gfx.Regular, Gfx.Muted, SKTextAlign.Left, r.Width - 20);

        float ringR = Math.Min(r.Width, r.Height - 18) / 2 - 7;
        var center = new SKPoint(r.MidX, r.Top + 18 + (r.Height - 18) / 2);
        Gfx.Arc(c, center, ringR, 3.5f, -90, 360, m.Fraction, m.Color, glow: false);
        float inner = ringR * 1.7f; // usable width inside the ring
        if (m.Unit.Length > 2)
        {
            // long units (kb/s, "/ 16 GB") go under the number
            Gfx.TextCentered(c, m.Value, center.X, center.Y + 3, 17, Gfx.SemiBold, Gfx.Text, inner);
            Gfx.TextCentered(c, m.Unit, center.X, center.Y + 14, 8, Gfx.Regular, Gfx.Muted, inner * 0.9f);
        }
        else
        {
            // short units (%, °F) sit small after the number; the pair is centred together
            Gfx.ValueUnitCentered(c, m.Value, m.Unit, center.X, center.Y + 6, 17, 9, Gfx.Text, Gfx.Muted, inner);
        }
    }
}
