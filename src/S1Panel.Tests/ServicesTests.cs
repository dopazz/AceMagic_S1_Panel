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
using S1Panel.Core.Rendering.Layouts;
using S1Panel.Core.Rendering.Widgets;
using S1Panel.Core.Sensors;
using S1Panel.Core.Services;
using SkiaSharp;

namespace S1Panel.Tests;

/// <summary>Weather/location/astronomy and the widgets that use them. No network or real Windows location: fakes only.</summary>
public class ServicesTests : IDisposable
{
    internal const string SampleWeatherJson = """
        {"latitude":41.85,"longitude":-87.65,
         "current":{"time":"2026-11-16T15:00","temperature_2m":11.2,"apparent_temperature":9.0,"relative_humidity_2m":61,
                    "weather_code":2,"is_day":1,"wind_speed_10m":14.5},
         "daily":{"time":["2026-11-16","2026-11-17","2026-11-18","2026-11-19"],
                  "weather_code":[2,61,71,95],
                  "temperature_2m_max":[12.0,9.5,2.0,14.1],
                  "temperature_2m_min":[1.0,3.0,-4.2,6.0],
                  "precipitation_probability_max":[5,80,60,null]}}
        """;

    private readonly string _photos = Directory.CreateTempSubdirectory("s1panel-photos-").FullName;

    public ServicesTests()
    {
        LocationService.WindowsLocator = _ => Task.FromResult<(GeoLocation?, string)>((new GeoLocation(41.85, -87.65, null, "fake"), ""));
        LocationService.Geocoder = (q, _) => Task.FromResult<GeoLocation?>(q == "60601" ? new GeoLocation(41.85, -87.65, "Chicago, Illinois", "fake") : null);
        WeatherService.Fetcher = (_, _) => Task.FromResult(SampleWeatherJson);
    }

    public void Dispose() => Directory.Delete(_photos, recursive: true);

    [Fact]
    public void Weather_json_parses()
    {
        var w = WeatherService.Parse(SampleWeatherJson, DateTime.UtcNow);
        Assert.Equal(11.2, w.TempC);
        Assert.True(w.IsDay);
        Assert.Equal(4, w.Days.Count);
        Assert.Equal(new DateOnly(2026, 11, 18), w.Days[2].Date);
        Assert.Equal(-4.2, w.Days[2].MinC);
        Assert.Null(w.Days[3].PrecipitationChance);
        Assert.Equal(WeatherKind.Snow, WeatherService.Describe(w.Days[2].Code).Kind);
        Assert.Equal(WeatherKind.Thunder, WeatherService.Describe(95).Kind);
    }

    [Theory]
    [InlineData("41.85,-87.65", true)]
    [InlineData(" -33.9 , 151.2 ", true)]
    [InlineData("91,0", false)]
    [InlineData("60601", false)]
    public void Coordinates_parse(string s, bool ok) => Assert.Equal(ok, LocationService.TryParseCoordinates(s, out _));

    [Fact]
    public async Task Location_resolves_auto_zip_and_reports_unknown()
    {
        Assert.Equal(LocationStatus.Ready, (await LocationService.ResolveAsync("auto", default)).Status);
        var zip = await LocationService.ResolveAsync("60601", default);
        Assert.Equal("Chicago, Illinois", zip.Location!.Name);
        Assert.Equal(LocationStatus.Failed, (await LocationService.ResolveAsync("nowhere-at-all", default)).Status);
    }

    [Fact]
    public async Task Auto_location_unavailable_raises_the_event_and_asks_for_input()
    {
        var original = LocationService.WindowsLocator;
        string? reason = null;
        void Handler(string why) => reason = why;
        LocationService.WindowsLocator = _ => Task.FromResult<(GeoLocation?, string)>((null, "Location is off."));
        LocationService.WindowsLocationUnavailable += Handler;
        try
        {
            var state = await LocationService.ResolveAsync("auto", default);
            Assert.Equal(LocationStatus.NeedsInput, state.Status);
            Assert.Equal("Location is off.", reason);
        }
        finally
        {
            LocationService.WindowsLocationUnavailable -= Handler;
            LocationService.WindowsLocator = original;
        }
    }

    [Fact]
    public void Sunrise_and_sunset_match_known_values()
    {
        // London (Greenwich), spring equinox 2024: sunrise ~06:03 UTC, sunset ~18:12 UTC.
        var sun = Astronomy.Sun(51.4779, 0.0, new DateOnly(2024, 3, 20), TimeZoneInfo.Utc);
        Assert.InRange(sun.Sunrise!.Value.TimeOfDay.TotalMinutes, 6 * 60 + 3 - 6, 6 * 60 + 3 + 6);
        Assert.InRange(sun.Sunset!.Value.TimeOfDay.TotalMinutes, 18 * 60 + 12 - 6, 18 * 60 + 12 + 6);
        // Tromsø in December: polar night.
        Assert.True(Astronomy.Sun(69.65, 18.96, new DateOnly(2024, 12, 21), TimeZoneInfo.Utc).AlwaysDown);
        // ...and midnight sun in June.
        Assert.True(Astronomy.Sun(69.65, 18.96, new DateOnly(2024, 6, 21), TimeZoneInfo.Utc).AlwaysUp);
    }

