# s1panel (Windows)

Controls the AceMagic S1 front LCD (320×170) and LED strip on Windows 11 — **no administrator rights, no drivers, no background service from the vendor**.

## Build

Requires the .NET 10 SDK (only on the build machine).

```powershell
cd src
dotnet test S1Panel.slnx                       # protocol tests (no hardware needed)
dotnet publish S1Panel.Cli -c Release -o ..\publish
```

```powershell
dotnet publish S1Panel.Tray -c Release -o ..\publish   # the tray app
```

This produces `publish\s1panel.exe` (command line, about 43 MB) and `publish\s1panel-tray.exe` (tray app, about 57 MB), both self-contained. Copy them to the same folder on the S1, so they share one `s1panel.json`. The S1 doesn't need .NET installed.

## Tray app (everyday use)

Run `s1panel-tray.exe`. It sits in the notification area and keeps the panel updated:

- **Right-click** for: status, **Settings…**, quick **Layout** and **Scene** switching, **Lights Out** (black screen and LED strip off; your layout and LED settings come back when you untick it), **Pause/Resume** (pausing releases the panel so other tools can use it), **Start with Windows**, and **Exit**.
- **Click or double-click** to open Settings, which covers every option, with a live preview of exactly what the panel will show. **Save/Apply** write `s1panel.json` and update the panel immediately.
- It **reconnects by itself** after sleep, unplugging or USB errors. The icon turns orange while it's waiting for the panel, and grey while paused.
- It **watches `s1panel.json`**, so `s1panel config set …` (even over ssh) takes effect without restarting.
- **Start with Windows** uses the per-user Run key, so no admin is needed.
- The tray app and a command-line `monitor` share a machine-wide lock, so they never fight over the panel; whichever starts second waits or is refused.

## Use (on the S1)

First quit or uninstall the stock AceMagic panel software, because it may hold the device open.

```powershell
s1panel list                                  # find the LCD (HID 04D9:FD01) and LED COM port
s1panel test                                  # colour bars + "TOP ^" marker
s1panel test --orientation landscape          # try other orientations until the marker points up
s1panel monitor                               # live clock / CPU / RAM / disk / network
s1panel layouts                               # list layouts: tiles, gauge, clock, scene, split, dashboard, blank
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

## Split layout and widgets

`layout: split` shows two widgets side by side (stacked in portrait), chosen with `widgets` (left/top first):

| Widget | Shows | Needs |
|---|---|---|
| `clock` | day, large time, date | - |
| `cpu-ram` | CPU and RAM rings, CPU history graph | - |
| `weather` | current conditions, today's high/low, 3-day forecast | location + internet (Open-Meteo, free, no key; only coordinates are sent) |
| `sun-moon` | sunrise/sunset arc with the sun's position, daylight left, moon phase | location (calculated locally, no internet) |
| `photos` | slideshow from a folder (subfolders included, EXIF rotation honoured) | `slideshow.folder` (default: your Pictures folder) |
| `now-playing` | album art, title, artist and progress of what Windows' media flyout shows (Spotify, browsers, Media Player…) | nothing (needs the tray app or a desktop session, not ssh) |
| `robot` | animated robot whose mood follows CPU load; sleeps late at night, waves on the hour | nothing |
| `hamster` | animated hamster whose wheel spins with CPU load; counts the day's laps | nothing |

```powershell
s1panel config set layout split
s1panel config set widgets weather,sun-moon
s1panel location                        # check what "auto" resolves to
s1panel config set location 90210       # if Windows location is off: a ZIP code, a city, or lat,lon
s1panel config set slideshow.folder D:\Photos\Favourites
s1panel config set slideshow.intervalSeconds 60
```

**Location:** `auto` uses Windows' location service (Settings → Privacy & security → Location, "Let desktop apps access your location"). If Windows doesn't give a position, the tray app asks for a ZIP code once.

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
| `layout` | tiles, gauge, clock, scene, split, dashboard, blank (plain black: the panel has no off switch) | dashboard |
| `orientation` | landscape, portrait, landscape-flipped, portrait-flipped | landscape |
| `clock` | 12, 24, auto | auto (Windows time format) |
| `units` | metric, imperial, auto | auto (Windows region) |
| `blink` | true, false | true |
| `textSize` | normal, large, larger, or 0.5–2 | normal |
| `intervalMs` | 100–3600000 | 1000 |
| `drive` | drive letter for the disk reading | C: |
| `widgets` | two of: clock, cpu-ram, weather, sun-moon, photos, now-playing, robot, hamster | clock, cpu-ram |
| `location` | auto, a ZIP/postal code, a place name, or lat,lon | auto |
| `slideshow` | `{ folder, intervalSeconds 5–86400, shuffle, fit: cover/contain }` | Pictures, 30, true, cover |
| `scene` | autumn, winter, spring, summer, auto (by date: Dec–Feb winter, Mar–May spring, Jun–Aug summer, Sep–Nov autumn) | auto |
| `background` | image path for the scene layout (replaces the artwork), or null | null |
| `led` | `{ mode, intensity 1–5, speed 1–5, port }`, or null to leave the LEDs alone | null |
| `lightsOut` | true = blank screen and LED strip off, keeping `layout` and `led` for when it's switched back | false |
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
| `S1Panel.Core/Monitor` | `MonitorEngine` (background update loop, auto-reconnect, live settings), `FrameComposer`, `PanelLock` |
| `S1Panel.Cli` | `s1panel.exe` command line |
| `S1Panel.Tray` | `s1panel-tray.exe`: notification-area app and settings window (WinForms) |

Protocol notes are in `claude/NOTES.md`. The protocol was learned from the GPL-3 project in `example/`, and this code is a fresh implementation.

## Tuning knobs (if the panel misbehaves)

- `--update-gap <ms>`: pause between tile writes (default 4). Raise it if updates get dropped or the panel lags.
- `--redraw-delay <ms>`: pause after a full-frame redraw (default 1600, as in the original software).
- `--always-redraw`: send full frames instead of tiles.

Each 4 KB write takes about 80 ms (the panel's USB link is roughly 64 KB/s), so what matters is how many 64×32 tiles change per update.
`monitor --stats` prints this per frame. Measured on the S1 at one update per second: scene <1 write, gauge ~5, tiles ~7, clock ~10.
