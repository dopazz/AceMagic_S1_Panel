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

namespace S1Panel.Tray;

internal static class Program
{
    /// <summary>
    /// s1panel-tray.exe [--config file]
    ///   --write-icon file.ico           (dev) write the app icon and exit
    ///   --screenshot-settings out.png   (dev) render the settings window to a PNG and exit
    ///   --screenshot-about out.png      (dev) render the About window to a PNG and exit
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        string? Arg(string name)
        {
            int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        if (Arg("--write-icon") is { } icoPath)
        {
            TrayIcons.WriteIcoFile(icoPath);
            return 0;
        }

        ApplicationConfiguration.Initialize();

        if (Arg("--screenshot-about") is { } aboutShot)
            return Capture(new AboutForm(), aboutShot);
        if (Arg("--screenshot-settings") is { } shot)
            return Screenshot(shot, Arg("--config"), int.TryParse(Arg("--tab"), out int tab) ? tab : 0);

        // One tray per user session.
        using var single = new Mutex(true, @"Local\S1Panel.Tray", out bool first);
        if (!first)
        {
            MessageBox.Show("S1 Panel is already running - look for its icon in the notification area.", "S1 Panel",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        // Log anything that would otherwise end the app silently. UI-thread exceptions are logged and the app carries on.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => TrayLog.Error("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            TrayLog.Write($"FATAL{(e.IsTerminating ? " (terminating)" : "")}: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => { TrayLog.Error("background task", e.Exception); e.SetObserved(); };

        try
        {
            Application.Run(new TrayApp(Arg("--config")));
            TrayLog.Write("Exited normally.");
            return 0;
        }
        catch (ArgumentException ex) // unreadable settings file
        {
            MessageBox.Show($"Could not start: {ex.Message}", "S1 Panel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
    }

    private static int Screenshot(string path, string? config, int tab)
    {
        var (settings, settingsPath) = SettingsStore.Load(config);
        using var form = new SettingsForm(settings, settingsPath ?? SettingsStore.ExeDirPath, _ => null)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            ShowInTaskbar = false,
        };
        form.ShowEngineStatus(new S1Panel.Core.Monitor.EngineStatus(S1Panel.Core.Monitor.EngineState.Running, $"{settings.Layout}, {settings.Orientation}"));
        form.Show();
        if (FindTabs(form) is { } tabs && tab < tabs.TabCount)
            tabs.SelectedIndex = tab;
        for (int i = 0; i < 12; i++) { Application.DoEvents(); Thread.Sleep(250); } // let location/weather/photo load
        Application.DoEvents();
        using var bmp = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        form.Close();
        return 0;
    }

    private static int Capture(Form form, string path)
    {
        using (form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-4000, -4000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            form.Close();
        }
        return 0;
    }

    private static TabControl? FindTabs(Control root) =>
        root as TabControl ?? root.Controls.Cast<Control>().Select(FindTabs).FirstOrDefault(t => t is not null);
}
