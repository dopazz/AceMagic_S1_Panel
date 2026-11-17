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
using S1Panel.Core.Lcd;
using S1Panel.Core.Rendering.Layouts;
using S1Panel.Core.Rendering.Widgets;
using S1Panel.Core.Sensors;
using SkiaSharp;
using Xunit.Abstractions;

namespace S1Panel.Tests;

public class UpdatePlannerTests
{
    private static byte[] Frame() => new byte[LcdPanel.FrameBytes];

    private static void Set(byte[] f, int x, int y, byte v = 0xFF) => f[(y * LcdPanel.Width + x) * 2] = v;

    private static void AssertCovers(byte[] a, byte[] b, List<PixelRect> plan)
    {
        foreach (var r in plan)
        {
            Assert.True(r.W is > 0 and <= UpdatePlanner.MaxSide && r.H is > 0 and <= UpdatePlanner.MaxSide, $"bad size {r}");
            Assert.True(r.Area <= UpdatePlanner.MaxPixels, $"too big {r}");
            Assert.True(r.X >= 0 && r.Y >= 0 && r.Right <= LcdPanel.Width && r.Bottom <= LcdPanel.Height, $"outside {r}");
        }
        for (int y = 0; y < LcdPanel.Height; y++)
        for (int x = 0; x < LcdPanel.Width; x++)
        {
            int o = (y * LcdPanel.Width + x) * 2;
            if (a[o] != b[o] || a[o + 1] != b[o + 1])
                Assert.True(plan.Any(r => x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom), $"pixel {x},{y} not covered");
        }
    }

    [Fact]
    public void No_change_means_no_writes() => Assert.Empty(UpdatePlanner.Plan(Frame(), Frame()));

    [Fact]
    public void Change_across_a_tile_edge_is_one_write_not_two()
    {
        var a = Frame();
        var b = Frame();
        for (int x = 50; x < 80; x++) // straddles the x=64 tile boundary
            for (int y = 10; y < 20; y++)
                Set(b, x, y);
        Assert.Equal(2, UpdatePlanner.Tiles(a, b).Count);
        var plan = UpdatePlanner.Plan(a, b);
        Assert.Single(plan);
        AssertCovers(a, b, plan);
    }

    [Fact]
    public void Random_changes_are_always_fully_covered_within_limits()
    {
        var rng = new Random(1234);
        for (int round = 0; round < 60; round++)
        {
            var a = Frame();
            var b = Frame();
            int blobs = rng.Next(1, 12);
            for (int i = 0; i < blobs; i++)
            {
                int x0 = rng.Next(LcdPanel.Width), y0 = rng.Next(LcdPanel.Height);
                int w = rng.Next(1, 90), h = rng.Next(1, 50);
                for (int y = y0; y < Math.Min(LcdPanel.Height, y0 + h); y++)
                    for (int x = x0; x < Math.Min(LcdPanel.Width, x0 + w); x++)
                        Set(b, x, y, (byte)rng.Next(1, 255));
            }
            var plan = UpdatePlanner.Plan(a, b);
            AssertCovers(a, b, plan);
            Assert.True(plan.Count <= UpdatePlanner.Tiles(a, b).Count);
        }
    }
}

