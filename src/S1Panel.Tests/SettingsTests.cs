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

using S1Panel.Core.Config;

namespace S1Panel.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("s1panel-tests-").FullName;
    private string PathFor(string name = "s1panel.json") => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Defaults_are_valid_and_leave_leds_alone()
    {
        var s = new PanelSettings();
        s.Validate();
        Assert.Equal("dashboard", s.Layout);
        Assert.Equal("normal", s.TextSize);
        Assert.Equal("auto", s.Clock);
        Assert.True(s.Blink);
        Assert.Null(s.Led);
    }

    [Fact]
    public void Explicit_path_that_does_not_exist_is_an_error()
    {
        Assert.Throws<ArgumentException>(() => SettingsStore.Load(PathFor("missing.json")));
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        var s = new PanelSettings { Layout = "scene", TextSize = "larger", Clock = "12", Led = new LedSettings { Mode = "rainbow" } };
        string written = SettingsStore.Save(s, PathFor());
        var (loaded, path) = SettingsStore.Load(written);
        Assert.Equal(PathFor(), path);
        Assert.Equal("scene", loaded.Layout);
        Assert.Equal("larger", loaded.TextSize);
        Assert.Equal("12", loaded.Clock);
        Assert.Equal("rainbow", loaded.Led!.Mode);
        Assert.Contains("\"textSize\": \"larger\"", File.ReadAllText(written)); // camelCase keys on disk
    }

    [Theory]
    [InlineData("text-size", "larger")]
    [InlineData("textSize", "larger")]
    [InlineData("TEXTSIZE", "larger")]
    public void Set_accepts_kebab_and_any_case(string key, string value)
    {
        var s = SettingsStore.Set(new PanelSettings(), key, value);
        Assert.Equal("larger", s.TextSize);
    }

    [Fact]
    public void Set_converts_types_and_aliases()
    {
        var s = new PanelSettings();
        s = SettingsStore.Set(s, "interval", "2000");
        s = SettingsStore.Set(s, "blink", "off");
        Assert.Equal(2000, s.IntervalMs);
        Assert.False(s.Blink);
        s = SettingsStore.Set(s, "no-blink", "false");
        Assert.True(s.Blink);
    }

    [Fact]
    public void Set_led_key_creates_led_section_and_none_clears_it()
    {
        var s = SettingsStore.Set(new PanelSettings(), "led.mode", "rainbow");
        Assert.NotNull(s.Led);
        Assert.Equal("rainbow", s.Led!.Mode);
        Assert.Equal(3, s.Led.Intensity); // defaults filled in
        s = SettingsStore.Set(s, "led.intensity", "5");
        Assert.Equal(5, s.Led!.Intensity);
        s = SettingsStore.Set(s, "led", "none");
        Assert.Null(s.Led);
    }

    [Theory]
    [InlineData("layout", "nope")]
    [InlineData("clock", "13")]
    [InlineData("units", "kelvin")]
    [InlineData("text-size", "huge")]
    [InlineData("interval", "abc")]
    [InlineData("led.speed", "9")]
    [InlineData("led.mode", "disco")]
    [InlineData("colour", "red")]
    public void Set_rejects_bad_values_and_unknown_keys(string key, string value)
    {
        Assert.Throws<ArgumentException>(() => SettingsStore.Set(new PanelSettings(), key, value));
    }

    [Fact]
    public void Load_reports_typos_and_bad_values_with_the_file_name()
    {
        File.WriteAllText(PathFor(), """{ "layuot": "scene" }""");
        var ex = Assert.Throws<ArgumentException>(() => SettingsStore.Load(PathFor()));
        Assert.Contains(PathFor(), ex.Message);

        File.WriteAllText(PathFor(), """{ "layout": "scene", "clock": "25" }""");
        ex = Assert.Throws<ArgumentException>(() => SettingsStore.Load(PathFor()));
        Assert.Contains("clock", ex.Message);
    }

    [Fact]
    public void Load_allows_comments_and_trailing_commas()
    {
        File.WriteAllText(PathFor(), """
            {
              // my panel
              "layout": "gauge",
              "textSize": "large",
            }
            """);
        var (s, _) = SettingsStore.Load(PathFor());
        Assert.Equal("gauge", s.Layout);
        Assert.Equal("large", s.TextSize);
    }
}

public class SettingsSerializationTests
{
    [Fact]
    public void Computed_values_are_not_written_to_the_file() =>
        Assert.DoesNotContain("resolvedFolder", S1Panel.Core.Config.SettingsStore.Serialize(new S1Panel.Core.Config.PanelSettings()));
}
