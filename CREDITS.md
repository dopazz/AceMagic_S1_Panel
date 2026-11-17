# Credits

S1 Panel is Copyright (C) 2026 dopaz and is licensed GPL-3.0-or-later; see `LICENSE`.
It was developed with Claude Code (Claude Opus 5.5).

## The panel protocol

The S1's LCD and LED-strip protocol was learned from tjaworski's
[AceMagic-S1-LED-TFT-Linux](https://github.com/tjaworski/AceMagic-S1-LED-TFT-Linux),
which is licensed under GPL-3.0. S1 Panel is a fresh implementation for Windows; no code was copied.

## Built into the programs

`s1panel.exe` and `s1panel-tray.exe` are self-contained: the following are built into each one.
Their license texts are in `licenses\` in the Windows package (`packaging/licenses/` in the source).

| Component | License | Text |
|--|--|--|
| .NET runtime and Windows Forms, including System.IO.Ports (.NET Foundation and Contributors) | MIT | `LICENSE-dotnet.txt`, `THIRD-PARTY-NOTICES-dotnet.txt` |
| SkiaSharp (Microsoft / Xamarin), with the native Skia, HarfBuzz and their dependencies | MIT; third-party notices | `LICENSE-SkiaSharp.txt`, `THIRD-PARTY-NOTICES-SkiaSharp.txt` |

## Services

- Weather and place-name lookup: [Open-Meteo](https://open-meteo.com/), under
  [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Only coordinates or the place name
  you enter are sent.

AceMagic is a trademark of its owner. S1 Panel is an unofficial project and is not affiliated with AceMagic.