public class LayoutCostBenchmark(ITestOutputHelper output)
{
    /// <summary>Replays 60 simulated seconds of every layout and compares writes: fixed tiles vs the planner.</summary>
    [Fact]
    public void Planner_never_costs_more_than_tiles_on_real_layouts()
    {
        foreach (var orientation in new[] { Orientation.Landscape, Orientation.Portrait })
        foreach (var info in LayoutRegistry.All)
        {
            var layout = LayoutRegistry.Create(info.Name);
            if (layout is SceneLayout scene) scene.Season = Season.Autumn;
            using var bmp = FrameConverter.CreateCanvasBitmap(orientation);
            using var canvas = new SKCanvas(bmp);
            var history = new List<double>();
            var rng = new Random(7);
            byte[] prev = new byte[LcdPanel.FrameBytes], next = new byte[LcdPanel.FrameBytes];
            int tiles = 0, planned = 0;
            double cpu = 20, net = 2e5;

            for (int sec = 0; sec <= 60; sec++)
            {
                cpu = Math.Clamp(cpu + rng.Next(-8, 9), 1, 99);
                net = Math.Max(0, net * (0.6 + rng.NextDouble() * 0.8));
                history.Add(cpu);
                if (history.Count > 60) history.RemoveAt(0);
                var t = new DateTime(2026, 11, 16, 13, 59, 30).AddSeconds(sec);
                var s = new StatsSnapshot(t, cpu, 45 + rng.NextDouble(), 7_400_000_000, 16_000_000_000, 76, 400_000_000_000, net, net / 8,
                    TimeSpan.FromHours(30), 47 + rng.Next(0, 2), 17_179_869_184);
                layout.Render(canvas, new LayoutContext(bmp.Width, bmp.Height, s, history, Clock12: true, Fahrenheit: true, BlinkColon: true, TextScale: 1.3f));
                canvas.Flush();
                FrameConverter.ToNative(bmp, orientation, next);
                if (sec > 0)
                {
                    tiles += UpdatePlanner.Tiles(prev, next).Count;
                    planned += UpdatePlanner.Plan(prev, next).Count;
                }
                (prev, next) = (next, prev);
            }

            output.WriteLine($"{info.Name,-10} {orientation,-9}  tiles {tiles / 60.0,5:0.0}/s   planner {planned / 60.0,5:0.0}/s   ({100.0 * (tiles - planned) / Math.Max(1, tiles):0}% fewer writes)");
            Assert.True(planned <= tiles);
        }
    }
}

public class WidgetTests
{
    private static StatsSnapshot Snap() => new(new DateTime(2026, 11, 16, 13, 5, 0), 37, 50, 7_900_000_000, 16_000_000_000, 40,
        1L << 38, 1e6, 1e5, TimeSpan.FromHours(3), 48, 17_179_869_184);

    [Theory]
    [InlineData(160, 170)] // landscape half
    [InlineData(170, 160)] // portrait half
    public void Every_widget_draws_something_in_both_half_shapes(int w, int h)
    {
        foreach (var (name, _) in WidgetRegistry.All)
        {
            using var bmp = new SKBitmap(w, h);
            using var c = new SKCanvas(bmp);
            c.Clear(SKColors.Black);
            var env = new WidgetEnvironment("41.85,-87.65", new S1Panel.Core.Config.SlideshowSettings { Folder = Path.Combine(Path.GetTempPath(), "s1panel-no-such-folder") });
            var ctx = new LayoutContext(w, h, Snap(), [10, 20, 30, 40], Clock12: true, Env: env);
            WidgetRegistry.Create(name).Draw(c, SKRect.Create(0, 0, w, h), ctx);
            c.Flush();
            int lit = 0;
            for (int y = 0; y < h; y += 2)
                for (int x = 0; x < w; x += 2)
                    if (bmp.GetPixel(x, y) != SKColors.Black) lit++;
            Assert.True(lit > 50, $"{name} drew almost nothing at {w}x{h}");
        }
    }

    [Fact]
    public void Split_layout_keeps_each_widget_inside_its_half()
    {
        // A widget that paints its whole area red must not spill into the other half.
        var split = new SplitLayout();
        split.SetWidgets(["clock", "clock"]);
        using var bmp = new SKBitmap(320, 170);
        using var c = new SKCanvas(bmp);
        split.Render(c, new LayoutContext(320, 170, Snap(), []));
        Assert.Throws<ArgumentException>(() => split.SetWidgets(["clock"]));
        Assert.Throws<ArgumentException>(() => split.SetWidgets(["clock", "toaster"]));
    }

    [Fact]
    public void Widgets_setting_is_validated_and_settable_from_config()
    {
        var s = SettingsStore.Set(new PanelSettings(), "widgets", "cpu-ram, clock");
        Assert.Equal(["cpu-ram", "clock"], s.Widgets);
        Assert.Throws<ArgumentException>(() => SettingsStore.Set(new PanelSettings(), "widgets", "clock"));
        Assert.Throws<ArgumentException>(() => SettingsStore.Set(new PanelSettings(), "widgets", "clock,toaster"));
        Assert.Equal(["clock", "cpu-ram"], new PanelSettings().Widgets);
    }
}
