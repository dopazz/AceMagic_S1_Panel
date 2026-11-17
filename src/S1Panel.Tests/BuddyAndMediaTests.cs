using S1Panel.Core.Rendering.Layouts;
using S1Panel.Core.Rendering.Widgets;
using S1Panel.Core.Sensors;
using S1Panel.Core.Services;
using SkiaSharp;

namespace S1Panel.Tests;

/// <summary>Robot, hamster and now-playing widgets. The media source is faked: no real Windows media sessions.</summary>
[Collection("NowPlaying")]
public class BuddyAndMediaTests
{
    private static StatsSnapshot At(DateTime t, double cpu) =>
        new(t, cpu, 40, 1, 2, 3, 4, 0, 0, TimeSpan.Zero, null, 2);

    private static LayoutContext Ctx(DateTime t, double cpu, int w = 160, int h = 170) => new(w, h, At(t, cpu), []);

    [Theory]
    [InlineData(3, 5, RobotWidget.Mood.Sleeping)]
    [InlineData(3, 95, RobotWidget.Mood.FlatOut)] // too busy to sleep
    [InlineData(14, 5, RobotWidget.Mood.Relaxed)]
    [InlineData(14, 30, RobotWidget.Mood.Working)]
    [InlineData(14, 70, RobotWidget.Mood.Busy)]
    [InlineData(14, 90, RobotWidget.Mood.FlatOut)]
    public void Robot_mood_follows_cpu_and_time(int hour, double cpu, RobotWidget.Mood expected) =>
        Assert.Equal(expected, RobotWidget.MoodFor(cpu, hour));

    [Fact]
    public void Hamster_wheel_speed_follows_cpu_and_stays_readable_at_one_frame_per_second()
    {
        Assert.Equal(0, HamsterWidget.DegreesPerSecond(3, HamsterWidget.PaceFor(3, 14)));
        Assert.Equal(0, HamsterWidget.DegreesPerSecond(5, HamsterWidget.PaceFor(5, 2)));
        double slow = HamsterWidget.DegreesPerSecond(20, HamsterWidget.PaceFor(20, 14));
        double fast = HamsterWidget.DegreesPerSecond(100, HamsterWidget.PaceFor(100, 14));
        Assert.True(fast > slow && slow > 0);
        Assert.True(fast < 30, "six spokes are 60° apart: more than 30°/frame would look like it turns backwards");
    }

    [Fact]
    public void Hamster_counts_laps_and_resets_at_midnight()
    {
        var h = new HamsterWidget();
        using var bmp = new SKBitmap(160, 170);
        using var c = new SKCanvas(bmp);
        var t = new DateTime(2026, 11, 16, 23, 0, 0);
        for (int i = 0; i <= 600; i++)
            h.Draw(c, SKRect.Create(0, 0, 160, 170), Ctx(t.AddSeconds(i), 100));
        Assert.InRange(h.LapsToday, 600 * 28.0 / 360 - 0.1, 600 * 28.0 / 360 + 0.1);

        h.Draw(c, SKRect.Create(0, 0, 160, 170), Ctx(new DateTime(2026, 11, 17, 0, 0, 1), 100));
        Assert.True(h.LapsToday < 0.2);
    }

    [Fact]
    public void Robot_and_hamster_animate_between_seconds()
    {
        foreach (IWidget w in new IWidget[] { new RobotWidget(), new HamsterWidget() })
        {
            var t = new DateTime(2026, 11, 16, 14, 0, 0); // on the hour: the robot waves
            byte[] a = Render(w, t, 70), b = Render(w, t.AddSeconds(1), 70);
            Assert.False(a.AsSpan().SequenceEqual(b), $"{w.Name} didn't change between frames");
        }
    }

