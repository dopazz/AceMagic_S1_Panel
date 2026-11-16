using System.Drawing.Drawing2D;

namespace S1Panel.Tray;

/// <summary>
/// The tray icon, drawn in code: a little landscape "screen" with a status-coloured bar.
/// (The same artwork is saved as s1panel.ico for the exe by <see cref="WriteIcoFile"/>.)
/// </summary>
internal static class TrayIcons
{
    public static readonly Color Ok = Color.FromArgb(0x2E, 0xD3, 0xF5);
    public static readonly Color Paused = Color.FromArgb(0x93, 0x9E, 0xB4);
    public static readonly Color Problem = Color.FromArgb(0xFF, 0x9F, 0x43);

    public static Bitmap Draw(int size, Color accent)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float s = size / 32f;
        var body = new RectangleF(1.5f * s, 6 * s, 29 * s, 20 * s);
        using (var path = RoundRect(body, 4 * s))
        {
            using var fill = new SolidBrush(Color.FromArgb(0x1B, 0x22, 0x30));
            g.FillPath(fill, path);
            using var edge = new Pen(Color.FromArgb(0xC8, 0xD0, 0xDE), Math.Max(1, 1.6f * s));
            g.DrawPath(edge, path);
        }

        using var a = new SolidBrush(accent);
        // a "clock" block and a bar, echoing the panel layouts
        g.FillRectangle(a, 6 * s, 11 * s, 9 * s, 6 * s);
        g.FillRectangle(a, 6 * s, 20 * s, 20 * s, 2.5f * s);
        using var dim = new SolidBrush(Color.FromArgb(0x60, accent));
        g.FillRectangle(dim, 18 * s, 11 * s, 8 * s, 6 * s);
        return bmp;
    }

    public static Icon Create(Color accent)
    {
        using var bmp = Draw(32, accent);
        IntPtr h = bmp.GetHicon();
        using var temp = Icon.FromHandle(h);
        var icon = (Icon)temp.Clone();
        DestroyIcon(h);
        return icon;
    }

    /// <summary>Writes a multi-size .ico (PNG-compressed entries) — used to generate s1panel.ico for the exe.</summary>
    public static void WriteIcoFile(string path)
    {
        int[] sizes = [16, 24, 32, 48, 64, 256];
        var images = sizes.Select(sz =>
        {
            using var b = Draw(sz, Ok);
            using var ms = new MemoryStream();
            b.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(images[i].Length); w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var img in images)
            w.Write(img);
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
