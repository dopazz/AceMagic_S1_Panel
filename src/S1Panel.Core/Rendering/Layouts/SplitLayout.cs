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

using S1Panel.Core.Rendering.Widgets;
using SkiaSharp;

namespace S1Panel.Core.Rendering.Layouts;

/// <summary>
/// Two widgets, 50/50: left and right in landscape (160x170 each), top and bottom in portrait (170x160 each).
/// Each widget draws in its own clipped area with local coordinates.
/// </summary>
public sealed class SplitLayout : ILayout
{
    public static readonly string[] DefaultWidgets = ["clock", "cpu-ram"];

    private IWidget[] _widgets = DefaultWidgets.Select(WidgetRegistry.Create).ToArray();

    public string Name => "split";
    public string Description => "Two widgets, side by side (stacked in portrait)";

    public IReadOnlyList<IWidget> Widgets => _widgets;

    /// <summary>Sets the two widgets by name (first = left/top, second = right/bottom).</summary>
    public void SetWidgets(IReadOnlyList<string> names)
    {
        if (names.Count != 2)
            throw new ArgumentException($"widgets needs exactly two names (got {names.Count}).");
        _widgets = names.Select(WidgetRegistry.Create).ToArray();
    }

    public void Draw(SKCanvas c, LayoutContext ctx)
    {
        int w = ctx.Width, h = ctx.Height;
        Gfx.Background(c, w, h);

        for (int i = 0; i < 2; i++)
        {
            var area = ctx.Portrait
                ? SKRect.Create(0, i * h / 2f, w, h / 2f)
                : SKRect.Create(i * w / 2f, 0, w / 2f, h);
            c.Save();
            c.ClipRect(area);
            c.Translate(area.Left, area.Top);
            _widgets[i].Draw(c, SKRect.Create(0, 0, area.Width, area.Height), ctx);
            c.Restore();
        }

        // A faint divider, inset from the edges.
        using var line = new SKPaint { Color = Gfx.CardEdge, StrokeWidth = 1, IsAntialias = false };
        if (ctx.Portrait)
            c.DrawLine(14, h / 2f - 0.5f, w - 14, h / 2f - 0.5f, line);
        else
            c.DrawLine(w / 2f - 0.5f, 14, w / 2f - 0.5f, h - 14, line);
    }
}
