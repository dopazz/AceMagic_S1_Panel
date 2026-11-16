using S1Panel.Core.Config;
using S1Panel.Core.Lcd;
using S1Panel.Core.Monitor;

namespace S1Panel.Tests;

/// <summary>Runs the real engine thread against a fake panel. Kept in one class so the machine-wide panel lock isn't contended.</summary>
public class EngineTests
{
    private static PanelSettings Fast(string layout = "tiles") => new()
    {
        Layout = layout, IntervalMs = 100, RedrawDelayMs = 0, UpdateGapMs = 0, Clock = "24", Units = "metric",
    };

    private static bool WaitFor(Func<bool> condition, int ms = 10_000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    [Fact]
    public void Engine_draws_follows_settings_pauses_and_holds_the_panel_lock()
    {
        var writers = new List<FakeWriter>();
        int frames = 0;
        var leds = new List<string>();

        using var engine = new MonitorEngine(Fast(),
            openPanel: _ => { var w = new FakeWriter(); lock (writers) writers.Add(w); return new LcdPanel(w); },
            applyLed: led => { lock (leds) leds.Add(led.Mode); });
        engine.FramePresented += (_, _) => Interlocked.Increment(ref frames);
        engine.Start();

        // First frame is a full redraw, then frames keep coming.
        Assert.True(WaitFor(() => Volatile.Read(ref frames) >= 3), "engine produced no frames");
        Assert.Equal(EngineState.Running, engine.Status.State);
        Assert.StartsWith("tiles", engine.Status.Message);
        Assert.Single(writers);
        Assert.Contains(writers[0].Reports, r => r[1] == 0x55 && r[2] == 0xA3); // redraw chunks

        // While the engine holds the panel, nobody else can take it.
        Assert.Null(PanelLock.TryAcquire());

        // Live settings change: layout switches without reopening the panel; LED settings get applied.
        var s = Fast("clock");
        s.Led = new LedSettings { Mode = "rainbow" };
        engine.UpdateSettings(s);
        Assert.True(WaitFor(() => engine.Status.Message.StartsWith("clock")), $"layout not switched: {engine.Status}");
        Assert.True(WaitFor(() => { lock (leds) return leds.Contains("rainbow"); }));
        Assert.Single(writers);

        // Orientation change reopens the panel (new session = orientation command + full redraw).
        var p = Fast("clock");
        p.Orientation = "portrait";
        engine.UpdateSettings(p);
        Assert.True(WaitFor(() => { lock (writers) return writers.Count == 2; }));

        // Pause releases the panel; resume takes it back.
        engine.Pause();
        Assert.True(WaitFor(() => engine.Status.State == EngineState.Paused));
        using (var probe = PanelLock.TryAcquire())
            Assert.NotNull(probe);
        engine.Resume();
        Assert.True(WaitFor(() => engine.Status.State == EngineState.Running));
    }

    [Fact]
    public void Missing_panel_is_retried_not_fatal()
    {
        int attempts = 0;
        using var engine = new MonitorEngine(Fast(), openPanel: _ =>
        {
            Interlocked.Increment(ref attempts);
            throw new IOException("AceMagic S1 LCD not found");
        });
        engine.Start();
        Assert.True(WaitFor(() => engine.Status.State == EngineState.WaitingForPanel));
        Assert.Contains("not found", engine.Status.Message);
        engine.Reconnect(); // wakes the retry loop early
        Assert.True(WaitFor(() => Volatile.Read(ref attempts) >= 2));
    }
}
