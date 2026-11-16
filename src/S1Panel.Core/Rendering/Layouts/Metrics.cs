using S1Panel.Core.Sensors;
using SkiaSharp;

namespace S1Panel.Core.Rendering.Layouts;

/// <summary>One displayable reading: label, formatted value, unit, and a 0..1 fill for gauges.</summary>
public readonly record struct Metric(string Label, string Value, string Unit, double Fraction, SKColor Color)
{
    public string ValueWithUnit => Unit.Length == 0 ? Value : Unit is "%" or "°C" or "°F" ? Value + Unit : $"{Value} {Unit}";
}

/// <summary>
/// The readings available without admin rights. These stand in for the stock app's slots:
/// CPU temp -> SSD temp, fan speed -> disk used, CPU power -> CPU load (big gauges) / network (small slots).
/// </summary>
public static class Metrics
{
    public static readonly SKColor Cyan = new(0x2E, 0xD3, 0xF5);
    public static readonly SKColor Orange = new(0xFF, 0x9F, 0x43);
    public static readonly SKColor Green = new(0x7B, 0xE0, 0x5A);
    public static readonly SKColor Blue = new(0x3D, 0x9B, 0xFF);
    public static readonly SKColor Magenta = new(0xE0, 0x4C, 0xD8);
    public static readonly SKColor Amber = new(0xFF, 0xC8, 0x3D);

    public static Metric CpuLoad(StatsSnapshot s) => new("CPU Load", $"{s.CpuPercent:0}", "%", s.CpuPercent / 100, Blue);

    /// <summary>Used / installed memory, e.g. "7.9" + "/ 16 GB" (total from the installed RAM, not hard-coded).</summary>
    public static Metric MemUsage(StatsSnapshot s)
    {
        const double GiB = 1024.0 * 1024 * 1024;
        double total = s.MemoryInstalledBytes / GiB;
        string totalText = Math.Abs(total - Math.Round(total)) < 0.05 ? $"{total:0}" : $"{total:0.0}";
        return new("Mem Usage", $"{s.MemoryUsedBytes / GiB:0.0}", $"/ {totalText} GB", s.MemoryPercent / 100, Magenta);
    }

    public static Metric DiskUsed(StatsSnapshot s) => new("Disk Used", $"{s.DiskPercent:0}", "%", s.DiskPercent / 100, Amber);

    /// <summary>NVMe composite temperature in °C or °F; gauge runs 20..85 °C (typical warning threshold).</summary>
    public static Metric Temp(StatsSnapshot s, bool fahrenheit) => s.SsdTempC is { } t
        ? new("Temp", fahrenheit ? $"{t * 9 / 5 + 32:0}" : $"{t:0}", fahrenheit ? "°F" : "°C", Math.Clamp((t - 20) / 65, 0, 1), Orange)
        : new("Temp", "--", "", 0, Orange);

    /// <summary>Download rate; gauge on a log scale where 1 Gb/s is full.</summary>
    public static Metric NetDown(StatsSnapshot s)
    {
        var (value, unit) = Rate(s.NetDownBytesPerSec);
        double bits = s.NetDownBytesPerSec * 8;
        return new("Net ↓", value, unit, Math.Clamp(Math.Log10(1 + bits) / 9, 0, 1), Green);
    }

    public static Metric NetUp(StatsSnapshot s)
    {
        var (value, unit) = Rate(s.NetUpBytesPerSec);
        double bits = s.NetUpBytesPerSec * 8;
        return new("Net ↑", value, unit, Math.Clamp(Math.Log10(1 + bits) / 9, 0, 1), Green);
    }

    /// <summary>Bits per second as a short value + unit, at most 4 characters of number.</summary>
    public static (string Value, string Unit) Rate(double bytesPerSec)
    {
        double b = bytesPerSec * 8;
        return b switch
        {
            >= 1e9 => ($"{b / 1e9:0.0}", "Gb/s"),
            >= 1e8 => ($"{b / 1e6:0}", "Mb/s"),
            >= 1e6 => ($"{b / 1e6:0.0}", "Mb/s"),
            >= 1e5 => ($"{b / 1e3:0}", "kb/s"),
            >= 1e3 => ($"{b / 1e3:0.0}", "kb/s"),
            _ => ($"{b:0}", "b/s"),
        };
    }
}
