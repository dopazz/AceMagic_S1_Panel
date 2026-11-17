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

using System.Diagnostics;
using S1Panel.Core.Config;
using S1Panel.Core.Lcd;
using S1Panel.Core.Led;

namespace S1Panel.Core.Monitor;

public enum EngineState { Stopped, Starting, Running, Paused, WaitingForPanel, PanelBusy, Error }

public sealed record EngineStatus(EngineState State, string Message)
{
    public override string ToString() => State switch
    {
        EngineState.Running => $"Running - {Message}",
        EngineState.Paused => "Paused",
        EngineState.WaitingForPanel => $"Waiting for panel - {Message}",
        EngineState.PanelBusy => "Panel in use by another s1panel",
        EngineState.Error => $"Error - {Message}",
        _ => State.ToString(),
    };
}

/// <summary>
/// Drives the panel on a background thread: composes a frame every interval (aligned to the wall clock),
/// sends changed tiles, keeps the heartbeat going, applies LED settings, and recovers by itself when the
/// panel disappears or errors (unplug, sleep/resume). Thread-safe control methods for the UI.
/// </summary>
public sealed class MonitorEngine : IDisposable
{
    private readonly Func<PanelSettings, LcdPanel> _openPanel;
    private readonly Action<LedSettings> _applyLed;
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly CancellationTokenSource _cts = new();
    private PanelSettings _settings;
    private int _version;
    private bool _paused;
    private bool _reconnect;
    private Thread? _thread;

    /// <summary>Raised on the engine thread whenever the state or message changes.</summary>
    public event Action<EngineStatus>? StatusChanged;

    /// <summary>Raised on the engine thread after each frame: tiles sent (-1 = full redraw) and milliseconds taken.</summary>
    public event Action<int, long>? FramePresented;

    /// <summary>Raised on the engine thread when applying LED settings fails (the panel keeps running).</summary>
    public event Action<string>? Warning;

    /// <summary>Raised on the engine thread when drawing a frame throws something unexpected (a bug); the engine retries.</summary>
    public event Action<Exception>? UnexpectedError;

    public EngineStatus Status { get; private set; } = new(EngineState.Stopped, "");

    public MonitorEngine(PanelSettings settings, Func<PanelSettings, LcdPanel>? openPanel = null, Action<LedSettings>? applyLed = null)
    {
        settings.Validate();
        _settings = SettingsStore.Clone(settings);
        _openPanel = openPanel ?? (s => LcdPanel.Open(s.Device));
        _applyLed = applyLed ?? ApplyLedToStrip;
    }

    public void Start()
    {
        if (_thread is not null)
            return;
        _thread = new Thread(Run) { IsBackground = true, Name = "s1panel engine" };
        _thread.Start();
    }

    /// <summary>Switches to new settings; the next frame uses them (immediately, not at the next tick).</summary>
    public void UpdateSettings(PanelSettings settings)
    {
        settings.Validate();
        lock (_gate)
        {
            _settings = SettingsStore.Clone(settings);
            _version++;
        }
        _wake.Set();
    }

    public PanelSettings CurrentSettings
    {
        get { lock (_gate) return SettingsStore.Clone(_settings); }
    }

    public bool IsPaused
    {
        get { lock (_gate) return _paused; }
    }

    /// <summary>Stops driving the panel and releases it (so other tools can use it) until <see cref="Resume"/>.</summary>
    public void Pause()
    {
        lock (_gate) _paused = true;
        _wake.Set();
    }

    public void Resume()
    {
        lock (_gate) _paused = false;
        _wake.Set();
    }

    /// <summary>Re-opens the panel and does a full redraw (e.g. after the PC resumes from sleep).</summary>
    public void Reconnect()
    {
        lock (_gate) _reconnect = true;
        _wake.Set();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _wake.Set();
        _thread?.Join(TimeSpan.FromSeconds(10));
        _cts.Dispose();
        _wake.Dispose();
    }

