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

using S1Panel.Core.Config;
using S1Panel.Core.Hid;
using S1Panel.Core.Lcd;
using S1Panel.Core.Led;
using S1Panel.Core.Monitor;
using S1Panel.Core.Rendering;
using S1Panel.Core.Rendering.Layouts;
using S1Panel.Core.Sensors;
using S1Panel.Core.Services;
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
          s1panel location [ZIP|place|auto]     Check the location used for weather / sun times
          s1panel now-playing [--seconds N]     Show what Windows reports as playing (diagnostics)

        Monitor options (each overrides the settings file for this run):
          --layout <tiles|gauge|clock|scene|split|dashboard>
          --widgets <left,right>                the split layout's two widgets (see `s1panel layouts`)
          --location <auto|ZIP|place|lat,lon>   for weather and sun times (auto = Windows location)
          --photos <folder>                     pictures for the photos widget (default: your Pictures folder)
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
            if (cmd is null or "help" or "-h" or "/?" || args.Flag("help") || args.Flag("h"))
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
                case "location": return Location(args, settings);
                case "now-playing": return NowPlaying(args, cts.Token);
                case "layouts":
                    foreach (var l in LayoutRegistry.All)
                        Console.WriteLine($"  {(l.Name == settings.Layout ? "*" : " ")} {l.Name,-10} {l.Description}");
                    Console.WriteLine("\nWidgets for the split layout (--widgets left,right):");
                    foreach (var (name, description) in S1Panel.Core.Rendering.Widgets.WidgetRegistry.All)
                        Console.WriteLine($"  {(settings.Widgets.Contains(name) ? "*" : " ")} {name,-10} {description}");
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

    private static int Monitor(Args args, PanelSettings saved, CancellationToken ct)
    {
        // Command line beats the settings file; the settings file beats the defaults.
        var settings = WithOverrides(args, saved);
        using var composer = new FrameComposer(settings);

        if (args.Option("preview") is { } preview)
        {
            composer.WarmUp(); // so graphs and rates have data
            composer.ComposeNext();
            // Location/weather/photos load in the background: give them a moment so the preview isn't blank.
            for (int i = 0; i < 60 && composer.HasPendingWork; i++)
            {
                Thread.Sleep(250);
                composer.ComposeNext();
            }
            composer.ComposeNext(); // data may have arrived just after the last frame
            return SavePreview(composer.Bitmap, composer.Orientation, preview);
        }

        using var session = OpenSession(args, settings, composer.Orientation);
        Console.WriteLine($"Monitor running ({composer.Layout.Name}, {composer.Orientation}, {(composer.Clock12 ? "12" : "24")}-hour clock, " +
                          $"{(composer.Fahrenheit ? "°F" : "°C")}, text {composer.TextScale:0.##}x) - press Ctrl+C to exit.");
        if (settings.Led is { } led && !args.Flag("no-led"))
        {
            try { ApplyLed(led); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"warning: LED settings not applied: {ex.Message}");
            }
        }

        while (!ct.IsCancellationRequested)
        {
            composer.ComposeNext();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            session.Present(composer.Bitmap);
            if (args.Flag("stats"))
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {(session.LastTilesSent < 0 ? "full redraw" : $"{session.LastTilesSent,2} tiles")}  {sw.ElapsedMilliseconds,5} ms");
            if (args.Flag("once"))
                return 0;

            // Sleep until the next tick, keeping the heartbeat alive in between. Ticks are aligned to the wall
            // clock (every whole interval + 20 ms) so the blinking colon and seconds stay in step.
            var next = MonitorEngine.NextTick(settings.IntervalMs);
            while (!ct.IsCancellationRequested && DateTime.UtcNow < next)
            {
                session.KeepAlive();
                ct.WaitHandle.WaitOne(Math.Min(250, Math.Max(1, (int)(next - DateTime.UtcNow).TotalMilliseconds)));
            }
        }
        return 0;
    }

    private static int Location(Args args, PanelSettings settings)
    {
        string query = args.Positional(1) ?? settings.Location;
        Console.WriteLine($"Resolving location '{query}'...");
        var state = LocationService.ResolveAsync(query, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine($"{state.Status}: {state.Message}");
        if (state.Status == LocationStatus.NeedsInput)
            Console.WriteLine("Set one with:  s1panel config set location <ZIP code or city>");
        else if (state.Location is { } l)
        {
            var sun = Astronomy.Sun(l.Latitude, l.Longitude, DateOnly.FromDateTime(DateTime.Now));
            Console.WriteLine($"Sunrise {sun.Sunrise:t}, sunset {sun.Sunset:t} (this PC's time zone); moon: {Astronomy.Moon(DateTime.UtcNow).Name}");
        }
        return state.Status == LocationStatus.Ready ? 0 : 3;
    }

    /// <summary>Prints what Windows' media sessions report every 2 s (and what the widget shows), for diagnosing now-playing.</summary>
    private static int NowPlaying(Args args, CancellationToken ct)
    {
        int seconds = int.TryParse(args.Option("seconds"), out int s) ? s : 10;
        var end = DateTime.UtcNow.AddSeconds(seconds);
        NowPlayingService.WindowsEvent += what => Console.WriteLine($"{DateTime.Now:HH:mm:ss.f} event:   {what}");
        do
        {
            string windows = NowPlayingService.DescribeAsync(ct).GetAwaiter().GetResult();
            var state = NowPlayingService.Get();
            string widget = state.Media is { } m ? $"'{m.Title}' by '{m.Artist}' ({m.App}, {(m.Playing ? "playing" : "paused")}, art={(m.Art is null ? "no" : "yes")})"
                : state.Ready ? state.Problem ?? "nothing playing" : "checking";
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} windows: {windows}");
            var wa = WinampSource.Read();
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} winamp:  {(wa is null ? "no Winamp/WACUP window" : $"{wa.ExeName} '{wa.Display}' {wa.Status} {(int)wa.Position.TotalSeconds}s/{(int)wa.Length.TotalSeconds}s")}");
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} widget:  {widget}");
            Console.Out.Flush();
        } while (DateTime.UtcNow < end && !ct.WaitHandle.WaitOne(1000));
        return 0;
    }

    /// <summary>A copy of the saved settings with any monitor options from the command line applied.</summary>
    private static PanelSettings WithOverrides(Args args, PanelSettings saved)
    {
        var s = SettingsStore.Clone(saved);
        s.Orientation = args.Option("orientation") ?? s.Orientation;
        s.Layout = args.Option("layout") ?? s.Layout;
        s.Clock = args.Option("clock") ?? s.Clock;
        s.Units = args.Option("units") ?? s.Units;
        s.TextSize = args.Option("text-size") ?? s.TextSize;
        s.Scene = args.Option("scene") ?? s.Scene;
        s.Location = args.Option("location") ?? s.Location;
        if (args.Option("photos") is { } photos)
            s.Slideshow.Folder = photos;
        if (args.Option("widgets") is { } widgets)
            s.Widgets = widgets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        s.Drive = args.Option("drive") ?? s.Drive;
        s.Background = args.Option("background") ?? s.Background;
        s.IntervalMs = args.IntOption("interval", s.IntervalMs);
        if (args.Flag("no-blink")) s.Blink = false;
        else if (args.Flag("blink")) s.Blink = true;
        s.Validate();
        return s;
    }

    /// <summary>Held for the life of the process once a screen command opens the panel.</summary>
    private static PanelLock? _panelLock;

    private static PanelSession OpenSession(Args args, PanelSettings settings, Orientation orientation)
    {
        var options = new PanelSessionOptions
        {
            PostRedrawDelay = TimeSpan.FromMilliseconds(args.IntOption("redraw-delay", settings.RedrawDelayMs)),
            UpdateGap = TimeSpan.FromMilliseconds(args.IntOption("update-gap", settings.UpdateGapMs)),
            AlwaysRedraw = args.Flag("always-redraw"),
        };
        _panelLock ??= PanelLock.TryAcquire()
                       ?? throw new IOException("The panel is in use by another s1panel (is the tray app running? Pause it from its menu first).");
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
        "once", "contain", "always-redraw", "stats", "no-blink", "blink", "no-led", "force", "help", "h",
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
