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
using Microsoft.Win32.SafeHandles;

namespace S1Panel.Core.Sensors;

/// <summary>
/// Reads NVMe drive temperature from the SMART/health log (page 2) with IOCTL_STORAGE_QUERY_PROPERTY
/// (StorageDeviceProtocolSpecificProperty). The physical drive is opened with ZERO access rights, which
/// any user may do, so no administrator rights are needed. Verified on the S1's BIWIN NVMe;
/// drives whose driver doesn't support the query are skipped.
/// </summary>
public sealed class NvmeTemperature
{
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int ProtocolTypeNvme = 3;
    private const int NVMeDataTypeLogPage = 2;
    private const int SmartHealthLogPage = 2;
    private const int SpecSize = 40;   // STORAGE_PROTOCOL_SPECIFIC_DATA
    private const int LogSize = 512;

    private readonly int _disk;

    private NvmeTemperature(int disk) => _disk = disk;

    /// <summary>Index of the \\.\PhysicalDriveN being read.</summary>
    public int Disk => _disk;

    /// <summary>Finds the first drive (or a specific one) whose temperature can be read; null if none.</summary>
    public static NvmeTemperature? Open(int? disk = null)
    {
        foreach (int d in disk is { } only ? [only] : Enumerable.Range(0, 8))
            if (Read(d) is not null)
                return new NvmeTemperature(d);
        return null;
    }

    /// <summary>Composite temperature in °C, or null if the read failed.</summary>
    public double? ReadCelsius() => Read(_disk);

    private static double? Read(int disk)
    {
        using SafeFileHandle h = CreateFileW($@"\\.\PhysicalDrive{disk}", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid)
            return null;

        var buf = new byte[8 + SpecSize + LogSize];
        BitConverter.TryWriteBytes(buf.AsSpan(0), StorageDeviceProtocolSpecificProperty);
        // QueryType (offset 4) = PropertyStandardQuery = 0
        BitConverter.TryWriteBytes(buf.AsSpan(8), ProtocolTypeNvme);
        BitConverter.TryWriteBytes(buf.AsSpan(12), NVMeDataTypeLogPage);
        BitConverter.TryWriteBytes(buf.AsSpan(16), SmartHealthLogPage);
        BitConverter.TryWriteBytes(buf.AsSpan(24), SpecSize); // ProtocolDataOffset
        BitConverter.TryWriteBytes(buf.AsSpan(28), LogSize);  // ProtocolDataLength

        if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, buf, buf.Length, buf, buf.Length, out _, IntPtr.Zero))
            return null;

        // Output: STORAGE_PROTOCOL_DATA_DESCRIPTOR { Version, Size, STORAGE_PROTOCOL_SPECIFIC_DATA }.
        int dataOffset = 8 + BitConverter.ToInt32(buf, 8 + 16);
        if (dataOffset + 3 > buf.Length)
            return null;
        int kelvin = BitConverter.ToUInt16(buf, dataOffset + 1);
        return kelvin is > 200 and < 400 ? kelvin - 273.15 : null;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inLen, byte[] outBuf, int outLen, out int returned, IntPtr overlapped);
}
