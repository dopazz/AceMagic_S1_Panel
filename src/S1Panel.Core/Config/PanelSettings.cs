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

using System.Globalization;
using S1Panel.Core.Lcd;
using S1Panel.Core.Led;
using S1Panel.Core.Rendering.Layouts;

namespace S1Panel.Core.Config;

/// <summary>LED strip settings. When present in the settings file, they're applied whenever the monitor starts.</summary>
public sealed class LedSettings
{
    /// <summary>rainbow | breathing | cycle | off | auto</summary>
    public string Mode { get; set; } = "breathing";

    /// <summary>1 (dim) .. 5 (bright)</summary>
    public int Intensity { get; set; } = 3;

    /// <summary>1 (slow) .. 5 (fast)</summary>
    public int Speed { get; set; } = 3;

    /// <summary>COM port, or null to auto-detect the CH340.</summary>
    public string? Port { get; set; }
}

/// <summary>Photo slideshow widget settings.</summary>
public sealed class SlideshowSettings
{
    /// <summary>Folder of pictures (subfolders included), or null for your Pictures folder.</summary>
    public string? Folder { get; set; }

    /// <summary>Seconds per photo. Each change costs about a second of panel bandwidth, so keep it relaxed.</summary>
    public int IntervalSeconds { get; set; } = 30;

    public bool Shuffle { get; set; } = true;

    /// <summary>cover (crop to fill) | contain (show the whole picture)</summary>
    public string Fit { get; set; } = "cover";

    [System.Text.Json.Serialization.JsonIgnore]
    public string ResolvedFolder => string.IsNullOrWhiteSpace(Folder)
        ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        : Environment.ExpandEnvironmentVariables(Folder);
}

/// <summary>
/// Everything that can live in s1panel.json. Values are kept as the same strings the command line accepts
/// ("auto", "larger", "portrait"...), so the file and the CLI share one vocabulary.
/// </summary>
public sealed class PanelSettings
{
    /// <summary>Monitor layout: tiles | gauge | clock | scene | dashboard</summary>
    public string Layout { get; set; } = "dashboard";

    /// <summary>landscape | portrait | landscape-flipped | portrait-flipped</summary>
    public string Orientation { get; set; } = "landscape";

    /// <summary>12 | 24 | auto (Windows regional time format)</summary>
    public string Clock { get; set; } = "auto";

    /// <summary>metric | imperial | auto (Windows region)</summary>
    public string Units { get; set; } = "auto";

    /// <summary>Blink the hours:minutes colon (the clock layout never blinks).</summary>
    public bool Blink { get; set; } = true;

    /// <summary>normal | large | larger | a number 0.5-2</summary>
    public string TextSize { get; set; } = "normal";

    /// <summary>Milliseconds between monitor updates.</summary>
    public int IntervalMs { get; set; } = 1000;

    /// <summary>Drive whose usage is shown.</summary>
    public string Drive { get; set; } = "C:";

    /// <summary>For weather and sunrise/sunset: "auto" (Windows location), a ZIP/postal code, a place name, or "lat,lon".</summary>
    public string Location { get; set; } = "auto";

    public SlideshowSettings Slideshow { get; set; } = new();

    /// <summary>The two widgets of the split layout: left/top then right/bottom (see WidgetRegistry).</summary>
    public string[] Widgets { get; set; } = ["clock", "cpu-ram"];

    /// <summary>Scene layout artwork: autumn | winter | spring | summer | auto (follows the calendar).</summary>
    public string Scene { get; set; } = "auto";

    /// <summary>Picture for the scene layout (overrides <see cref="Scene"/>), or null for the built-in art.</summary>
    public string? Background { get; set; }

    /// <summary>LED strip settings, or null to leave the LEDs alone.</summary>
    public LedSettings? Led { get; set; }

    /// <summary>
    /// "Lights Out": show the blank layout and switch the LED strip off, without touching <see cref="Layout"/> or
    /// <see cref="Led"/>, so turning it off again restores both. (With no LED settings the strip can't be restored
    /// automatically and stays off.)
    /// </summary>
    public bool LightsOut { get; set; }

