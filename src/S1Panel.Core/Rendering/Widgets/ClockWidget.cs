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

/// <summary>Day name, large time (12/24 h, blinking colon per settings) and the date.</summary>
public sealed class ClockWidget : IWidget
{
    public string Name => "clock";
    public string Description => "Large time with day and date";

    public void Draw(SKCanvas c, SKRect area, LayoutContext ctx)
    {
        var t = ctx.Stats.Time;
        float cx = area.MidX, h = area.Height;

        Gfx.TextCentered(c, t.ToString("dddd"), cx, area.Top + h * 0.27f, 15, Gfx.Regular, Metrics.Cyan, area.Width - 16);
        Gfx.TimeCentered(c, t, ctx.Clock, cx, area.Top + h * 0.6f, 50, Gfx.Light, Gfx.Text, area.Width - 14);
        Gfx.TextCentered(c, t.ToString("d MMMM"), cx, area.Top + h * 0.77f, 15, Gfx.Regular, Gfx.Muted, area.Width - 16);
        Gfx.TextCentered(c, t.ToString("yyyy"), cx, area.Top + h * 0.89f, 11, Gfx.Regular, Gfx.Muted.WithAlpha(150), area.Width - 16);
    }
}
