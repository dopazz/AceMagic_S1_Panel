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

using System.IO.Ports;
using Microsoft.Win32;

namespace S1Panel.Core.Led;

public enum LedMode : byte
{
    Rainbow = 0x01,
    Breathing = 0x02,
    ColorCycle = 0x03,
    Off = 0x04,
    Auto = 0x05,
}

/// <summary>
/// The S1 LED strip, driven through a CH340 USB-serial adapter (VID 1A86) at 10000 baud.
/// Command: FA mode intensity speed checksum. On the wire, 1 = strongest/fastest and 5 = weakest/slowest;
/// this class takes friendly levels where 5 = brightest/fastest.
/// </summary>
public static class LedStrip
{
    public const int BaudRate = 10000;
    private const byte Signature = 0xFA;

    /// <summary>Builds the 5-byte command. <paramref name="intensity"/> and <paramref name="speed"/> are 1 (low) .. 5 (high).</summary>
    public static byte[] BuildCommand(LedMode mode, int intensity, int speed)
    {
        byte i = mode == LedMode.Off ? (byte)5 : ToWire(intensity);
        byte s = mode == LedMode.Off ? (byte)5 : ToWire(speed);
        byte[] cmd = [Signature, (byte)mode, i, s, 0];
        cmd[4] = (byte)(cmd[0] + cmd[1] + cmd[2] + cmd[3]);
        return cmd;
    }

    public static void Set(string portName, LedMode mode, int intensity = 3, int speed = 3) =>
        Send(portName, BuildCommand(mode, intensity, speed));

    /// <summary>Builds a command from raw wire bytes (no range checks) — for probing undocumented modes.</summary>
    public static byte[] BuildRaw(byte mode, byte intensity, byte speed)
    {
        byte[] cmd = [Signature, mode, intensity, speed, 0];
        cmd[4] = (byte)(cmd[0] + cmd[1] + cmd[2] + cmd[3]);
        return cmd;
    }

    public static void Send(string portName, byte[] cmd)
    {
        using var port = new SerialPort(portName, BaudRate) { WriteTimeout = 1000 };
        port.Open();
        // The controller wants the bytes paced out individually.
        foreach (byte b in cmd)
        {
            port.Write([b], 0, 1);
            Thread.Sleep(5);
        }
    }

    /// <summary>Finds COM ports belonging to present CH340 adapters by reading the (user-readable) device registry.</summary>
    public static List<string> FindPorts()
    {
        var present = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
        var found = new List<string>();
        using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (usb is null)
            return found;

        foreach (string dev in usb.GetSubKeyNames().Where(n => n.StartsWith("VID_1A86", StringComparison.OrdinalIgnoreCase)))
        {
            using var devKey = usb.OpenSubKey(dev);
            if (devKey is null)
                continue;
            foreach (string inst in devKey.GetSubKeyNames())
            {
                using var p = devKey.OpenSubKey($@"{inst}\Device Parameters");
                if (p?.GetValue("PortName") is string port && present.Contains(port) && !found.Contains(port))
                    found.Add(port);
            }
        }
        return found;
    }

    public static LedMode ParseMode(string name) => name.Trim().ToLowerInvariant() switch
    {
        "rainbow" => LedMode.Rainbow,
        "breathing" or "breathe" => LedMode.Breathing,
        "cycle" or "colors" or "colours" or "colour" or "color" => LedMode.ColorCycle,
        "off" => LedMode.Off,
        "auto" or "automatic" => LedMode.Auto,
        _ => throw new ArgumentException($"Unknown LED mode '{name}'. Use rainbow, breathing, cycle, off or auto."),
    };

    private static byte ToWire(int level) => (byte)(6 - Math.Clamp(level, 1, 5));
}
