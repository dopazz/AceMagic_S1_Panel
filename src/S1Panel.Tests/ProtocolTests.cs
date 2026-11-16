using S1Panel.Core.Hid;
using S1Panel.Core.Lcd;
using S1Panel.Core.Led;
using SkiaSharp;

namespace S1Panel.Tests;

/// <summary>Records reports instead of sending them to hardware.</summary>
internal sealed class FakeWriter : IReportWriter
{
    public List<byte[]> Reports { get; } = [];
    public int OutputReportLength => LcdPanel.ReportSize;
    public void Write(ReadOnlySpan<byte> report) => Reports.Add(report.ToArray());
    public void Dispose() { }

    /// <summary>The 8-byte header of report n, as hex like the README captures.</summary>
    public string Header(int n) => Convert.ToHexString(Reports[n], 1, 8).ToLowerInvariant();
}

public class LcdProtocolTests
{
    [Fact]
    public void Redraw_matches_usb_capture()
    {
        var w = new FakeWriter();
        new LcdPanel(w).Redraw(new byte[LcdPanel.FrameBytes]);

        Assert.Equal(27, w.Reports.Count);
        Assert.All(w.Reports, r => Assert.Equal(4105, r.Length));
        Assert.All(w.Reports, r => Assert.Equal(0, r[0])); // report ID
        // From the captures in example/.../README.md
        Assert.Equal("55a3f00100000010", w.Header(0));
        Assert.Equal("55a3f10200100010", w.Header(1));
        Assert.Equal("55a3f10300200010", w.Header(2));
        Assert.Equal("55a3f11100000010", w.Header(16)); // offset wraps at 64K
        Assert.Equal("55a3f21b00a00009", w.Header(26)); // last chunk: 2304 bytes
    }

    [Fact]
    public void Redraw_payload_is_frame_in_order()
    {
        var frame = new byte[LcdPanel.FrameBytes];
        for (int i = 0; i < frame.Length; i++) frame[i] = (byte)(i * 7 + i / 4096);
        var w = new FakeWriter();
        new LcdPanel(w).Redraw(frame);

        var joined = w.Reports.SelectMany((r, i) => r.Skip(9).Take(i == 26 ? 2304 : 4096)).ToArray();
        Assert.Equal(frame, joined);
    }

    [Fact]
    public void Update_matches_usb_capture()
    {
        var w = new FakeWriter();
        new LcdPanel(w).Update(0x12, 0x32, 0x18, 0x0C, new byte[0x18 * 0x0C * 2]);
        Assert.Equal("55a212003200180c", w.Header(0));
    }

    [Fact]
    public void Update_rejects_oversized_rect()
    {
        var lcd = new LcdPanel(new FakeWriter());
        Assert.Throws<ArgumentException>(() => lcd.Update(0, 0, 65, 32, new byte[65 * 32 * 2]));
    }

    [Fact]
    public void Config_commands()
    {
        var w = new FakeWriter();
        var lcd = new LcdPanel(w);
        lcd.SetOrientation(portrait: true);
        lcd.SetOrientation(portrait: false);
        lcd.Heartbeat(new DateTime(2024, 1, 1, 14, 28, 45));
        Assert.Equal("55a1f10200000000", w.Header(0));
        Assert.Equal("55a1f10100000000", w.Header(1));
        Assert.Equal("55a1f20e1c2d0000", w.Header(2));
    }
}

public class FrameConverterTests
{
    private static (int X, int Y) FindLitNativePixel(byte[] frame)
    {
        for (int i = 0; i < frame.Length; i += 2)
            if (frame[i] != 0 || frame[i + 1] != 0)
                return ((i / 2) % LcdPanel.Width, (i / 2) / LcdPanel.Width);
        throw new InvalidOperationException("no lit pixel");
    }

    [Theory]
    // Portrait matches the original software's translate(0,170)+rotate(-90): logical (x,y) -> native (y, 169-x)
    [InlineData(Orientation.Portrait, 0, 0, 0, 169)]
    [InlineData(Orientation.Portrait, 169, 319, 319, 0)]
    [InlineData(Orientation.Portrait, 10, 40, 40, 159)]
    [InlineData(Orientation.Landscape, 10, 40, 10, 40)]
    [InlineData(Orientation.LandscapeFlipped, 0, 0, 319, 169)]
    [InlineData(Orientation.PortraitFlipped, 0, 0, 319, 0)]
    public void Single_pixel_lands_in_expected_native_position(Orientation o, int lx, int ly, int nx, int ny)
    {
        using var bmp = FrameConverter.CreateCanvasBitmap(o);
        bmp.Erase(SKColors.Black);
        bmp.SetPixel(lx, ly, SKColors.White);
        var frame = new byte[LcdPanel.FrameBytes];
        FrameConverter.ToNative(bmp, o, frame);
        Assert.Equal((nx, ny), FindLitNativePixel(frame));
    }

