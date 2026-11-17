S1 Panel -- for the AceMagic S1 mini PC (Windows 10 2004 or later, x64)

Drives the S1's front LCD and LED strip without administrator rights,
drivers or a background service: clocks, CPU/RAM/disk gauges, weather,
seasonal scenes, a photo slideshow, Now Playing, and a couple of animated
buddies.

  s1panel-tray.exe    the app: lives in the notification area
  s1panel.exe         the same from a command prompt (s1panel --help)
  s1panel-source.zip  the source code
  LICENSE             the GNU General Public License, version 3
  licenses\           the licenses of what is built into the programs

Getting started

1. Quit (or uninstall) AceMagic's own panel software: it holds the panel.
2. Extract this folder somewhere to keep it, e.g. C:\S1_Panel, and run
   s1panel-tray.exe.  The programs aren't signed, so Windows may say
   "Windows protected your PC": choose "More info", then "Run anyway".
3. Click the tray icon for Settings, with a live preview of the panel.
   Right-click it for quick layout changes, Lights Out (black screen and
   LED strip off), Pause, and Start with Windows.

Settings are kept in s1panel.json beside the programs (or in
%APPDATA%\s1panel\ if this folder isn't writable).  Nothing needs to be
installed: to remove S1 Panel, untick Start with Windows, exit it, and
delete the folder.

Weather comes from Open-Meteo (open-meteo.com, CC BY 4.0).  Only your
location's coordinates, or the place name you type, are sent.

Why are the programs so big?

s1panel-tray.exe is about 63 MB and s1panel.exe about 50 MB.  They hold
no debug symbols: almost all of the size is .NET itself.

A design goal of S1 Panel is that it runs without administrator rights.
Installing the .NET runtime needs administrator rights, so instead each
program carries its own private copy of .NET built in.  Nothing has to
be installed, and each program is a single file rather than a folder of
a couple of hundred small ones.  Inside, about 146 MB of files are
compressed into the exe and unpacked when it starts.  The largest:

  Component                          Size    What it is
  ---------------------------------  ------  ------------------------------
  Microsoft.Windows.SDK.NET.dll      24 MB   the bridge to newer Windows
                                             features: Now Playing and
                                             Windows location need it
  System.Private.CoreLib.dll         15 MB   the core of .NET
  System.Windows.Forms.dll, with     22 MB   the tray menu and the Settings
    its Design and Primitives parts          and About windows
  libSkiaSharp.dll                   12.5 MB the graphics library that
                                             draws everything on the panel
  coreclr, clrjit, XML, LINQ,        the     the remaining parts of .NET
    cryptography, data and more      rest

s1panel.exe carries its own copy of nearly all of this too, which is
why the download is about 100 MB.

Support

If S1 Panel is useful to you, you can buy me a coffee:
https://ko-fi.com/ddopaz

License

S1 Panel
Copyright (C) 2026 dopaz
SPDX-License-Identifier: GPL-3.0-or-later

This program is free software: you can redistribute it and/or modify it
under the terms of the GNU General Public License as published by the
Free Software Foundation, either version 3 of the License, or (at your
option) any later version.  It is distributed WITHOUT ANY WARRANTY; see
the GNU General Public License in LICENSE for details.  The source is in
s1panel-source.zip, and the licenses of what is built into the programs
(.NET, SkiaSharp) are in licenses\, with licenses\CREDITS.md.

S1 Panel is built upon the work shared at
https://github.com/tjaworski/AceMagic-S1-LED-TFT-Linux (GPL-3.0), which
worked out how the S1's panel and LED strip are driven.  Many thanks to
tjaworski for figuring it out and sharing it.  S1 Panel is a fresh
implementation for Windows.

AceMagic is a trademark of its owner; S1 Panel is unofficial.

Developed with Claude Code (Claude Opus 5.5).
