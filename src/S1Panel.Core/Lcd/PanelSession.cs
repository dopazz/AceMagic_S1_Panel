using System.Diagnostics;
using SkiaSharp;

namespace S1Panel.Core.Lcd;

public sealed class PanelSessionOptions
{
    /// <summary>Minimum time between heartbeats. The firmware slows down if they come too often.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Pause after a full redraw before sending anything else (the original software waits ~1.6 s).</summary>
    public TimeSpan PostRedrawDelay { get; set; } = TimeSpan.FromMilliseconds(1600);

    /// <summary>Pause between consecutive partial-update reports; the firmware drops writes that come too fast.</summary>
    public TimeSpan UpdateGap { get; set; } = TimeSpan.FromMilliseconds(4);

    /// <summary>If true, every frame is sent as a full redraw instead of changed tiles.</summary>
    public bool AlwaysRedraw { get; set; }
}

/// <summary>
/// High-level driver: draw on an SKBitmap in logical orientation, call <see cref="Present"/>,
/// and only the changed areas are sent to the panel, in as few writes as possible (see <see cref="UpdatePlanner"/>). Call <see cref="KeepAlive"/> when idle.
/// </summary>
public sealed class PanelSession : IDisposable
{
    private readonly LcdPanel _lcd;
    private readonly byte[] _frame = new byte[LcdPanel.FrameBytes];
    private readonly byte[] _shown = new byte[LcdPanel.FrameBytes];
    private readonly byte[] _tile = new byte[LcdPanel.PayloadSize];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastHeartbeat = TimeSpan.MinValue;
    private bool _hasFrame;

    public Orientation Orientation { get; }
    public PanelSessionOptions Options { get; }

    /// <summary>Number of partial-update writes sent by the last <see cref="Present"/> (-1 = full redraw).</summary>
    public int LastTilesSent { get; private set; }

    public PanelSession(LcdPanel lcd, Orientation orientation, PanelSessionOptions? options = null)
    {
        _lcd = lcd;
        Orientation = orientation;
        Options = options ?? new PanelSessionOptions();
        _lcd.SetOrientation(orientation.IsPortrait());
        HeartbeatNow();
    }

    public SKBitmap CreateCanvasBitmap() => FrameConverter.CreateCanvasBitmap(Orientation);

    public void Present(SKBitmap logical)
    {
        FrameConverter.ToNative(logical, Orientation, _frame);

        if (!_hasFrame || Options.AlwaysRedraw)
        {
            _lcd.Redraw(_frame);
            _frame.CopyTo(_shown, 0);
            _hasFrame = true;
            LastTilesSent = -1;
            Thread.Sleep(Options.PostRedrawDelay);
            KeepAlive();
            return;
        }

        var plan = UpdatePlanner.Plan(_shown, _frame);
        foreach (var r in plan)
        {
            CopyRect(r);
            _lcd.Update(r.X, r.Y, r.W, r.H, _tile);
            Thread.Sleep(Options.UpdateGap);
        }

        _frame.CopyTo(_shown, 0);
        LastTilesSent = plan.Count;
        KeepAlive();
    }

    /// <summary>Sends a heartbeat if one is due. Call this at least every second or so while running.</summary>
    public void KeepAlive()
    {
        if (_clock.Elapsed - _lastHeartbeat >= Options.HeartbeatInterval)
            HeartbeatNow();
    }

    /// <summary>Forces the next <see cref="Present"/> to do a full redraw.</summary>
    public void Invalidate() => _hasFrame = false;

    public void Dispose() => _lcd.Dispose();

    private void HeartbeatNow()
    {
        _lcd.Heartbeat(DateTime.Now);
        _lastHeartbeat = _clock.Elapsed;
    }

    private void CopyRect(PixelRect r)
    {
        int d = 0;
        for (int y = r.Y; y < r.Bottom; y++)
        {
            _frame.AsSpan((y * LcdPanel.Width + r.X) * 2, r.W * 2).CopyTo(_tile.AsSpan(d));
            d += r.W * 2;
        }
    }
}