    [Fact]
    public void Pixels_are_big_endian_rgb565()
    {
        using var bmp = FrameConverter.CreateCanvasBitmap(Orientation.Landscape);
        bmp.Erase(new SKColor(0xFF, 0x00, 0x00));
        var frame = new byte[LcdPanel.FrameBytes];
        FrameConverter.ToNative(bmp, Orientation.Landscape, frame);
        Assert.Equal(0xF8, frame[0]); // red 0xF800, high byte first
        Assert.Equal(0x00, frame[1]);

        bmp.Erase(new SKColor(0x00, 0x00, 0xFF));
        FrameConverter.ToNative(bmp, Orientation.Landscape, frame);
        Assert.Equal(0x00, frame[0]);
        Assert.Equal(0x1F, frame[1]);
    }

    [Theory]
    [InlineData(Orientation.Portrait)]
    [InlineData(Orientation.PortraitFlipped)]
    [InlineData(Orientation.LandscapeFlipped)]
    public void Round_trip_preserves_layout(Orientation o)
    {
        using var bmp = FrameConverter.CreateCanvasBitmap(o);
        bmp.Erase(SKColors.Black);
        bmp.SetPixel(3, 5, SKColors.White);
        var frame = new byte[LcdPanel.FrameBytes];
        FrameConverter.ToNative(bmp, o, frame);
        using var back = FrameConverter.FromNative(frame, o);
        Assert.Equal(SKColors.White, back.GetPixel(3, 5));
        Assert.Equal(SKColors.Black, back.GetPixel(4, 5));
    }
}

public class PanelSessionTests
{
    [Fact]
    public void First_frame_redraws_then_only_changed_tiles_are_sent()
    {
        var w = new FakeWriter();
        var opts = new PanelSessionOptions { PostRedrawDelay = TimeSpan.Zero, UpdateGap = TimeSpan.Zero, HeartbeatInterval = TimeSpan.FromHours(1) };
        using var session = new PanelSession(new LcdPanel(w), Orientation.Landscape, opts);
        int setup = w.Reports.Count; // orientation + heartbeat
        Assert.Equal(2, setup);

        using var bmp = session.CreateCanvasBitmap();
        bmp.Erase(SKColors.Black);
        session.Present(bmp);
        Assert.Equal(-1, session.LastTilesSent);
        Assert.Equal(setup + 27, w.Reports.Count);

        session.Present(bmp);
        Assert.Equal(0, session.LastTilesSent);

        bmp.SetPixel(100, 50, SKColors.White); // tile (64..127, 32..63)
        session.Present(bmp);
        Assert.Equal(1, session.LastTilesSent);
        Assert.Equal("55a2400020004020", w.Header(w.Reports.Count - 1));

        bmp.SetPixel(319, 169, SKColors.White); // bottom-right partial tile: 64 x 10
        session.Present(bmp);
        Assert.Equal("55a20001a000400a", w.Header(w.Reports.Count - 1)); // x=256, y=160 (LE), 64x10
    }
}

public class LedTests
{
    [Fact]
    public void Command_bytes_and_checksum()
    {
        // Rainbow at friendly level 3/3 -> wire 3/3; checksum = low byte of FA+01+03+03 = 0x101.
        Assert.Equal(new byte[] { 0xFA, 0x01, 0x03, 0x03, 0x01 }, LedStrip.BuildCommand(LedMode.Rainbow, 3, 3));
        // Brightest / fastest is wire value 1.
        Assert.Equal(new byte[] { 0xFA, 0x02, 0x01, 0x01, 0xFE }, LedStrip.BuildCommand(LedMode.Breathing, 5, 5));
        // Off always sends 5/5.
        Assert.Equal(new byte[] { 0xFA, 0x04, 0x05, 0x05, 0x08 }, LedStrip.BuildCommand(LedMode.Off, 1, 1));
    }
}

public class MetricsTests
{
    private static S1Panel.Core.Sensors.StatsSnapshot Snap(double? tempC, ulong usedBytes, ulong installedBytes, int second = 0) =>
        new(new DateTime(2026, 11, 16, 13, 5, second), 10, 50, usedBytes, installedBytes - (1UL << 28), 40, 1L << 30, 0, 0,
            TimeSpan.Zero, tempC, installedBytes);

