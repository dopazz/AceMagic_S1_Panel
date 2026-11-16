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
/// and only the changed 64x32 tiles are sent to the panel. Call <see cref="KeepAlive"/> when idle.
/// </summary>
public sealed class PanelSession : IDisposable
{
    // 64 * 32 * 2 = 4096 bytes: one tile fills exactly one report.
    private const int TileW = 64;
    private const int TileH = 32;

    private readonly LcdPanel _lcd;
    private readonly byte[] _frame = new byte[LcdPanel.FrameBytes];
    private readonly byte[] _shown = new byte[LcdPanel.FrameBytes];
    private readonly byte[] _tile = new byte[LcdPanel.PayloadSize];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastHeartbeat = TimeSpan.MinValue;
    private bool _hasFrame;

    public Orientation Orientation { get; }
    public PanelSessionOptions Options { get; }

    /// <summary>Number of tile updates sent by the last <see cref="Present"/> (-1 = full redraw).</summary>
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

        int sent = 0;
        for (int ty = 0; ty < LcdPanel.Height; ty += TileH)
        {
            int th = Math.Min(TileH, LcdPanel.Height - ty);
            for (int tx = 0; tx < LcdPanel.Width; tx += TileW)
            {
                int tw = Math.Min(TileW, LcdPanel.Width - tx);
                if (!TileChanged(tx, ty, tw, th))
                    continue;

                CopyTile(tx, ty, tw, th);
                _lcd.Update(tx, ty, tw, th, _tile);
                sent++;
                Thread.Sleep(Options.UpdateGap);
            }
        }

        _frame.CopyTo(_shown, 0);
        LastTilesSent = sent;
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

    private bool TileChanged(int tx, int ty, int tw, int th)
    {
        for (int y = ty; y < ty + th; y++)
        {
            int o = (y * LcdPanel.Width + tx) * 2;
            if (!_frame.AsSpan(o, tw * 2).SequenceEqual(_shown.AsSpan(o, tw * 2)))
                return true;
        }
        return false;
    }

    private void CopyTile(int tx, int ty, int tw, int th)
    {
        int d = 0;
        for (int y = ty; y < ty + th; y++)
        {
            _frame.AsSpan((y * LcdPanel.Width + tx) * 2, tw * 2).CopyTo(_tile.AsSpan(d));
            d += tw * 2;
        }
    }
}
