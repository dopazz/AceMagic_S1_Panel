using System.Buffers.Binary;
using S1Panel.Core.Hid;

namespace S1Panel.Core.Lcd;

/// <summary>
/// Low-level protocol for the S1 front LCD (Holtek HT32 controller, USB HID VID 04D9 / PID FD01).
/// Every report is: report ID (0) + 8-byte header + 4096-byte payload.
/// Pixel data is always the native 320x170 landscape framebuffer in big-endian RGB565.
/// </summary>
public sealed class LcdPanel : IDisposable
{
    public const ushort VendorId = 0x04D9;
    public const ushort ProductId = 0xFD01;

    public const int Width = 320;
    public const int Height = 170;
    public const int FrameBytes = Width * Height * 2; // 108800
    public const int HeaderSize = 8;
    public const int PayloadSize = 4096;
    public const int ReportSize = 1 + HeaderSize + PayloadSize; // 4105

    private const byte Signature = 0x55;
    private const byte CmdConfig = 0xA1;
    private const byte CmdUpdate = 0xA2;
    private const byte CmdRedraw = 0xA3;
    private const byte SubOrientation = 0xF1;
    private const byte SubHeartbeat = 0xF2;
    private const byte RedrawFirst = 0xF0;
    private const byte RedrawMiddle = 0xF1;
    private const byte RedrawLast = 0xF2;

    private readonly IReportWriter _writer;
    private readonly byte[] _report;

    public LcdPanel(IReportWriter writer)
    {
        if (writer.OutputReportLength < ReportSize)
            throw new ArgumentException($"Device output report is {writer.OutputReportLength} bytes; expected at least {ReportSize}. Wrong HID interface?");
        _writer = writer;
        _report = new byte[writer.OutputReportLength];
    }

    /// <summary>Finds the panel's HID interface (the vendor-defined collection with the 4 KB output report).</summary>
    public static HidDeviceInfo? Find()
    {
        return HidDevice.Enumerate(VendorId, ProductId)
            .Where(d => d.OutputReportLength >= ReportSize)
            .OrderByDescending(d => d.UsagePage >= 0xFF00)
            .FirstOrDefault();
    }

    /// <summary>Opens the panel. Pass a device path to override auto-detection.</summary>
    public static LcdPanel Open(string? devicePath = null)
    {
        HidDeviceInfo? info = devicePath is null
            ? Find()
            : HidDevice.Enumerate(VendorId, ProductId).FirstOrDefault(d => string.Equals(d.Path, devicePath, StringComparison.OrdinalIgnoreCase))
              ?? HidDevice.Enumerate().FirstOrDefault(d => string.Equals(d.Path, devicePath, StringComparison.OrdinalIgnoreCase));

        if (info is null)
            throw new IOException("AceMagic S1 LCD not found (USB HID 04D9:FD01). Run 'list' to see what is attached.");

        return new LcdPanel(HidDevice.Open(info));
    }

    /// <summary>Tells the firmware which way up its own built-in messages should be drawn.</summary>
    public void SetOrientation(bool portrait)
    {
        Span<byte> h = BeginReport();
        h[1] = CmdConfig;
        h[2] = SubOrientation;
        h[3] = portrait ? (byte)0x02 : (byte)0x01;
        Send();
    }

    /// <summary>Keep-alive that also sets the device clock. Without it the panel shows a "disconnected" notice.</summary>
    public void Heartbeat(DateTime now)
    {
        Span<byte> h = BeginReport();
        h[1] = CmdConfig;
        h[2] = SubHeartbeat;
        h[3] = (byte)now.Hour;
        h[4] = (byte)now.Minute;
        h[5] = (byte)now.Second;
        Send();
    }

    /// <summary>Sends a whole native frame (320x170 BE RGB565, 108800 bytes) in 27 chunks.</summary>
    public void Redraw(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != FrameBytes)
            throw new ArgumentException($"Frame must be {FrameBytes} bytes.");

        int chunks = (FrameBytes + PayloadSize - 1) / PayloadSize; // 27
        for (int i = 0; i < chunks; i++)
        {
            int offset = i * PayloadSize;
            int length = Math.Min(PayloadSize, FrameBytes - offset);

            Span<byte> h = BeginReport();
            h[1] = CmdRedraw;
            h[2] = i == 0 ? RedrawFirst : i == chunks - 1 ? RedrawLast : RedrawMiddle;
            h[3] = (byte)(i + 1);
            BinaryPrimitives.WriteUInt16LittleEndian(h[4..], (ushort)offset); // wraps; firmware uses the sequence number
            BinaryPrimitives.WriteUInt16LittleEndian(h[6..], (ushort)length);
            frame.Slice(offset, length).CopyTo(h[HeaderSize..]);
            Send();
        }
    }

    /// <summary>
    /// Blits a rectangle into the native framebuffer. <paramref name="pixels"/> is w*h BE RGB565, row-major, at most 4096 bytes.
    /// </summary>
    public void Update(int x, int y, int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (width is < 1 or > 255 || height is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(width), "Width/height must be 1..255.");
        if (x < 0 || y < 0 || x + width > Width || y + height > Height)
            throw new ArgumentOutOfRangeException(nameof(x), "Rectangle outside the 320x170 framebuffer.");
        int bytes = width * height * 2;
        if (bytes > PayloadSize)
            throw new ArgumentException($"Update rectangle is {bytes} bytes; max {PayloadSize}.");
        if (pixels.Length < bytes)
            throw new ArgumentException("Not enough pixel data for the rectangle.");

        Span<byte> h = BeginReport();
        h[1] = CmdUpdate;
        BinaryPrimitives.WriteUInt16LittleEndian(h[2..], (ushort)x);
        BinaryPrimitives.WriteUInt16LittleEndian(h[4..], (ushort)y);
        h[6] = (byte)width;
        h[7] = (byte)height;
        pixels[..bytes].CopyTo(h[HeaderSize..]);
        Send();
    }

    public void Dispose() => _writer.Dispose();

    /// <summary>Clears the report buffer and returns the span starting at the 8-byte header (after the report ID).</summary>
    private Span<byte> BeginReport()
    {
        Array.Clear(_report);
        Span<byte> h = _report.AsSpan(1);
        h[0] = Signature;
        return h;
    }

    private void Send() => _writer.Write(_report);
}
