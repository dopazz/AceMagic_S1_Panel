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

using Microsoft.Win32;
using S1Panel.Core.Config;
using S1Panel.Core.Monitor;
using S1Panel.Core.Rendering.Layouts;
using S1Panel.Core.Services;

namespace S1Panel.Tray;

/// <summary>
/// The tray icon and its menu. Owns the <see cref="MonitorEngine"/> that drives the panel, watches
/// s1panel.json for outside edits (e.g. `s1panel config set` over ssh), and reconnects after sleep.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _layoutMenu = new("Layout");
    private readonly ToolStripMenuItem _sceneMenu = new("Scene");
    private readonly ToolStripMenuItem _pause = new("Pause");
    private readonly ToolStripMenuItem _lightsOut = new("Lights Out") { ToolTipText = "Black screen and LED strip off (the panel has no real off switch)" };
    private readonly ToolStripMenuItem _autostart = new("Start with Windows");
    private readonly SynchronizationContext _ui;
    private readonly MonitorEngine _engine;
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Windows.Forms.Timer _reloadDebounce = new() { Interval = 400 };
    private string _settingsPath;
    private string _lastSavedJson;
    private SettingsForm? _settingsForm;
    private AboutForm? _aboutForm;
    private Icon? _currentIcon;
    private bool _askedForZip;
    private bool _exiting;

    public TrayApp(string? explicitConfigPath)
    {
        var (settings, path) = SettingsStore.Load(explicitConfigPath);
        _settingsPath = path ?? explicitConfigPath ?? SettingsStore.ExeDirPath;
        _lastSavedJson = SettingsStore.Serialize(settings);
        TrayLog.Init(_settingsPath);
        TrayLog.Write($"Started (layout {settings.Layout}, widgets {string.Join(",", settings.Widgets)}).");

        BuildMenu();
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _icon = new NotifyIcon { ContextMenuStrip = _menu, Visible = true, Text = "S1 Panel - starting" };
        SetIcon(TrayIcons.Ok);
        _icon.DoubleClick += (_, _) => OpenSettings();
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };

        _engine = new MonitorEngine(settings);
        _engine.StatusChanged += s => _ui.Post(_ => OnStatus(s), null);
        _engine.Warning += w => _ui.Post(_ => _icon.ShowBalloonTip(5000, "S1 Panel", w, ToolTipIcon.Warning), null);
        _engine.UnexpectedError += ex => TrayLog.Error("panel drawing", ex);
        NowPlayingService.Diagnostic += TrayLog.Write;
        SystemEvents.SessionEnding += (_, e) => TrayLog.Write($"Windows session ending ({e.Reason}).");
        _engine.Start();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        LocationService.WindowsLocationUnavailable += OnLocationUnavailable;

        string? dir = Path.GetDirectoryName(Path.GetFullPath(_settingsPath));
        if (dir is not null && Directory.Exists(dir))
        {
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_settingsPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler onChange = (_, _) => _ui.Post(_ => { _reloadDebounce.Stop(); _reloadDebounce.Start(); }, null);
            _watcher.Changed += onChange;
            _watcher.Created += onChange;
            _watcher.Renamed += (_, _) => _ui.Post(_ => { _reloadDebounce.Stop(); _reloadDebounce.Start(); }, null);
        }
        _reloadDebounce.Tick += (_, _) => { _reloadDebounce.Stop(); ReloadFromFile(); };
    }

    private void BuildMenu()
    {
        var settingsItem = new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings()) { Font = new Font(_menu.Font, FontStyle.Bold) };

        foreach (var l in LayoutRegistry.All)
        {
            string name = l.Name;
            _layoutMenu.DropDownItems.Add(new ToolStripMenuItem(char.ToUpper(name[0]) + name[1..], null, (_, _) => Change(s => { s.Layout = name; s.LightsOut = false; })) { Tag = name, ToolTipText = l.Description });
        }
        foreach (string season in new[] { "auto", "winter", "spring", "summer", "autumn" })
        {
            string label = season == "auto" ? "Follow the season" : char.ToUpper(season[0]) + season[1..];
            _sceneMenu.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) => Change(s => { s.Layout = "scene"; s.Scene = season; s.LightsOut = false; })) { Tag = season });
        }

        _lightsOut.Click += (_, _) => Change(s => s.LightsOut = !s.LightsOut);
        _pause.Click += (_, _) =>
        {
            if (_engine.IsPaused) _engine.Resume(); else _engine.Pause();
        };
        _autostart.Click += (_, _) =>
        {
            try { Autostart.Set(!Autostart.IsEnabled); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                MessageBox.Show(ex.Message, "S1 Panel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        _menu.Items.AddRange([
            _status,
            new ToolStripSeparator(),
            settingsItem,
            _layoutMenu,
            _sceneMenu,
            _lightsOut,
            _pause,
            new ToolStripSeparator(),
            _autostart,
            new ToolStripMenuItem("About S1 Panel…", null, (_, _) => OpenAbout()),
            new ToolStripMenuItem("Exit", null, (_, _) => { TrayLog.Write("Exit chosen from the tray menu."); ExitThread(); }),
        ]);
        _menu.Opening += (_, _) => RefreshMenu();
    }

    private void RefreshMenu()
    {
        var s = _engine.CurrentSettings;
        foreach (ToolStripMenuItem item in _layoutMenu.DropDownItems)
            item.Checked = !s.LightsOut && (string)item.Tag! == s.Layout;
        foreach (ToolStripMenuItem item in _sceneMenu.DropDownItems)
            item.Checked = !s.LightsOut && s.Layout == "scene" && (string)item.Tag! == s.Scene;
        _lightsOut.Checked = s.LightsOut;
        _pause.Text = _engine.IsPaused ? "Resume" : "Pause";
        _autostart.Checked = Autostart.IsEnabled;
        _status.Text = _engine.Status.ToString();
    }

    private void OnStatus(EngineStatus status)
    {
        if (_exiting)
            return; // the engine's final "stopped" arrives after the icon is gone

        string text = $"S1 Panel - {status}";
        _icon.Text = text.Length > 127 ? text[..127] : text; // NotifyIcon limit
        _status.Text = status.ToString();
        SetIcon(status.State switch
        {
            EngineState.Running or EngineState.Starting => TrayIcons.Ok,
            EngineState.Paused or EngineState.Stopped => TrayIcons.Paused,
            _ => TrayIcons.Problem,
        });
        _settingsForm?.ShowEngineStatus(status);
    }

    private void SetIcon(Color accent)
    {
        var old = _currentIcon;
        _currentIcon = TrayIcons.Create(accent);
        _icon.Icon = _currentIcon;
        old?.Dispose();
    }

    /// <summary>Applies a quick change from the menu, saves it, and hands it to the engine.</summary>
    private void Change(Action<PanelSettings> edit)
    {
        var s = _engine.CurrentSettings;
        edit(s);
        string? error = Save(s);
        if (error is not null)
            MessageBox.Show(error, "S1 Panel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Validates, writes s1panel.json and updates the engine. Returns an error message or null.</summary>
    private string? Save(PanelSettings s)
    {
        try
        {
            s.Validate();
            _lastSavedJson = SettingsStore.Serialize(s);
            _settingsPath = SettingsStore.Save(s, _settingsPath);
            _engine.UpdateSettings(s);
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private void ReloadFromFile()
    {
        try
        {
            var (s, _) = SettingsStore.Load(_settingsPath);
            string json = SettingsStore.Serialize(s);
            if (json == _lastSavedJson)
                return; // our own save
            _lastSavedJson = json;
            _engine.UpdateSettings(s);
            _icon.ShowBalloonTip(3000, "S1 Panel", "Settings file changed - applied.", ToolTipIcon.Info);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            _icon.ShowBalloonTip(8000, "S1 Panel - settings file problem", ex.Message, ToolTipIcon.Warning);
        }
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_engine.CurrentSettings, _settingsPath, Save);
        _settingsForm.ShowEngineStatus(_engine.Status);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void OpenAbout()
    {
        if (_aboutForm is { IsDisposed: false })
        {
            _aboutForm.Activate();
            return;
        }
        _aboutForm = new AboutForm();
        _aboutForm.FormClosed += (_, _) => _aboutForm = null;
        _aboutForm.Show();
        _aboutForm.Activate();
    }

    private void OnLocationUnavailable(string why) => _ui.Post(_ =>
    {
        // Ask once per run, and only if the user hasn't already given a location.
        if (_askedForZip || _engine.CurrentSettings.Location != "auto")
            return;
        _askedForZip = true;
        string? zip = ZipPrompt.Ask(why);
        if (zip is not null)
            Change(s => s.Location = zip);
    }, null);

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            _engine.Reconnect(); // the panel controller often resets during sleep
    }

    protected override void ExitThreadCore()
    {
        _exiting = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        LocationService.WindowsLocationUnavailable -= OnLocationUnavailable;
        _watcher?.Dispose();
        _settingsForm?.Close();
        _aboutForm?.Close();
        _engine.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _currentIcon?.Dispose();
        base.ExitThreadCore();
    }
}
