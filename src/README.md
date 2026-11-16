# s1panel (Windows)

Controls the AceMagic S1 front LCD (320×170) and LED strip on Windows 11 — **no administrator rights, no drivers, no background service from the vendor**.

## Build

Requires the .NET 10 SDK (only on the build machine).

```powershell
cd src
dotnet test S1Panel.slnx                       # protocol tests (no hardware needed)
dotnet publish S1Panel.Cli -c Release -o ..\publish
```

This produces a single `publish\s1panel.exe` (self-contained, about 43 MB). Copy it to the S1. The S1 doesn't need .NET installed.

## Use (on the S1)

First quit or uninstall the stock AceMagic panel software, because it may hold the device open.

```powershell
s1panel list                                  # find the LCD (HID 04D9:FD01) and LED COM port
s1panel test                                  # colour bars + "TOP ^" marker
s1panel test --orientation landscape          # try other orientations until the marker points up
s1panel monitor                               # live clock / CPU / RAM / disk / network
s1panel layouts                               # list layouts: tiles, gauge, clock, scene, dashboard
s1panel monitor --layout gauge                # any layout, landscape (default) or --orientation portrait
s1panel monitor --layout scene --clock 12     # 12-hour with AM/PM; --clock 24, or auto (default: Windows setting)
s1panel monitor --units metric                # °C; --units imperial for °F, or auto (default: Windows region)
s1panel monitor --no-blink                    # steady colon (default: blinks 1 s on / 1 s off)
s1panel monitor --text-size large             # bigger labels/units: normal (default), large, larger, or a number like 1.2
s1panel monitor --layout scene --scene winter   # autumn | winter | spring | summer | auto (by date)
s1panel monitor --layout scene --background C:\pics\photo.jpg   # scene with your own picture
s1panel image C:\pics\cat.jpg                 # crop to fill (add --contain to letterbox)
s1panel text "Hello\nWorld" --fg "#4CC2FF"
s1panel led rainbow --intensity 3 --speed 2   # rainbow | breathing | cycle | off | auto
```

LCD commands stay running and send a heartbeat every second until you press Ctrl+C. Without the heartbeat the firmware shows a "Disconnection…" notice.

## Settings file

Save your choices once, and a plain `s1panel monitor` uses them:

```powershell
s1panel config                          # where the file is + the settings in effect
s1panel config set layout scene
s1panel config set text-size larger
s1panel config set clock 12             # 12 | 24 | auto
s1panel config set units imperial       # metric | imperial | auto
s1panel config set blink off
s1panel config set led.mode breathing   # any led.* key turns on LED control at monitor start
s1panel config set led none             # leave the LEDs alone again
s1panel config init --force             # reset to defaults
```

The file is `s1panel.json` next to `s1panel.exe`. If that folder isn't writable, it goes in `%APPDATA%\s1panel\` instead. It's plain JSON (comments allowed), so you can also edit it by hand; misspelled keys and bad values are reported with the file name.
Order of precedence: **command-line option > settings file > built-in default**.

| Key | Values | Default |
|---|---|---|
| `layout` | tiles, gauge, clock, scene, dashboard | dashboard |
| `orientation` | landscape, portrait, landscape-flipped, portrait-flipped | landscape |
| `clock` | 12, 24, auto | auto (Windows time format) |
| `units` | metric, imperial, auto | auto (Windows region) |
| `blink` | true, false | true |
| `textSize` | normal, large, larger, or 0.5–2 | normal |
| `intervalMs` | 100–3600000 | 1000 |
| `drive` | drive letter for the disk reading | C: |
| `scene` | autumn, winter, spring, summer, auto (by date: Dec–Feb winter, Mar–May spring, Jun–Aug summer, Sep–Nov autumn) | auto |
| `background` | image path for the scene layout (replaces the artwork), or null | null |
| `led` | `{ mode, intensity 1–5, speed 1–5, port }`, or null to leave the LEDs alone | null |
| `device`, `updateGapMs`, `redrawDelayMs` | hardware tuning | auto, 4, 1600 |

Develop without hardware by adding `--preview out.png` to any LCD command. It writes exactly what the panel would show, including RGB565 colour loss and rotation.

## Layout

| Project | What |
|---|---|
| `S1Panel.Core/Hid` | Win32 SetupAPI/HID P/Invoke: enumerate, open, overlapped writes with timeout |
| `S1Panel.Core/Lcd/LcdPanel.cs` | Wire protocol: orientation, heartbeat, full redraw (27 chunks), partial update |
| `S1Panel.Core/Lcd/FrameConverter.cs` | Logical canvas ⇄ native 320×170 big-endian RGB565, with 4 orientations |
| `S1Panel.Core/Lcd/PanelSession.cs` | Diffs frames and sends only changed 64×32 tiles (one 4 KB report each), plus heartbeats |
| `S1Panel.Core/Led/LedStrip.cs` | CH340 serial LED commands and COM-port discovery |
| `S1Panel.Core/Sensors` | CPU/RAM/disk/network without admin |
| `S1Panel.Core/Rendering/Scenes.cs` | Test pattern, image, text, dashboard (SkiaSharp) |
| `S1Panel.Core/Rendering/Layouts` | Monitor layouts (each draws landscape + portrait) and the metrics they show |
| `S1Panel.Core/Sensors/NvmeTemperature.cs` | SSD temperature from the NVMe health log, no admin needed |
| `S1Panel.Core/Config` | `s1panel.json` settings: model, validation, load/save, `config set` |
| `S1Panel.Cli` | `s1panel.exe` command line |

Protocol notes are in `claude/NOTES.md`. The protocol was learned from the GPL-3 project in `example/`, and this code is a fresh implementation.

## Tuning knobs (if the panel misbehaves)

- `--update-gap <ms>`: pause between tile writes (default 4). Raise it if updates get dropped or the panel lags.
- `--redraw-delay <ms>`: pause after a full-frame redraw (default 1600, as in the original software).
- `--always-redraw`: send full frames instead of tiles.

Each 4 KB write takes about 80 ms (the panel's USB link is roughly 64 KB/s), so what matters is how many 64×32 tiles change per update.
`monitor --stats` prints this per frame. Measured on the S1 at one update per second: scene <1 write, gauge ~5, tiles ~7, clock ~10.
