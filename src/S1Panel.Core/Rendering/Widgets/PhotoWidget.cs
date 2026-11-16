using S1Panel.Core.Config;
using S1Panel.Core.Rendering.Layouts;
using SkiaSharp;

namespace S1Panel.Core.Rendering.Widgets;

/// <summary>
/// Slideshow of the pictures in a folder (and its subfolders). Which photo shows is derived from the clock
/// (one per interval), so it doesn't reset when settings change. Files are listed and decoded in the background,
/// decoded at reduced size (fast even for big camera JPEGs), and turned upright using their EXIF orientation.
/// Each change costs ~1 s of panel bandwidth, hence the relaxed default interval.
/// </summary>
public sealed class PhotoWidget : IWidget, IPendingWork
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    private readonly object _gate = new();
    private string? _folder;
    private List<string> _files = [];
    private DateTime _listedUtc;
    private bool _listing, _loading;
    private string? _problem;
    private long _shownSlot = -1, _loadingSlot = -1;
    private SKBitmap? _current;
    private string? _currentName;

    public string Name => "photos";
    public string Description => "Slideshow of pictures from a folder";

    public bool Pending
    {
        get { lock (_gate) return _listing || _loading || (_current is null && _files.Count > 0); }
    }

    public void Draw(SKCanvas c, SKRect area, LayoutContext ctx)
    {
        var settings = (ctx.Env ?? WidgetEnvironment.Default).Slideshow;
        string folder = settings.ResolvedFolder;
        EnsureListing(folder);

        List<string> files;
        lock (_gate) files = _files;
        if (files.Count == 0)
        {
            string why;
            lock (_gate) why = _listing ? "Looking for photos…" : _problem ?? $"No pictures in\n{ShortPath(folder)}";
            WeatherWidget.Message(c, area, why);
            return;
        }

        long slot = (long)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / settings.IntervalSeconds);
        int index = settings.Shuffle ? ShuffledIndex(slot, files.Count) : (int)(slot % files.Count);
        EnsureLoaded(slot, files[index], area);

        SKBitmap? img;
        lock (_gate) img = _current;
        if (img is null)
        {
            WeatherWidget.Message(c, area, "Loading photo…");
            return;
        }

        float scale = settings.Fit == "contain"
            ? Math.Min(area.Width / img.Width, area.Height / img.Height)
            : Math.Max(area.Width / img.Width, area.Height / img.Height);
        float dw = img.Width * scale, dh = img.Height * scale;
        var dest = SKRect.Create(area.MidX - dw / 2, area.MidY - dh / 2, dw, dh);
        using var image = SKImage.FromBitmap(img);
        c.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    /// <summary>Visits every photo once per cycle, in a different order each cycle (deterministic from the slot).</summary>
    private static int ShuffledIndex(long slot, int count)
    {
        long cycle = slot / count;
        int pos = (int)(slot % count);
        var order = Enumerable.Range(0, count).ToArray();
        new Random(unchecked((int)(cycle * 2654435761L))).Shuffle(order);
        return order[pos];
    }

    private void EnsureListing(string folder)
    {
        lock (_gate)
        {
            bool changed = !string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase);
            if (_listing || (!changed && DateTime.UtcNow - _listedUtc < TimeSpan.FromMinutes(10)))
                return;
            if (changed)
            {
                _folder = folder;
                _files = [];
                _shownSlot = -1;
            }
            _listing = true;
        }
        Task.Run(() =>
        {
            List<string> found = [];
            string? problem = null;
            try
            {
                if (!Directory.Exists(folder))
                    problem = $"Folder not found:\n{ShortPath(folder)}";
                else
                    found = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                        .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problem = $"Can't read folder:\n{ShortPath(folder)}";
            }
            lock (_gate)
            {
                if (string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase))
                {
                    _files = found;
                    _problem = problem;
                    _listedUtc = DateTime.UtcNow;
                }
                _listing = false;
            }
        });
    }

    private void EnsureLoaded(long slot, string file, SKRect area)
    {
        lock (_gate)
        {
            if (slot == _shownSlot || slot == _loadingSlot || _loading)
                return;
            if (file == _currentName) // same photo picked again (e.g. a one-photo folder)
            {
                _shownSlot = slot;
                return;
            }
            _loading = true;
            _loadingSlot = slot;
        }
        int target = (int)(Math.Max(area.Width, area.Height) * 2);
        Task.Run(() =>
        {
            SKBitmap? bmp = null;
            try { bmp = DecodeUpright(file, target); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            lock (_gate)
            {
                if (bmp is not null)
                {
                    _current?.Dispose();
                    _current = bmp;
                    _currentName = file;
                }
                _shownSlot = slot; // even on failure: move on next interval instead of retrying a broken file
                _loading = false;
                _loadingSlot = -1;
            }
        });
    }

    /// <summary>Decodes at roughly <paramref name="target"/> pixels on the long side, applying EXIF rotation.</summary>
    internal static SKBitmap? DecodeUpright(string path, int target)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null)
            return null;
        var full = codec.Info;
        float scale = Math.Min(1f, target / (float)Math.Max(full.Width, full.Height));
        var size = codec.GetScaledDimensions(scale);
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var decoded = SKBitmap.Decode(codec, info);
        if (decoded is null)
            return null;

        int degrees = codec.EncodedOrigin switch
        {
            SKEncodedOrigin.RightTop => 90,
            SKEncodedOrigin.BottomRight => 180,
            SKEncodedOrigin.LeftBottom => 270,
            _ => 0,
        };
        if (degrees == 0)
            return decoded;

        bool swap = degrees != 180;
        var rotated = new SKBitmap(new SKImageInfo(swap ? decoded.Height : decoded.Width, swap ? decoded.Width : decoded.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(rotated))
        {
            c.Translate(rotated.Width / 2f, rotated.Height / 2f);
            c.RotateDegrees(degrees);
            c.Translate(-decoded.Width / 2f, -decoded.Height / 2f);
            c.DrawBitmap(decoded, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), null);
        }
        decoded.Dispose();
        return rotated;
    }

    private static string ShortPath(string p) => p.Length <= 28 ? p : "…" + p[^27..];
}
