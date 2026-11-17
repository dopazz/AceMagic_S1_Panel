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

using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace S1Panel.Core.Services;

public enum WinampStatus { Stopped = 0, Playing = 1, Paused = 3 }

/// <param name="Display">The playlist title as shown in the player, normally "Artist - Title".</param>
/// <param name="ExeName">The player's executable, e.g. "wacup.exe".</param>
public sealed record WinampTrack(string Display, WinampStatus Status, TimeSpan Position, TimeSpan Length, string ExeName);

/// <summary>
/// Reads Winamp-compatible players (WACUP, Winamp) through their classic window-message API: the "Winamp v1.x"
/// window's title and IPC_ISPLAYING / IPC_GETOUTPUTTIME. Works as a normal user in the same desktop session.
/// Needed because WACUP withdraws its Windows media session a few seconds into most tracks.
/// </summary>
public static partial class WinampSource
{
    private const uint WM_USER = 0x0400;
    private const int IPC_ISPLAYING = 104, IPC_GETOUTPUTTIME = 105;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    public static WinampTrack? Read()
    {
        IntPtr hwnd = FindWindowW("Winamp v1.x", null);
        if (hwnd == IntPtr.Zero)
            return null;

        var text = new StringBuilder(1024);
        GetWindowTextW(hwnd, text, text.Capacity);
        if (!Ask(hwnd, 0, IPC_ISPLAYING, out long status) ||
            !Ask(hwnd, 0, IPC_GETOUTPUTTIME, out long posMs) ||
            !Ask(hwnd, 1, IPC_GETOUTPUTTIME, out long lenS))
            return null; // player busy/hung: try again next poll

        GetWindowThreadProcessId(hwnd, out uint pid);
        string exe = "winamp.exe";
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            exe = p.ProcessName + ".exe";
        }
        catch (ArgumentException) { } // exited meanwhile

        int pos = unchecked((int)posMs); // -1 when stopped
        return new WinampTrack(ParseDisplay(text.ToString()), (WinampStatus)status,
            TimeSpan.FromMilliseconds(Math.Max(0, pos)), TimeSpan.FromSeconds(Math.Max(0, unchecked((int)lenS))), exe);
    }

    /// <summary>"5132. Turnstile - Never Enough - Winamp [Stopped]" → "Turnstile - Never Enough".</summary>
    public static string ParseDisplay(string windowText)
    {
        var m = TitleRegex().Match(windowText.Trim());
        return m.Success ? m.Groups["t"].Value.Trim() : windowText.Trim();
    }

    /// <summary>Splits "Artist - Title" at the first " - " (no separator: it's all title).</summary>
    public static (string Artist, string Title) SplitDisplay(string display)
    {
        int i = display.IndexOf(" - ", StringComparison.Ordinal);
        return i > 0 ? (display[..i].Trim(), display[(i + 3)..].Trim()) : ("", display.Trim());
    }

    public static bool IsWinampApp(string? appId) =>
        appId is not null && (appId.EndsWith("wacup.exe", StringComparison.OrdinalIgnoreCase) || appId.EndsWith("winamp.exe", StringComparison.OrdinalIgnoreCase));

    private static bool Ask(IntPtr hwnd, nint wParam, nint ipc, out long result)
    {
        IntPtr ok = SendMessageTimeoutW(hwnd, WM_USER, wParam, ipc, SMTO_ABORTIFHUNG, 500, out nint r);
        result = r;
        return ok != IntPtr.Zero;
    }

    [GeneratedRegex(@"^(?:\d+\.\s*)?(?<t>.*?)(?:\s+-\s+(?:Winamp|WACUP))?(?:\s*\[[^\]]*\])?$", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowW(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);
}
