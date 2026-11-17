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

using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace S1Panel.Core.Sensors;

public sealed record StatsSnapshot(
    DateTime Time,
    double CpuPercent,
    double MemoryPercent,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    double DiskPercent,
    long DiskFreeBytes,
    double NetDownBytesPerSec,
    double NetUpBytesPerSec,
    TimeSpan Uptime,
    double? SsdTempC,
    ulong MemoryInstalledBytes);

/// <summary>
/// System readings that work as a normal (non-admin) user. CPU temperature is deliberately absent:
/// on Windows it needs a kernel driver (admin), which is exactly what this project avoids.
/// The SSD (NVMe) temperature is used instead where the drive supports it.
/// </summary>
public sealed class SystemStats
{
    private readonly string _drive;
    private ulong _lastIdle, _lastKernel, _lastUser;
    private long _lastRx, _lastTx;
    private DateTime _lastSample;
    private readonly NvmeTemperature? _nvme = NvmeTemperature.Open();
    private double? _ssdTemp;
    private DateTime _ssdTempAt = DateTime.MinValue;

    /// <summary>Installed RAM as the firmware reports it (e.g. 16 GB), rather than the slightly smaller usable amount.</summary>
    private static readonly ulong? InstalledMemory = GetPhysicallyInstalledSystemMemory(out ulong kb) && kb > 0 ? kb * 1024 : null;

    public SystemStats(string drive = "C:\\")
    {
        _drive = drive;
        GetSystemTimes(out _lastIdle, out _lastKernel, out _lastUser);
        (_lastRx, _lastTx) = NetTotals();
        _lastSample = DateTime.UtcNow;
    }

    public StatsSnapshot Sample()
    {
        var nowUtc = DateTime.UtcNow;
        double secs = Math.Max(0.001, (nowUtc - _lastSample).TotalSeconds);
        _lastSample = nowUtc;

        // CPU: kernel time includes idle time.
        GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        ulong dIdle = idle - _lastIdle, dTotal = (kernel - _lastKernel) + (user - _lastUser);
        (_lastIdle, _lastKernel, _lastUser) = (idle, kernel, user);
        double cpu = dTotal == 0 ? 0 : 100.0 * (dTotal - dIdle) / dTotal;

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
        ulong used = mem.ullTotalPhys - mem.ullAvailPhys;

        double diskPct = 0;
        long diskFree = 0;
        try
        {
            var d = new DriveInfo(_drive);
            diskFree = d.AvailableFreeSpace;
            diskPct = d.TotalSize == 0 ? 0 : 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize;
        }
        catch (IOException) { }

        var (rx, tx) = NetTotals();
        double down = Math.Max(0, rx - _lastRx) / secs, up = Math.Max(0, tx - _lastTx) / secs;
        (_lastRx, _lastTx) = (rx, tx);

        // Drive temperature changes slowly; don't query the SSD more than every 5 s.
        if (_nvme is not null && nowUtc - _ssdTempAt > TimeSpan.FromSeconds(5))
        {
            _ssdTemp = _nvme.ReadCelsius();
            _ssdTempAt = nowUtc;
        }

        return new StatsSnapshot(DateTime.Now, Math.Clamp(cpu, 0, 100), mem.dwMemoryLoad, used, mem.ullTotalPhys,
            diskPct, diskFree, down, up, TimeSpan.FromMilliseconds(Environment.TickCount64), _ssdTemp,
            InstalledMemory ?? mem.ullTotalPhys);
    }

    private static (long Rx, long Tx) NetTotals()
    {
        long rx = 0, tx = 0;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            try
            {
                var s = ni.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
            catch (NetworkInformationException) { }
        }
        return (rx, tx);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);
}
