using S1Panel.Core.Config;
using S1Panel.Core.Hid;
using S1Panel.Core.Lcd;
using S1Panel.Core.Led;
using S1Panel.Core.Rendering;
using S1Panel.Core.Rendering.Layouts;
using S1Panel.Core.Sensors;
using SkiaSharp;

namespace S1Panel.Cli;

internal static class Program
{
    private const string Usage = """
        s1panel - control the AceMagic S1 front panel (LCD + LED strip). No admin rights needed.

        Usage:
          s1panel monitor [options]             Live stats screen, using your saved settings
          s1panel layouts                       List the available monitor layouts
          s1panel config                        Show the settings file location and current settings
          s1panel config init [--force]         Create a settings file with the defaults
          s1panel config set <key> <value>      Change one setting, e.g.  config set text-size larger
          s1panel test    [options]             Colour bars + orientation marker
          s1panel image   <file> [--contain]    Show an image (default: crop to fill)
          s1panel text    "<message>" [--fg #RRGGBB] [--bg #RRGGBB]   (use \n for new lines)
          s1panel led     [mode] [--intensity 1-5] [--speed 1-5] [--port COMx]
                          mode: rainbow | breathing | cycle | off | auto  (default: from settings)
          s1panel list                          Show detected panel / LED devices

        Monitor options (each overrides the settings file for this run):
          --layout <tiles|gauge|clock|scene|dashboard>
          --clock <12|24|auto>                  time format (auto = Windows setting)
          --units <metric|imperial|auto>        °C or °F (auto = Windows region)
          --text-size <normal|large|larger|1.2> bigger small text
          --blink / --no-blink                  blink the clock's colon or keep it steady
          --scene <autumn|winter|spring|summer|auto>  artwork for the scene layout (auto = by date)
          --interval <ms>   --drive <C:>   --background <image>   (background is for 'scene')
          --no-led                              don't apply the LED settings from the file
          --stats                               print tiles sent and time taken per frame

        LCD options (all screen commands):
          --orientation <landscape|portrait|landscape-flipped|portrait-flipped>
          --preview <out.png>     Render to a PNG (as the panel would show it) instead of the device
          --once                  Draw and exit instead of holding the screen (it will show "disconnected" later)
          --seconds <n>           Run for n seconds then exit (handy over ssh, where Ctrl+C isn't available)
          --device <path>         HID device path (default: auto-detect 04D9:FD01)
          --redraw-delay <ms>     Wait after a full redraw (default 1600)
          --update-gap <ms>       Wait between partial updates (default 4)
          --always-redraw         Send full frames instead of changed tiles
          --config <file>         Use this settings file instead of s1panel.json

        Settings are read from s1panel.json next to s1panel.exe (or %APPDATA%\s1panel\s1panel.json).
        LCD commands keep running (sending heartbeats) until you press Ctrl+C.
        """;

    private static int Main(string[] argv)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8; // so °C/°F print correctly, including over ssh
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            var args = new Args(argv);
            string? cmd = args.Positional(0)?.ToLowerInvariant();
            int seconds = args.IntOption("seconds", 0);
            if (seconds > 0)
                cts.CancelAfter(TimeSpan.FromSeconds(seconds));

            if (cmd == "config")
                return Config(args);
            if (cmd is null or "help" or "-h" or "--help" or "/?")
            {
                Console.WriteLine(Usage);
                return cmd is null ? 1 : 0;
            }

            var (settings, _) = SettingsStore.Load(args.Option("config"));

