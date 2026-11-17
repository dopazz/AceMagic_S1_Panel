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

namespace S1Panel.Core.Monitor;

/// <summary>
/// Machine-wide "I'm driving the LCD" lock, so the tray app and a command-line monitor (even one started
/// over ssh, in another Windows session) never fight over the panel. Must be released on the thread that took it.
/// </summary>
public sealed class PanelLock : IDisposable
{
    private const string Name = @"Global\S1Panel.Lcd";
    private Mutex? _mutex;

    private PanelLock(Mutex mutex) => _mutex = mutex;

    /// <summary>Returns the lock, or null if another s1panel process holds it.</summary>
    public static PanelLock? TryAcquire()
    {
        var mutex = new Mutex(false, Name);
        try
        {
            if (mutex.WaitOne(0))
                return new PanelLock(mutex);
        }
        catch (AbandonedMutexException)
        {
            return new PanelLock(mutex); // previous owner died without releasing: it's ours now
        }
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_mutex is null)
            return;
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
        _mutex = null;
    }
}
