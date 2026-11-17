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

/// <summary>Two ring gauges (CPU load, memory used / installed) above a CPU history graph.</summary>
public sealed class CpuRamWidget : IWidget
{
    public string Name => "cpu-ram";
    public string Description => "CPU and memory rings with a CPU history graph";

    public void Draw(SKCanvas c, SKRect area, LayoutContext ctx)
    {
        float w = area.Width, h = area.Height;
        float r = Math.Min(w * 0.2f, h * 0.2f);
        float ringY = area.Top + h * 0.3f;

        var cpu = Metrics.CpuLoad(ctx.Stats);
        var mem = Metrics.MemUsage(ctx.Stats);
        Ring(c, new SKPoint(area.Left + w * 0.27f, ringY), r, cpu, "CPU");
        Ring(c, new SKPoint(area.Left + w * 0.73f, ringY), r, mem, "RAM");

        float graphTop = ringY + r + 24;
        var graph = new SKRect(area.Left + 10, graphTop, area.Right - 10, area.Bottom - 8);
        if (graph.Height > 12)
            Gfx.Sparkline(c, graph, ctx.CpuHistory, Metrics.Blue);
    }

    private static void Ring(SKCanvas c, SKPoint center, float r, Metric m, string label)
    {
        Gfx.Arc(c, center, r, 5, -90, 360, m.Fraction, m.Color, glow: false);
        float inner = r * 1.6f;
        if (m.Unit.Length > 2)
        {
            // memory: "7.9" with "/ 16 GB" underneath
            Gfx.TextCentered(c, m.Value, center.X, center.Y + 3, 17, Gfx.SemiBold, Gfx.Text, inner);
            Gfx.TextCentered(c, m.Unit, center.X, center.Y + 14, 8, Gfx.Regular, Gfx.Muted, inner * 0.9f);
        }
        else
        {
            Gfx.ValueUnitCentered(c, m.Value, m.Unit, center.X, center.Y + 6, 17, 9, Gfx.Text, Gfx.Muted, inner);
        }
        Gfx.TextCentered(c, label, center.X, center.Y + r + 16, 11, Gfx.SemiBold, m.Color);
    }
}