            switch (cmd)
            {
                case "list": return List();
                case "led": return Led(args, settings);
                case "led-probe": return LedProbe(args, settings, cts.Token);
                case "test":
                    return RunStatic(args, settings, cts.Token, (c, w, h) => Scenes.TestPattern(c, w, h));
                case "image":
                {
                    string file = args.Positional(1) ?? throw new ArgumentException("image: missing file name.");
                    using var img = SKBitmap.Decode(file) ?? throw new ArgumentException($"Could not read image '{file}'.");
                    bool contain = args.Flag("contain");
                    return RunStatic(args, settings, cts.Token, (c, w, h) => Scenes.Image(c, w, h, img, !contain));
                }
                case "text":
                {
                    string msg = args.Positional(1) ?? throw new ArgumentException("text: missing message.");
                    var fg = SKColor.Parse(args.Option("fg") ?? "#FFFFFF");
                    var bg = SKColor.Parse(args.Option("bg") ?? "#000000");
                    return RunStatic(args, settings, cts.Token, (c, w, h) => Scenes.Text(c, w, h, msg, fg, bg));
                }
                case "monitor": return Monitor(args, settings, cts.Token);
                case "layouts":
                    foreach (var l in LayoutRegistry.All)
                        Console.WriteLine($"  {(l.Name == settings.Layout ? "*" : " ")} {l.Name,-10} {l.Description}");
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown command '{cmd}'.\n");
                    Console.WriteLine(Usage);
                    return 1;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or TimeoutException or UnauthorizedAccessException or FormatException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int Config(Args args)
    {
        string? explicitPath = args.Option("config");
        switch (args.Positional(1)?.ToLowerInvariant())
        {
            case null or "show":
            {
                var (settings, path) = SettingsStore.Load(explicitPath);
                Console.WriteLine(path is null
                    ? $"No settings file - using defaults. 'config init' or 'config set' will create:\n  {SettingsStore.ExeDirPath}"
                    : $"Settings file: {path}");
                Console.WriteLine(SettingsStore.Serialize(settings));
                return 0;
            }
            case "path":
                Console.WriteLine(SettingsStore.FindExisting(explicitPath) ?? SettingsStore.ExeDirPath);
                return 0;
            case "init":
            {
                string? existing = SettingsStore.FindExisting(explicitPath);
                if (existing is not null && File.Exists(existing) && !args.Flag("force"))
                    throw new ArgumentException($"{existing} already exists (use --force to overwrite with defaults).");
                string written = SettingsStore.Save(new PanelSettings(), explicitPath ?? existing);
                Console.WriteLine($"Wrote defaults to {written}");
                return 0;
            }
            case "set":
            {
                string key = args.Positional(2) ?? throw new ArgumentException("config set: missing key.");
                string value = args.Positional(3) ?? throw new ArgumentException("config set: missing value.");
                var (settings, path) = SettingsStore.Load(explicitPath);
                var updated = SettingsStore.Set(settings, key, value);
                string written = SettingsStore.Save(updated, path ?? explicitPath);
                Console.WriteLine($"Saved to {written}");
                Console.WriteLine(SettingsStore.Serialize(updated));
                return 0;
            }
            default:
                throw new ArgumentException($"Unknown config command '{args.Positional(1)}'. Use show, path, init or set.");
        }
    }

    private static int List()
    {
        Console.WriteLine("LCD (USB HID 04D9:FD01):");
        var hid = HidDevice.Enumerate(LcdPanel.VendorId, LcdPanel.ProductId);
        if (hid.Count == 0)
            Console.WriteLine("  (none found - is this the S1?)");
        var chosen = LcdPanel.Find();
        foreach (var d in hid)
            Console.WriteLine($"  {(d == chosen ? "*" : " ")} {d}");

        Console.WriteLine("\nLED strip (CH340 serial, VID 1A86):");
        var ports = LedStrip.FindPorts();
        Console.WriteLine(ports.Count == 0 ? "  (none found)" : "  " + string.Join(", ", ports));
        return 0;
    }

    private static int Led(Args args, PanelSettings settings)
    {
        var saved = settings.Led;
        string modeName = args.Positional(1) ?? saved?.Mode
                          ?? throw new ArgumentException("led: give a mode (rainbow, breathing, cycle, off, auto) or set led.mode in the settings.");
        var led = new LedSettings
        {
            Mode = modeName,
            Intensity = args.IntOption("intensity", saved?.Intensity ?? 3),
            Speed = args.IntOption("speed", saved?.Speed ?? 3),
            Port = args.Option("port") ?? saved?.Port,
        };
        ApplyLed(led);
        return 0;
    }

    /// <summary>
    /// Steps the LED controller through mode bytes outside the documented 1-5, showing the mode under test
    /// on the LCD so someone watching the strip can note what each one does. Restores breathing at the end.
    /// </summary>
    private static int LedProbe(Args args, PanelSettings settings, CancellationToken ct)
    {
        var modes = ParseByteList(args.Option("modes") ?? "0,6-15");
        int hold = args.IntOption("hold", 8);
        byte intensity = (byte)args.IntOption("wire-intensity", 3), speed = (byte)args.IntOption("wire-speed", 3);
        string port = args.Option("port") ?? settings.Led?.Port ?? LedStrip.FindPorts().FirstOrDefault()
                      ?? throw new IOException("LED strip (CH340) not found. Pass --port COMx.");

        var orientation = OrientationExtensions.Parse(args.Option("orientation") ?? settings.Orientation);
        using var session = OpenSession(args, settings, orientation);
        using var bmp = FrameConverter.CreateCanvasBitmap(orientation);
        using var canvas = new SKCanvas(bmp);
        var accent = new SKColor(0xFF, 0xC8, 0x3D);

        void Show(string text, SKColor color)
        {
            Scenes.Text(canvas, bmp.Width, bmp.Height, text, color, new SKColor(0x10, 0x14, 0x1D), 40);
            canvas.Flush();
            session.Present(bmp);
        }

        void Wait(int secs)
        {
            var until = DateTime.UtcNow.AddSeconds(secs);
            while (!ct.IsCancellationRequested && DateTime.UtcNow < until)
            {
                session.KeepAlive();
                ct.WaitHandle.WaitOne(250);
            }
        }

        Console.WriteLine($"LED probe on {port}: modes {string.Join(",", modes.Select(m => $"0x{m:X2}"))}, {hold}s each, wire intensity {intensity} speed {speed}");
        Show("LED probe\nwatch the strip\nstarting...", SKColors.White);
        Wait(10);

        try
        {
            for (int i = 0; i < modes.Count && !ct.IsCancellationRequested; i++)
            {
                byte m = modes[i];
                var cmd = LedStrip.BuildRaw(m, intensity, speed);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  mode {m,3} (0x{m:X2})  sent {Convert.ToHexString(cmd)}");
                Show($"LED mode {m}\n(0x{m:X2})\n{i + 1} of {modes.Count}", accent);
                LedStrip.Send(port, cmd);
                Wait(hold);
            }
        }
        finally
        {
            LedStrip.Set(port, LedMode.Breathing, 3, 3);
            Console.WriteLine("Restored LED to breathing.");
            Show("LED probe\ndone", SKColors.White);
        }
        return 0;
    }

    private static List<byte> ParseByteList(string spec)
    {
        var result = new List<byte>();
        foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] range = part.Split('-');
            int from = ParseByte(range[0]), to = range.Length > 1 ? ParseByte(range[1]) : from;
            for (int v = from; v <= to; v++)
                result.Add((byte)v);
        }
        return result;