    [Fact]
    public void Moon_phase_matches_known_full_and_new_moons()
    {
        // Full moon 2024-01-25 17:54 UTC, new moon 2024-02-09 22:59 UTC.
        var m = Astronomy.Moon(new DateTime(2024, 1, 25, 18, 0, 0, DateTimeKind.Utc));
        Assert.Equal("Full moon", m.Name);
        Assert.True(m.Illumination > 0.97);
        var before = Astronomy.Moon(new DateTime(2024, 1, 20, 0, 0, 0, DateTimeKind.Utc));
        Assert.True(Math.Abs((before.NextFullUtc - new DateTime(2024, 1, 25, 17, 54, 0, DateTimeKind.Utc)).TotalHours) < 18);
        Assert.True(before.Waxing);
        Assert.Equal("New moon", Astronomy.Moon(new DateTime(2024, 2, 9, 23, 0, 0, DateTimeKind.Utc)).Name);
    }

    [Theory]
    [InlineData(160, 170)]
    [InlineData(170, 160)]
    public void Location_widgets_draw_real_content_once_data_arrives(int w, int h)
    {
        var env = new WidgetEnvironment("60601", new SlideshowSettings { Folder = _photos });
        foreach (var widget in new IWidget[] { new WeatherWidget(), new SunMoonWidget() })
        {
            using var bmp = new SKBitmap(w, h);
            using var c = new SKCanvas(bmp);
            var stats = new StatsSnapshot(new DateTime(2026, 11, 16, 13, 0, 0), 10, 40, 1, 2, 3, 4, 0, 0, TimeSpan.Zero, null, 2);
            var ctx = new LayoutContext(w, h, stats, [], Fahrenheit: true, Env: env);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            do
            {
                c.Clear(SKColors.Black);
                widget.Draw(c, SKRect.Create(0, 0, w, h), ctx);
                if (!((IPendingWork)widget).Pending) break;
                Thread.Sleep(50);
            } while (DateTime.UtcNow < deadline);
            Assert.False(((IPendingWork)widget).Pending, $"{widget.Name} still loading");
        }
    }

    [Fact]
    public void Photo_widget_shows_a_picture_and_reports_an_empty_folder()
    {
        var env = new WidgetEnvironment("auto", new SlideshowSettings { Folder = _photos, IntervalSeconds = 5 });
        var stats = new StatsSnapshot(DateTime.Now, 10, 40, 1, 2, 3, 4, 0, 0, TimeSpan.Zero, null, 2);

        using (var empty = new SKBitmap(160, 170))
        using (var c = new SKCanvas(empty))
        {
            var widget = new PhotoWidget();
            for (int i = 0; i < 100 && (i == 0 || widget.Pending); i++) { widget.Draw(c, SKRect.Create(0, 0, 160, 170), new LayoutContext(160, 170, stats, [], Env: env)); Thread.Sleep(20); }
            Assert.False(widget.Pending);
        }

        // A red test photo, stored sideways with EXIF-style dimensions doesn't matter here: just check it gets drawn.
        using (var photo = new SKBitmap(400, 300))
        {
            photo.Erase(SKColors.Red);
            using var data = photo.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(_photos, "red.png"), data.ToArray());
        }
        using var bmp = new SKBitmap(160, 170);
        using var canvas = new SKCanvas(bmp);
        var w2 = new PhotoWidget();
        for (int i = 0; i < 200 && (i == 0 || w2.Pending); i++)
        {
            canvas.Clear(SKColors.Black);
            w2.Draw(canvas, SKRect.Create(0, 0, 160, 170), new LayoutContext(160, 170, stats, [], Env: env));
            Thread.Sleep(20);
        }
        canvas.Clear(SKColors.Black);
        w2.Draw(canvas, SKRect.Create(0, 0, 160, 170), new LayoutContext(160, 170, stats, [], Env: env)); // frame after loading finished
        Assert.Equal(SKColors.Red, bmp.GetPixel(80, 85));
    }

    [Fact]
    public void Location_and_slideshow_settings_validate()
    {
        var s = SettingsStore.Set(new PanelSettings(), "location", "60601");
        Assert.Equal("60601", s.Location);
        s = SettingsStore.Set(s, "slideshow.intervalSeconds", "60");
        Assert.Equal(60, s.Slideshow.IntervalSeconds);
        Assert.Throws<ArgumentException>(() => SettingsStore.Set(new PanelSettings(), "slideshow.fit", "stretch"));
        Assert.Throws<ArgumentException>(() => SettingsStore.Set(new PanelSettings(), "slideshow.intervalSeconds", "1"));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), new SlideshowSettings().ResolvedFolder);
    }
}
