@echo off
rem S1 Panel
rem Copyright (C) 2026 dopaz
rem SPDX-License-Identifier: GPL-3.0-or-later
rem
rem This program is free software: you can redistribute it and/or modify it
rem under the terms of the GNU General Public License as published by the
rem Free Software Foundation, either version 3 of the License, or (at your
rem option) any later version.  It is distributed WITHOUT ANY WARRANTY; see
rem the GNU General Public License in LICENSE for details.
rem
rem Developed with Claude Code (Claude Opus 5.5).

rem packaging\windows.cmd -- the distribution package:
rem
rem   zip_distribution\s1panel-windows-x64.zip   an S1Panel\ folder with
rem       s1panel-tray.exe, s1panel.exe, README.txt, LICENSE, licenses\
rem       and s1panel-source.zip
rem   zip_distribution\s1panel-source.zip        the source on its own
rem
rem It runs the tests, then publishes both programs self-contained (no .NET
rem needed on the PC that runs them).  Needs the .NET 10 SDK.  The source
rem zip is LICENSE, CREDITS.md, src\ and this packaging (without its
rem output), minus build output, so it builds and packages again as is.
rem The zips are made by Windows' own tar.exe (bsdtar, in System32 since
rem Windows 10 1803).  Pass --no-tests to skip the tests.
setlocal
cd /d "%~dp0.."
set "TAR=%SystemRoot%\System32\tar.exe"

if /i not "%~1"=="--no-tests" (
    dotnet test src\S1Panel.slnx --nologo -v q || exit /b 1
)
dotnet publish src\S1Panel.Tray -c Release -o publish --nologo -v q || exit /b 1
dotnet publish src\S1Panel.Cli -c Release -o publish --nologo -v q || exit /b 1

set "ST=publish\pkg"
set "D=%ST%\S1Panel"
if exist "%ST%" rmdir /s /q "%ST%"
mkdir "%D%\licenses" || exit /b 1

rem the source, as fully compressed as zip goes
"%TAR%" -a -c -f "%D%\s1panel-source.zip" --options zip:compression-level=9 ^
    --exclude bin --exclude obj --exclude .vs --exclude "*.user" ^
    LICENSE CREDITS.md src packaging\windows.cmd packaging\README-windows.txt packaging\licenses || exit /b 1

copy /y publish\s1panel-tray.exe "%D%\" >nul || exit /b 1
copy /y publish\s1panel.exe "%D%\" >nul || exit /b 1
copy /y LICENSE "%D%\" >nul || exit /b 1
copy /y packaging\README-windows.txt "%D%\README.txt" >nul || exit /b 1
copy /y packaging\licenses\* "%D%\licenses\" >nul || exit /b 1
copy /y LICENSE "%D%\licenses\LICENSE-s1panel.txt" >nul || exit /b 1
copy /y CREDITS.md "%D%\licenses\" >nul || exit /b 1

set "OUT=zip_distribution"
if not exist "%OUT%" mkdir "%OUT%"
if exist "%OUT%\s1panel-windows-x64.zip" del "%OUT%\s1panel-windows-x64.zip"
"%TAR%" -a -c -f "%OUT%\s1panel-windows-x64.zip" -C "%ST%" S1Panel || exit /b 1
copy /y "%D%\s1panel-source.zip" "%OUT%\" >nul || exit /b 1
rem the staging folder can stay locked for a moment (a virus scan of the
rem fresh exes): try again once, and otherwise leave it for the next run
rmdir /s /q "%ST%" 2>nul || (ping -n 3 127.0.0.1 >nul & rmdir /s /q "%ST%" 2>nul)
echo %CD%\%OUT%\s1panel-windows-x64.zip
echo %CD%\%OUT%\s1panel-source.zip