    private void Run()
    {
        var ct = _cts.Token;
        PanelSession? session = null;
        PanelLock? panelLock = null;
        FrameComposer? composer = null;
        int composedVersion = -1;
        string? sessionKey = null, ledKey = null;

        void CloseSession()
        {
            session?.Dispose();
            session = null;
            sessionKey = null;
            panelLock?.Dispose();
            panelLock = null;
        }

        void Wait(int ms) => _wake.WaitOne(ms);

        SetStatus(EngineState.Starting, "");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                PanelSettings s;
                int version;
                bool paused, reconnect;
                lock (_gate)
                {
                    s = _settings.Effective();
                    version = _version;
                    paused = _paused;
                    reconnect = _reconnect;
                    _reconnect = false;
                }

                if (paused)
                {
                    CloseSession();
                    SetStatus(EngineState.Paused, "");
                    Wait(1000);
                    continue;
                }
                if (reconnect)
                    CloseSession();

                // LED: apply whenever the LED section changes (and once at start).
                string newLedKey = s.Led is null ? "" : SettingsStore.Serialize(new PanelSettings { Led = s.Led });
                if (newLedKey != ledKey)
                {
                    ledKey = newLedKey;
                    if (s.Led is not null)
                    {
                        try { _applyLed(s.Led); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException)
                        {
                            Warning?.Invoke($"LED settings not applied: {ex.Message}");
                        }
                    }
                }

                if (composer is null || composedVersion != version)
                {
                    composer?.Dispose();
                    composer = null;
                    try
                    {
                        composer = new FrameComposer(s);
                        composedVersion = version;
                    }
                    catch (ArgumentException ex)
                    {
                        SetStatus(EngineState.Error, ex.Message);
                        Wait(5000);
                        continue;
                    }
                }

                string key = $"{s.Orientation}|{s.Device}|{s.UpdateGapMs}|{s.RedrawDelayMs}";
                if (session is null || key != sessionKey)
                {
                    CloseSession();
                    panelLock = PanelLock.TryAcquire();
                    if (panelLock is null)
                    {
                        SetStatus(EngineState.PanelBusy, "");
                        Wait(5000);
                        continue;
                    }
                    try
                    {
                        var options = new PanelSessionOptions
                        {
                            UpdateGap = TimeSpan.FromMilliseconds(s.UpdateGapMs),
                            PostRedrawDelay = TimeSpan.FromMilliseconds(s.RedrawDelayMs),
                        };
                        session = new PanelSession(_openPanel(s), composer.Orientation, options);
                        sessionKey = key;
                    }
                    catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
                    {
                        CloseSession();
                        SetStatus(EngineState.WaitingForPanel, ex.Message.Split('\n')[0]);
                        Wait(5000);
                        continue;
                    }
                }

                try
                {
                    composer.ComposeNext();
                    var sw = Stopwatch.StartNew();
                    session.Present(composer.Bitmap);
                    FramePresented?.Invoke(session.LastTilesSent, sw.ElapsedMilliseconds);
                    SetStatus(EngineState.Running, $"{composer.Layout.Name}, {composer.Orientation.ToString().ToLowerInvariant()}");

                    // Hold until the next aligned tick, keeping the heartbeat alive; wake early on any control change.
                    var next = NextTick(s.IntervalMs);
                    while (!ct.IsCancellationRequested && DateTime.UtcNow < next)
                    {
                        session.KeepAlive();
                        int ms = (int)Math.Clamp((next - DateTime.UtcNow).TotalMilliseconds, 1, 250);
                        if (_wake.WaitOne(ms))
                            break;
                    }
                }
                catch (Exception ex) when (ex is IOException or TimeoutException)
                {
                    CloseSession();
                    SetStatus(EngineState.Error, $"{ex.Message.Split('\n')[0]} (retrying)");
                    Wait(2000);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException && !ct.IsCancellationRequested)
                {
                    // A bug in a layout/widget must not take the whole app down: report it and keep going.
                    UnexpectedError?.Invoke(ex);
                    SetStatus(EngineState.Error, $"Drawing failed: {ex.Message.Split('\n')[0]} (retrying)");
                    Wait(2000);
                }
            }
        }
        finally
        {
            CloseSession();
            composer?.Dispose();
            SetStatus(EngineState.Stopped, "");
        }
    }

    private void SetStatus(EngineState state, string message)
    {
        var status = new EngineStatus(state, message);
        if (status == Status)
            return;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    /// <summary>Next wall-clock multiple of the interval, +20 ms (keeps blinking colons / seconds in step).</summary>
    public static DateTime NextTick(int intervalMs)
    {
        long nowMs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
        long nextMs = (nowMs / intervalMs + 1) * intervalMs + 20;
        return new DateTime(nextMs * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    public static void ApplyLedToStrip(LedSettings led)
    {
        string port = led.Port ?? LedStrip.FindPorts().FirstOrDefault()
                      ?? throw new IOException("LED strip (CH340) not found.");
        LedStrip.Set(port, LedStrip.ParseMode(led.Mode), led.Intensity, led.Speed);
    }
}
