using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static S1Panel.Core.Hid.HidNative;

namespace S1Panel.Core.Hid;

/// <summary>Something that accepts complete HID output reports (report ID byte first).</summary>
public interface IReportWriter : IDisposable
{
    /// <summary>Exact size of one output report including the leading report ID byte.</summary>
    int OutputReportLength { get; }

    void Write(ReadOnlySpan<byte> report);
}

public sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort Version,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    string? Product)
{
    public override string ToString() =>
        $"{VendorId:X4}:{ProductId:X4} rev {Version:X4} usage {UsagePage:X4}/{Usage:X4} in={InputReportLength} out={OutputReportLength} \"{Product}\"\n    {Path}";
}

/// <summary>A HID top-level collection opened for writing via plain Win32 (no admin, no driver install).</summary>
public sealed unsafe class HidDevice : IReportWriter
{
    private readonly SafeFileHandle _handle;
    private readonly ManualResetEvent _ioDone = new(false);

    public HidDeviceInfo Info { get; }
    public int OutputReportLength => Info.OutputReportLength;
    public int WriteTimeoutMs { get; set; } = 2000;

    private HidDevice(SafeFileHandle handle, HidDeviceInfo info)
    {
        _handle = handle;
        Info = info;
    }

    /// <summary>Lists present HID collections, optionally filtered by VID/PID.</summary>
    public static List<HidDeviceInfo> Enumerate(ushort? vendorId = null, ushort? productId = null)
    {
        var result = new List<HidDeviceInfo>();
        HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data))
                    break; // ERROR_NO_MORE_ITEMS

                string? path = GetInterfacePath(set, ref data);
                if (path is null)
                    continue;

                var info = Query(path);
                if (info is null)
                    continue;
                if (vendorId.HasValue && info.VendorId != vendorId.Value)
                    continue;
                if (productId.HasValue && info.ProductId != productId.Value)
                    continue;
                result.Add(info);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    public static HidDevice Open(HidDeviceInfo info)
    {
        var handle = CreateFileW(info.Path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
            OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            throw new IOException($"Could not open HID device for writing (Win32 error {err}: {new Win32Exception(err).Message}). " +
                                  "If the AceMagic panel software is running, close it first.\n  " + info.Path);
        }

        return new HidDevice(handle, info);
    }

    /// <summary>Writes one output report and waits for completion (with timeout).</summary>
    public void Write(ReadOnlySpan<byte> report)
    {
        if (report.Length != OutputReportLength)
            throw new ArgumentException($"Report must be exactly {OutputReportLength} bytes (got {report.Length}).");

        _ioDone.Reset();
        var ov = new NativeOverlapped { EventHandle = _ioDone.SafeWaitHandle.DangerousGetHandle() };

        fixed (byte* p = report)
        {
            if (!WriteFile(_handle, p, (uint)report.Length, IntPtr.Zero, &ov))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_IO_PENDING)
                    throw new IOException($"HID write failed (Win32 error {err}: {new Win32Exception(err).Message}).");

                if (!_ioDone.WaitOne(WriteTimeoutMs))
                {
                    CancelIoEx(_handle, &ov);
                    GetOverlappedResult(_handle, &ov, out _, true); // must finish before buffer is unpinned
                    throw new TimeoutException($"HID write timed out after {WriteTimeoutMs} ms.");
                }
            }

            if (!GetOverlappedResult(_handle, &ov, out uint written, true))
            {
                int err = Marshal.GetLastWin32Error();
                throw new IOException($"HID write failed (Win32 error {err}: {new Win32Exception(err).Message}).");
            }

            if (written != report.Length)
                throw new IOException($"Short HID write: {written} of {report.Length} bytes.");
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
        _ioDone.Dispose();
    }

    private static string? GetInterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data)
    {
        SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
        if (size == 0)
            return null;

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on 64-bit, 6 on 32-bit.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, buffer, size, out _, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Opens with zero access (query only), which works even for collections Windows holds exclusively.</summary>
    private static HidDeviceInfo? Query(string path)
    {
        using var h = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid)
            return null;

        var attr = new HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(h, ref attr))
            return null;

        HIDP_CAPS caps = default;
        if (HidD_GetPreparsedData(h, out IntPtr pp))
        {
            try { HidP_GetCaps(pp, out caps); }
            finally { HidD_FreePreparsedData(pp); }
        }

        string? product = null;
        char* buf = stackalloc char[128];
        if (HidD_GetProductString(h, buf, 256))
            product = new string(buf);

        return new HidDeviceInfo(path, attr.VendorID, attr.ProductID, attr.VersionNumber, caps.UsagePage, caps.Usage,
            caps.InputReportByteLength, caps.OutputReportByteLength, product);
    }
}
