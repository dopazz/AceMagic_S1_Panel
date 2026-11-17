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
/// A half-screen panel element. Widgets are drawn into a local area with (0,0) at its top-left:
/// 160x170 (a landscape half: left/right) or 170x160 (a portrait half: top/bottom). Every widget must look
/// right in both shapes. Like layouts, keep per-second changes small; each write to the panel costs ~80 ms.
/// </summary>
public interface IWidget
{
    string Name { get; }
    string Description { get; }

    /// <param name="area">The widget's area in local coordinates (always starts at 0,0).</param>
    /// <param name="ctx">The frame's data and preferences (clock style, units, text scale...).</param>
    void Draw(SKCanvas canvas, SKRect area, LayoutContext ctx);
}

/// <summary>Implemented by widgets whose data loads in the background; true while they're showing a "loading" placeholder.</summary>
public interface IPendingWork
{
    bool Pending { get; }
}

/// <summary>Settings widgets need beyond the per-frame data (where we are, which photos...).</summary>
public sealed record WidgetEnvironment(string Location, S1Panel.Core.Config.SlideshowSettings Slideshow)
{
    public static WidgetEnvironment Default { get; } = new("auto", new S1Panel.Core.Config.SlideshowSettings());
}

public static class WidgetRegistry
{
    private static readonly (string Name, string Description, Func<IWidget> Create)[] Entries =
    [
        ("clock", "Large time with day and date", () => new ClockWidget()),
        ("cpu-ram", "CPU and memory rings with a CPU history graph", () => new CpuRamWidget()),
        ("weather", "Current weather and a 3-day forecast (Open-Meteo)", () => new WeatherWidget()),
        ("sun-moon", "Sunrise, sunset, daylight and the moon phase", () => new SunMoonWidget()),
        ("photos", "Slideshow of pictures from a folder", () => new PhotoWidget()),
        ("now-playing", "Album art, title, artist and progress of the music or video playing", () => new NowPlayingWidget()),
        ("robot", "An animated robot buddy whose mood follows the CPU load", () => new RobotWidget()),
        ("hamster", "An animated hamster whose wheel spins with the CPU load", () => new HamsterWidget()),
    ];

    public static IEnumerable<(string Name, string Description)> All => Entries.Select(e => (e.Name, e.Description));

    public static IWidget Create(string name) =>
        Entries.FirstOrDefault(e => string.Equals(e.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).Create?.Invoke()
        ?? throw new ArgumentException($"Unknown widget '{name}'. Available: {string.Join(", ", Entries.Select(e => e.Name))}.");
}
