using S1Panel.Core.Config;
using S1Panel.Core.Lcd;
using S1Panel.Core.Rendering.Layouts;
using S1Panel.Core.Rendering.Widgets;
using S1Panel.Core.Sensors;
using SkiaSharp;

namespace S1Panel.Core.Monitor;

/// <summary>
/// Turns settings + live sensor readings into frames. One instance per settings version; owns its own layout
/// instance, so several composers (the panel engine and a settings-window preview) can run side by side.
/// </summary>
public sealed class FrameComposer : IDisposable
{
    private readonly SystemStats _stats;
    private readonly List<double> _history = [];
    private readonly SKCanvas _canvas;
    private readonly SKBitmap? _background;
    private readonly WidgetEnvironment _env;

    public Orientation Orientation { get; }
    public ILayout Layout { get; }
    public bool Clock12 { get; }
    public bool Fahrenheit { get; }
    public bool Blink { get; }
    public float TextScale { get; }

    /// <summary>The frame, in logical orientation, after <see cref="ComposeNext"/>.</summary>
    public SKBitmap Bitmap { get; }

    public StatsSnapshot? LastStats { get; private set; }

    /// <summary>True while a widget is still loading its data (location, weather, photo) in the background.</summary>
    public bool HasPendingWork => Layout is SplitLayout split && split.Widgets.OfType<IPendingWork>().Any(w => w.Pending);

    public FrameComposer(PanelSettings settings)
    {
        settings.Validate();
        Orientation = OrientationExtensions.Parse(settings.Orientation);
        Layout = LayoutRegistry.Create(settings.Layout);
        Clock12 = PanelSettings.ParseClock12(settings.Clock);
        Fahrenheit = PanelSettings.ParseFahrenheit(settings.Units);
        Blink = settings.Blink;
        TextScale = LayoutExtensions.ParseTextSize(settings.TextSize);

        _env = new WidgetEnvironment(settings.Location, settings.Slideshow);
        string drive = settings.Drive.EndsWith('\\') ? settings.Drive : settings.Drive + "\\";
        _stats = new SystemStats(drive);

        if (Layout is SplitLayout split)
            split.SetWidgets(settings.Widgets);

        if (Layout is SceneLayout scene)
        {
            if (settings.Background is { } file)
                _background = SKBitmap.Decode(file) ?? throw new ArgumentException($"Could not read image '{file}'.");
            scene.Image = _background;
            scene.Season = SceneArt.Parse(settings.Scene);
        }

        Bitmap = FrameConverter.CreateCanvasBitmap(Orientation);
        _canvas = new SKCanvas(Bitmap);
    }

    /// <summary>Takes a few quick samples so graphs and rates have data (used for one-off previews).</summary>
    public void WarmUp(int samples = 20, int delayMs = 100)
    {
        for (int i = 0; i < samples; i++)
        {
            Thread.Sleep(delayMs);
            _history.Add(_stats.Sample().CpuPercent);
        }
    }

    /// <summary>Samples the sensors and draws a new frame into <see cref="Bitmap"/>.</summary>
    public void ComposeNext()
    {
        var s = _stats.Sample();
        _history.Add(s.CpuPercent);
        if (_history.Count > 60)
            _history.RemoveAt(0);

        Layout.Render(_canvas, new LayoutContext(Bitmap.Width, Bitmap.Height, s, _history, Clock12, Fahrenheit, Blink, TextScale, _env));
        _canvas.Flush();
        LastStats = s;
    }

    /// <summary>What the panel would actually show for the current frame (RGB565, dithered), as a new bitmap.</summary>
    public SKBitmap RenderAsPanelShows()
    {
        var frame = new byte[LcdPanel.FrameBytes];
        FrameConverter.ToNative(Bitmap, Orientation, frame);
        return FrameConverter.FromNative(frame, Orientation);
    }

    public void Dispose()
    {
        if (Layout is SceneLayout scene)
            scene.Image = null;
        _canvas.Dispose();
        Bitmap.Dispose();
        _background?.Dispose();
    }
}