    /// <summary>The settings the panel should actually use: these, or with <see cref="LightsOut"/> applied.</summary>
    public PanelSettings Effective()
    {
        if (!LightsOut)
            return this;
        var s = SettingsStore.Clone(this);
        s.Layout = "blank";
        s.Led = new LedSettings { Mode = "off", Port = Led?.Port };
        return s;
    }

    /// <summary>HID device path, or null to auto-detect.</summary>
    public string? Device { get; set; }

    /// <summary>Pause between partial-update writes (ms).</summary>
    public int UpdateGapMs { get; set; } = 4;

    /// <summary>Pause after a full-screen redraw (ms).</summary>
    public int RedrawDelayMs { get; set; } = 1600;

    /// <summary>Throws <see cref="ArgumentException"/> naming the first bad setting.</summary>
    public void Validate()
    {
        Check("layout", () => LayoutRegistry.Get(Layout));
        Check("orientation", () => OrientationExtensions.Parse(Orientation));
        Check("clock", () => ParseClock12(Clock));
        Check("units", () => ParseFahrenheit(Units));
        Check("textSize", () => LayoutExtensions.ParseTextSize(TextSize));
        Check("scene", () => SceneArt.Parse(Scene));
        if (string.IsNullOrWhiteSpace(Location)) throw new ArgumentException("location must be auto, a ZIP code, a place, or lat,lon.");
        Slideshow ??= new SlideshowSettings();
        if (Slideshow.IntervalSeconds is < 5 or > 86_400) throw new ArgumentException("slideshow.intervalSeconds must be 5..86400.");
        if (Slideshow.Fit is not ("cover" or "contain")) throw new ArgumentException("slideshow.fit must be cover or contain.");
        if (Widgets is not { Length: 2 })
            throw new ArgumentException($"widgets must list exactly two widgets (got {Widgets?.Length ?? 0}).");
        foreach (string w in Widgets)
            Check("widgets", () => S1Panel.Core.Rendering.Widgets.WidgetRegistry.Create(w));
        if (IntervalMs is < 100 or > 3_600_000) throw new ArgumentException("intervalMs must be 100..3600000.");
        if (UpdateGapMs is < 0 or > 1000) throw new ArgumentException("updateGapMs must be 0..1000.");
        if (RedrawDelayMs is < 0 or > 10_000) throw new ArgumentException("redrawDelayMs must be 0..10000.");
        if (string.IsNullOrWhiteSpace(Drive)) throw new ArgumentException("drive must not be empty.");
        if (Led is { } led)
        {
            Check("led.mode", () => LedStrip.ParseMode(led.Mode));
            if (led.Intensity is < 1 or > 5) throw new ArgumentException("led.intensity must be 1..5.");
            if (led.Speed is < 1 or > 5) throw new ArgumentException("led.speed must be 1..5.");
        }

        static void Check(string key, Action parse)
        {
            try { parse(); }
            catch (ArgumentException ex) when (!ex.Message.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"{key}: {ex.Message}", ex);
            }
        }
    }

    /// <summary>12 | 24 | auto. "auto" follows Windows: 12-hour time patterns use a lowercase 'h'.</summary>
    public static bool ParseClock12(string value) => value.Trim().ToLowerInvariant() switch
    {
        "12" => true,
        "24" => false,
        "auto" => CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('h'),
        _ => throw new ArgumentException($"clock must be 12, 24 or auto (got '{value}')."),
    };

    /// <summary>metric | imperial | auto. "auto" follows the Windows region.</summary>
    public static bool ParseFahrenheit(string value) => value.Trim().ToLowerInvariant() switch
    {
        "imperial" or "f" => true,
        "metric" or "c" => false,
        "auto" => !RegionInfo.CurrentRegion.IsMetric,
        _ => throw new ArgumentException($"units must be metric, imperial or auto (got '{value}')."),
    };
}