    [Fact]
    public void Now_playing_shows_the_track_and_progress()
    {
        var original = NowPlayingService.Source;
        try
        {
            NowPlayingService.Reset();
            NowPlayingService.Source = _ => Task.FromResult<MediaInfo?>(new MediaInfo(
                "A Very Long Song Title That Will Not Fit On The Panel", "Some Artist", "Album", "Spotify", true,
                TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), DateTime.UtcNow, null));

            var w = new NowPlayingWidget();
            using var bmp = new SKBitmap(160, 170);
            using var c = new SKCanvas(bmp);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            do
            {
                c.Clear(SKColors.Black);
                w.Draw(c, SKRect.Create(0, 0, 160, 170), Ctx(DateTime.Now, 10));
                if (!w.Pending && NowPlayingService.Get().Media is not null) break;
                Thread.Sleep(20);
            } while (DateTime.UtcNow < deadline);
            c.Clear(SKColors.Black);
            w.Draw(c, SKRect.Create(0, 0, 160, 170), Ctx(DateTime.Now, 10));

            Assert.NotNull(NowPlayingService.Get().Media);
            // the green progress fill starts at the left of the bar (x=12, y≈133 with a 5 px top offset)
            Assert.Equal(new SKColor(0x1E, 0xD7, 0x60), bmp.GetPixel(14, 5 + 133));
        }
        finally
        {
            NowPlayingService.Source = original;
            NowPlayingService.Reset();
        }
    }

    [Fact]
    public void Last_track_is_held_through_a_short_session_gap_then_cleared()
    {
        var (source, poll, hold) = (NowPlayingService.Source, NowPlayingService.PollEvery, NowPlayingService.HoldLastFor);
        try
        {
            NowPlayingService.Reset();
            NowPlayingService.PollEvery = TimeSpan.FromMilliseconds(20);
            NowPlayingService.HoldLastFor = TimeSpan.FromMilliseconds(600);
            int calls = 0;
            NowPlayingService.Source = _ => Task.FromResult(Interlocked.Increment(ref calls) <= 2
                ? new MediaInfo("Song", "Band", "", "WACUP", true, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(3), DateTime.UtcNow, null)
                : null);

            static NowPlayingState WaitFor(Func<NowPlayingState, bool> ok)
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                NowPlayingState s;
                while (!ok(s = NowPlayingService.Get()) && DateTime.UtcNow < deadline) Thread.Sleep(10);
                return s;
            }

            Assert.True(WaitFor(s => s.Media is { Playing: true }).Media!.Playing);
            var held = WaitFor(_ => Volatile.Read(ref calls) >= 4).Media;
            Assert.NotNull(held);                 // session gone, track still shown...
            Assert.False(held!.Playing);          // ...but no longer advancing
            Assert.Null(WaitFor(s => s.Media is null).Media); // and cleared after the hold time
        }
        finally
        {
            (NowPlayingService.Source, NowPlayingService.PollEvery, NowPlayingService.HoldLastFor) = (source, poll, hold);
            NowPlayingService.Reset();
        }
    }

    [Theory]
    [InlineData("5132. Turnstile - Never Enough - Winamp [Stopped]", "Turnstile - Never Enough")]
    [InlineData("12. Toadies - Backslider - Winamp", "Toadies - Backslider")]
    [InlineData("7. Some Title - WACUP [Paused]", "Some Title")]
    [InlineData("Winamp 5.666 Build 3516", "Winamp 5.666 Build 3516")]
    public void Winamp_window_titles_parse(string windowText, string display) =>
        Assert.Equal(display, WinampSource.ParseDisplay(windowText));

    [Fact]
    public void Winamp_track_uses_media_session_tags_and_art_only_for_the_same_song()
    {
        using var art = SKImage.Create(new SKImageInfo(4, 4));
        var now = DateTime.UtcNow;
        var seen = new MediaInfo("Backslider", "Toadies", "Rubberneck", "Wacup", false, TimeSpan.Zero, TimeSpan.Zero, now, art, "wacup.exe");

        var same = NowPlayingService.WindowsMedia.FromWinamp(
            new WinampTrack("Toadies - Backslider", WinampStatus.Playing, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(200), "wacup.exe"), seen, now);
        Assert.Equal(("Backslider", "Toadies", "Rubberneck", "WACUP"), (same.Title, same.Artist, same.Album, same.App));
        Assert.Same(art, same.Art);
        Assert.True(same.Playing);
        Assert.Equal(TimeSpan.FromSeconds(200), same.Duration);

        var next = NowPlayingService.WindowsMedia.FromWinamp(
            new WinampTrack("The Guess Who - 969 (The Oldest Man)", WinampStatus.Paused, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(300), "wacup.exe"), seen, now);
        Assert.Equal(("969 (The Oldest Man)", "The Guess Who", ""), (next.Title, next.Artist, next.Album));
        Assert.Null(next.Art);
        Assert.False(next.Playing);
    }

    [Fact]
    public void Media_position_advances_only_while_playing()
    {
        var at = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var m = new MediaInfo("t", "a", "", "", true, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), at, null);
        Assert.Equal(TimeSpan.FromSeconds(15), m.PositionNow(at.AddSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(60), m.PositionNow(at.AddMinutes(5)));
        Assert.Equal(TimeSpan.FromSeconds(10), (m with { Playing = false }).PositionNow(at.AddSeconds(5)));
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Media Player")]
    [InlineData("MSEdge", "Edge")]
    [InlineData("Chrome", "Chrome")]
    [InlineData(@"C:\Program Files\VideoLAN\VLC\vlc.exe", "Vlc")]
    [InlineData("YKWPBHOUVGMYKNKNI5ERIZNJ2U", "")]
    [InlineData("Brave.YKWPBHOUVGMYKNKNI5ERIZNJ2U", "Brave")]
    [InlineData("", "")]
    public void App_names_are_friendly(string id, string expected) => Assert.Equal(expected, NowPlayingService.AppName(id));

    [Fact]
    public void Long_titles_are_trimmed_with_an_ellipsis()
    {
        string s = NowPlayingWidget.Clip(new string('W', 80), 13, Gfx.SemiBold, 140);
        Assert.EndsWith("…", s);
        Assert.True(s.Length < 80);
        Assert.Equal("Short", NowPlayingWidget.Clip("Short", 13, Gfx.SemiBold, 140));
    }

    private static byte[] Render(IWidget w, DateTime t, double cpu)
    {
        using var bmp = new SKBitmap(160, 170);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Black);
        w.Draw(c, SKRect.Create(0, 0, 160, 170), Ctx(t, cpu));
        return bmp.Bytes;
    }
}