        static int ParseByte(string t)
        {
            int v = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(t[2..], 16) : int.Parse(t);
            return v is >= 0 and <= 255 ? v : throw new ArgumentException($"'{t}' is not a byte value.");
        }
    }

    private static void ApplyLed(LedSettings led)
    {
        LedMode mode = LedStrip.ParseMode(led.Mode);
        string port = led.Port ?? LedStrip.FindPorts().FirstOrDefault()
                      ?? throw new IOException("LED strip (CH340) not found. Pass --port COMx or set led.port.");
        LedStrip.Set(port, mode, led.Intensity, led.Speed);
        Console.WriteLine($"LED on {port}: {mode}" + (mode == LedMode.Off ? "" : $" (intensity {led.Intensity}, speed {led.Speed})"));
    }

    /// <summary>Draws a single unchanging scene, then holds it on screen with heartbeats.</summary>
    private static int RunStatic(Args args, PanelSettings settings, CancellationToken ct, Action<SKCanvas, int, int> draw)
    {
        var orientation = OrientationExtensions.Parse(args.Option("orientation") ?? settings.Orientation);
        using var bmp = FrameConverter.CreateCanvasBitmap(orientation);
        using (var canvas = new SKCanvas(bmp))
            draw(canvas, bmp.Width, bmp.Height);

        if (args.Option("preview") is { } preview)
            return SavePreview(bmp, orientation, preview);

        using var session = OpenSession(args, settings, orientation);
        session.Present(bmp);
        bool once = args.Flag("once");
        Console.WriteLine("Shown on panel." + (once ? "" : " Holding - press Ctrl+C to exit."));
        if (once)
            return 0;

        while (!ct.IsCancellationRequested)
        {
            session.KeepAlive();
            ct.WaitHandle.WaitOne(250);
        }
        return 0;
    }

    private static int Monitor(Args args, PanelSettings settings, CancellationToken ct)
    {
        // Command line beats the settings file; the settings file beats the defaults.
        var orientation = OrientationExtensions.Parse(args.Option("orientation") ?? settings.Orientation);
        int interval = args.IntOption("interval", settings.IntervalMs);
        string drive = args.Option("drive") ?? settings.Drive;
        if (!drive.EndsWith('\\')) drive += "\\";
        var layout = LayoutRegistry.Get(args.Option("layout") ?? settings.Layout);
        bool clock12 = PanelSettings.ParseClock12(args.Option("clock") ?? settings.Clock);
        bool fahrenheit = PanelSettings.ParseFahrenheit(args.Option("units") ?? settings.Units);
        bool blink = args.Flag("no-blink") ? false : args.Flag("blink") || settings.Blink;
        float textScale = LayoutExtensions.ParseTextSize(args.Option("text-size") ?? settings.TextSize);

        string? bgFile = args.Option("background") ?? settings.Background;
        using SKBitmap? background = bgFile is null
            ? null
            : SKBitmap.Decode(bgFile) ?? throw new ArgumentException($"Could not read image '{bgFile}'.");
        if (layout is SceneLayout scene)
        {
            scene.Image = background;
            scene.Season = SceneArt.Parse(args.Option("scene") ?? settings.Scene);
        }

        var stats = new SystemStats(drive);
        var history = new List<double>();
        using var bmp = FrameConverter.CreateCanvasBitmap(orientation);
        using var canvas = new SKCanvas(bmp);

        string? preview = args.Option("preview");
        using PanelSession? session = preview is null ? OpenSession(args, settings, orientation) : null;
        if (preview is not null)
        {
            // Collect a few samples so the preview has something in the graph.
            for (int i = 0; i < 20; i++) { Thread.Sleep(100); history.Add(stats.Sample().CpuPercent); }
        }
        else
        {
            Console.WriteLine($"Monitor running ({layout.Name}, {orientation}, {(clock12 ? "12" : "24")}-hour clock, " +
                              $"{(fahrenheit ? "°F" : "°C")}, text {textScale:0.##}x) - press Ctrl+C to exit.");
            if (settings.Led is { } led && !args.Flag("no-led"))
            {
                try { ApplyLed(led); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    Console.Error.WriteLine($"warning: LED settings not applied: {ex.Message}");
                }
            }
        }

        while (!ct.IsCancellationRequested)
        {
            var s = stats.Sample();
            history.Add(s.CpuPercent);
            if (history.Count > 60) history.RemoveAt(0);

            layout.Render(canvas, new LayoutContext(bmp.Width, bmp.Height, s, history, clock12, fahrenheit, blink, textScale));
            canvas.Flush();

            if (preview is not null)
                return SavePreview(bmp, orientation, preview);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            session!.Present(bmp);
            if (args.Flag("stats"))
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {(session.LastTilesSent < 0 ? "full redraw" : $"{session.LastTilesSent,2} tiles")}  {sw.ElapsedMilliseconds,5} ms");
            if (args.Flag("once"))
                return 0;

            // Sleep until the next tick, keeping the heartbeat alive in between. Ticks are aligned to the wall
            // clock (every whole interval + 20 ms) so the blinking colon and seconds stay in step.
            var next = NextTick(interval);
            while (!ct.IsCancellationRequested && DateTime.UtcNow < next)
            {
                session.KeepAlive();
                ct.WaitHandle.WaitOne(Math.Min(250, Math.Max(1, (int)(next - DateTime.UtcNow).TotalMilliseconds)));
            }
        }
        return 0;
    }

    private static DateTime NextTick(int intervalMs)
    {
        long nowMs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
        long nextMs = (nowMs / intervalMs + 1) * intervalMs + 20;
        return new DateTime(nextMs * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    private static PanelSession OpenSession(Args args, PanelSettings settings, Orientation orientation)
    {
        var options = new PanelSessionOptions
        {
            PostRedrawDelay = TimeSpan.FromMilliseconds(args.IntOption("redraw-delay", settings.RedrawDelayMs)),
            UpdateGap = TimeSpan.FromMilliseconds(args.IntOption("update-gap", settings.UpdateGapMs)),
            AlwaysRedraw = args.Flag("always-redraw"),
        };
        var lcd = LcdPanel.Open(args.Option("device") ?? settings.Device);
        return new PanelSession(lcd, orientation, options);
    }

    /// <summary>Saves what the panel would actually display (round-tripped through RGB565 and the rotation).</summary>
    private static int SavePreview(SKBitmap logical, Orientation orientation, string path)
    {
        var frame = new byte[LcdPanel.FrameBytes];
        FrameConverter.ToNative(logical, orientation, frame);
        using var shown = FrameConverter.FromNative(frame, orientation);
        using var data = shown.Encode(SKEncodedImageFormat.Png, 100);
        using (var fs = File.Create(path))
            data.SaveTo(fs);
        Console.WriteLine($"Preview written to {Path.GetFullPath(path)} ({shown.Width}x{shown.Height}, {orientation}).");
        return 0;
    }
}

/// <summary>Tiny argument parser: positionals plus --name value / --flag.</summary>
internal sealed class Args
{
    private readonly List<string> _positional = [];
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "once", "contain", "always-redraw", "stats", "no-blink", "blink", "no-led", "force",
    };

    public Args(string[] argv)
    {
        for (int i = 0; i < argv.Length; i++)
        {
            string a = argv[i];
            if (a.StartsWith("--"))
            {
                string name = a[2..];
                if (Flags.Contains(name))
                    _options[name] = null;
                else if (i + 1 < argv.Length)
                    _options[name] = argv[++i];
                else
                    throw new ArgumentException($"Option --{name} needs a value.");
            }
            else
            {
                _positional.Add(a);
            }
        }
    }

    public string? Positional(int i) => i < _positional.Count ? _positional[i] : null;
    public string? Option(string name) => _options.TryGetValue(name, out var v) ? v : null;
    public bool Flag(string name) => _options.ContainsKey(name);

    public int IntOption(string name, int fallback) =>
        Option(name) is { } v ? int.TryParse(v, out int n) ? n : throw new ArgumentException($"--{name} must be a number.") : fallback;
}