    [Fact]
    public void Temperature_label_and_units()
    {
        var c = S1Panel.Core.Rendering.Layouts.Metrics.Temp(Snap(48, 0, 16UL << 30), fahrenheit: false);
        var f = S1Panel.Core.Rendering.Layouts.Metrics.Temp(Snap(48, 0, 16UL << 30), fahrenheit: true);
        Assert.Equal("Temp", c.Label);
        Assert.Equal("48°C", c.ValueWithUnit);
        Assert.Equal("118°F", f.ValueWithUnit); // 48 * 9/5 + 32 = 118.4
        Assert.Equal("--", S1Panel.Core.Rendering.Layouts.Metrics.Temp(Snap(null, 0, 16UL << 30), true).Value);
    }

    [Fact]
    public void Memory_shows_used_over_installed_total()
    {
        var m = S1Panel.Core.Rendering.Layouts.Metrics.MemUsage(Snap(48, (ulong)(7.9 * (1UL << 30)), 16UL << 30));
        Assert.Equal("Mem Usage", m.Label);
        Assert.Equal("7.9 / 16 GB", m.ValueWithUnit);
        var odd = S1Panel.Core.Rendering.Layouts.Metrics.MemUsage(Snap(48, 3UL << 30, 12UL << 29)); // 6 GB
        Assert.Equal("3.0 / 6 GB", odd.ValueWithUnit);
    }

    [Fact]
    public void Colon_blinks_on_odd_seconds_only_when_enabled()
    {
        var even = new S1Panel.Core.Rendering.Layouts.LayoutContext(320, 170, Snap(48, 0, 16UL << 30, 2), []);
        var oddCtx = even with { Stats = Snap(48, 0, 16UL << 30, 3) };
        Assert.False(even.Clock.ColonHidden);
        Assert.True(oddCtx.Clock.ColonHidden);
        Assert.False((oddCtx with { BlinkColon = false }).Clock.ColonHidden);
    }
}

public class SeasonTests
{
    [Theory]
    [InlineData(1, S1Panel.Core.Rendering.Layouts.Season.Winter)]
    [InlineData(2, S1Panel.Core.Rendering.Layouts.Season.Winter)]
    [InlineData(3, S1Panel.Core.Rendering.Layouts.Season.Spring)]
    [InlineData(6, S1Panel.Core.Rendering.Layouts.Season.Summer)]
    [InlineData(9, S1Panel.Core.Rendering.Layouts.Season.Autumn)]
    [InlineData(11, S1Panel.Core.Rendering.Layouts.Season.Autumn)]
    [InlineData(12, S1Panel.Core.Rendering.Layouts.Season.Winter)]
    public void Season_follows_the_calendar(int month, S1Panel.Core.Rendering.Layouts.Season expected) =>
        Assert.Equal(expected, S1Panel.Core.Rendering.Layouts.SceneArt.SeasonOf(new DateTime(2026, month, 15)));

    [Fact]
    public void Scene_names_parse()
    {
        Assert.Null(S1Panel.Core.Rendering.Layouts.SceneArt.Parse("auto"));
        Assert.Equal(S1Panel.Core.Rendering.Layouts.Season.Autumn, S1Panel.Core.Rendering.Layouts.SceneArt.Parse("fall"));
        Assert.Throws<ArgumentException>(() => S1Panel.Core.Rendering.Layouts.SceneArt.Parse("monsoon"));
    }

    [Fact]
    public void Every_season_renders_in_both_orientations()
    {
        foreach (var season in Enum.GetValues<S1Panel.Core.Rendering.Layouts.Season>())
        foreach (var (w, h) in new[] { (320, 170), (170, 320) })
        {
            using var bmp = new SkiaSharp.SKBitmap(w, h);
            using var c = new SkiaSharp.SKCanvas(bmp);
            S1Panel.Core.Rendering.Layouts.SceneArt.Draw(c, w, h, season);
            Assert.NotEqual(bmp.GetPixel(w / 2, 5), bmp.GetPixel(w / 2, h - 5)); // sky differs from ground
        }
    }
}

public class LedRawTests
{
    [Fact]
    public void Raw_command_matches_documented_builder_for_known_modes()
    {
        // friendly level 3 == wire 3
        Assert.Equal(S1Panel.Core.Led.LedStrip.BuildCommand(S1Panel.Core.Led.LedMode.Rainbow, 3, 3),
                     S1Panel.Core.Led.LedStrip.BuildRaw(0x01, 3, 3));
        Assert.Equal(new byte[] { 0xFA, 0x06, 0x03, 0x03, 0x06 }, S1Panel.Core.Led.LedStrip.BuildRaw(0x06, 3, 3));
    }
}
