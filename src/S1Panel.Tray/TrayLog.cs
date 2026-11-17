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

namespace S1Panel.Tray;

/// <summary>
/// A small append-only log (s1panel-tray.log next to the settings file) of starts, exits and errors, so an app
/// that quietly disappeared leaves a trace. Kept under ~256 KB by trimming the older half.
/// </summary>
internal static class TrayLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Init(string settingsPath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(settingsPath));
        _path = dir is null ? null : Path.Combine(dir, "s1panel-tray.log");
    }

    public static void Write(string message)
    {
        if (_path is null) return;
        lock (Gate)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > 256 * 1024)
                {
                    string all = File.ReadAllText(_path);
                    File.WriteAllText(_path, all[(all.Length / 2)..]);
                }
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // logging must never take the app down
            }
        }
    }

    public static void Error(string where, Exception ex) => Write($"ERROR in {where}: {ex}");
}
