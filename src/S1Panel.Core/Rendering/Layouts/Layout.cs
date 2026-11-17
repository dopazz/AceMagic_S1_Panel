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

using S1Panel.Core.Sensors;
using SkiaSharp;

namespace S1Panel.Core.Rendering.Layouts;

/// <summary>Everything a layout needs to draw one frame.</summary>
/// <param name="Clock12">Show the time as 12-hour with AM/PM instead of 24-hour.</param>
/// <param name="Fahrenheit">Show temperatures in °F instead of °C.</param>
/// <param name="BlinkColon">Hide the hours:minutes colon on odd seconds (1 s on, 1 s off).</param>
/// <param name="TextScale">Text size multiplier (1 = design size). Applied mostly to small text; see <see cref="Gfx.Scaled"/>.</param>
public sealed record LayoutContext(int Width, int Height, StatsSnapshot Stats, IReadOnlyList<double> CpuHistory,
    bool Clock12 = false, bool Fahrenheit = false, bool BlinkColon = true, float TextScale = 1f,
    S1Panel.Core.Rendering.Widgets.WidgetEnvironment? Env = null)
{
    public ClockStyle Clock => new(Clock12, BlinkColon && Stats.Time.Second % 2 == 1);

    public bool Portrait => Height > Width;
}

/// <summary>
/// A full-screen design. Every layout must handle both landscape (320x170) and portrait (170x320).
/// Keep per-second changes small: the panel moves ~12 writes of 64x32 pixels per second.
/// </summary>
public interface ILayout
{
    string Name { get; }
    string Description { get; }
    void Draw(SKCanvas canvas, LayoutContext ctx);
}

public static class LayoutExtensions
{
    /// <summary>Draws the layout with the context's text scale applied. Use this rather than calling Draw directly.</summary>
    public static void Render(this ILayout layout, SKCanvas canvas, LayoutContext ctx)
    {
        float previous = Gfx.TextScale;
        Gfx.TextScale = ctx.TextScale;
        try { layout.Draw(canvas, ctx); }
        finally { Gfx.TextScale = previous; }
    }

    /// <summary>Parses --text-size: normal | large | larger, or a number such as 1.2.</summary>
    public static float ParseTextSize(string s) => s.Trim().ToLowerInvariant() switch
    {
        "normal" or "1" => 1f,
        "large" => 1.15f,
        "larger" => 1.3f,
        var v when float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f is >= 0.5f and <= 2f => f,
        _ => throw new ArgumentException($"--text-size must be normal, large, larger or a number 0.5-2 (got '{s}')."),
    };
}

/// <summary>
/// Plain black. The panel has no command to switch its backlight off, so this is the closest to "off":
/// after the first frame nothing changes, so nothing is sent except the once-a-second heartbeat.
/// </summary>
public sealed class BlankLayout : ILayout
{
    public string Name => "blank";
    public string Description => "Black screen (the panel has no off switch; the backlight stays on)";
    public void Draw(SKCanvas canvas, LayoutContext ctx) => canvas.Clear(SKColors.Black);
}

public static class LayoutRegistry
{
    public static IReadOnlyList<ILayout> All { get; } =
    [
        new TilesLayout(),
        new GaugeLayout(),
        new ClockLayout(),
        new SceneLayout(),
        new SplitLayout(),
        new DashboardLayout(),
        new BlankLayout(),
    ];

    /// <summary>The shared catalogue instance (for names/descriptions/validation). Use <see cref="Create"/> to draw.</summary>
    public static ILayout Get(string name) =>
        All.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown layout '{name}'. Available: {string.Join(", ", All.Select(l => l.Name))}.");

    /// <summary>A fresh instance, so layouts with state (the scene's cached picture) aren't shared between threads.</summary>
    public static ILayout Create(string name) => (ILayout)Activator.CreateInstance(Get(name).GetType())!;
}

/// <summary>The existing clock/bars/sparkline dashboard, wrapped as a layout.</summary>
public sealed class DashboardLayout : ILayout
{
    public string Name => "dashboard";
    public string Description => "Clock with CPU / RAM / disk bars, network and a CPU history graph";
    public void Draw(SKCanvas canvas, LayoutContext ctx) => Scenes.Dashboard(canvas, ctx.Width, ctx.Height, ctx.Stats, ctx.CpuHistory, ctx.Clock);
}
