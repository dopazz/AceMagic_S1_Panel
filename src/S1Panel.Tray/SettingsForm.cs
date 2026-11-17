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

using System.Diagnostics;
using S1Panel.Core.Config;
using S1Panel.Core.Led;
using S1Panel.Core.Monitor;
using S1Panel.Core.Rendering.Layouts;
using S1Panel.Core.Services;
using SkiaSharp;

namespace S1Panel.Tray;

/// <summary>
/// Settings window: every option in s1panel.json, with a live preview of exactly what the panel will show.
/// Save / Apply write the file and hand the settings to the running engine.
/// </summary>
internal sealed class SettingsForm : Form
{
    private sealed record Choice(string Value, string Text)
    {
        public override string ToString() => Text;
    }

    private readonly Func<PanelSettings, string?> _save; // returns an error message or null
    private PanelSettings _working;
    private FrameComposer? _preview;
    private bool _loading;

    private readonly ComboBox _layout = DropDown();
    private readonly ComboBox _scene = DropDown();
    private readonly ComboBox _widgetA = DropDown();
    private readonly ComboBox _widgetB = DropDown();
    private readonly Label _widgetALabel = new() { Text = "Left widget" };
    private readonly Label _widgetBLabel = new() { Text = "Right widget" };
    private readonly TextBox _background = new() { Width = 150 };
    private readonly Button _browse = new() { Text = "Browse…", AutoSize = true };
    private readonly Button _clearBg = new() { Text = "Clear", AutoSize = true };
    private readonly ComboBox _orientation = DropDown();
    private readonly ComboBox _textSize = DropDown();
    private readonly NumericUpDown _interval = new() { Minimum = 0.25m, Maximum = 60, DecimalPlaces = 2, Increment = 0.25m, Width = 70 };
    private readonly ComboBox _drive = DropDown();
    private readonly ComboBox _clock = DropDown();
    private readonly ComboBox _units = DropDown();
    private readonly CheckBox _blink = new() { Text = "Blink the colon between hours and minutes", AutoSize = true };
    private readonly CheckBox _ledOn = new() { Text = "Control the LED strip", AutoSize = true };
    private readonly ComboBox _ledMode = DropDown();
    private readonly TrackBar _ledBrightness = Slider();
    private readonly TrackBar _ledSpeed = Slider();
    private readonly ComboBox _ledPort = DropDown();
    private readonly Label _brightnessValue = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
    private readonly Label _speedValue = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
    private readonly TextBox _location = new() { Width = 170, PlaceholderText = "auto" };
    private readonly Button _detect = new() { Text = "Detect", AutoSize = true };
    private readonly Label _locationStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(330, 0) };
    private readonly TextBox _photoFolder = new() { Width = 170 };
    private readonly Button _photoBrowse = new() { Text = "Browse…", AutoSize = true };
    private readonly NumericUpDown _photoInterval = new() { Minimum = 5, Maximum = 86_400, Increment = 5, Width = 80 };
    private readonly CheckBox _photoShuffle = new() { Text = "Shuffle", AutoSize = true };
    private readonly ComboBox _photoFit = DropDown();
    private readonly PictureBox _picture = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(0x10, 0x14, 0x1D) };
    private readonly Label _problem = new() { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(420, 0) };
    private readonly Label _engineStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    public SettingsForm(PanelSettings current, string settingsPath, Func<PanelSettings, string?> save)
    {
        _save = save;
        _working = SettingsStore.Clone(current);

        Text = "S1 Panel settings";
        Icon = TrayIcons.Create(TrayIcons.Ok);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10);

        FillChoices();

        var screenGrid = Grid(
            ("Layout", _layout),
            (_widgetALabel, _widgetA),
            (_widgetBLabel, _widgetB),
            ("Scene", _scene),
            ("Scene picture", Row(_background, _browse, _clearBg)),
            ("Orientation", _orientation),
            ("Text size", _textSize),
            ("Update every", Row(_interval, new Label { Text = "seconds", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) })),
            ("Disk", _drive));
        var widgetsGrid = Grid(
            ("Location", Row(_location, _detect)),
            ("", _locationStatus),
            ("", new Label { Text = "auto = Windows location; or a ZIP code, a city, or lat,lon", AutoSize = true, ForeColor = SystemColors.GrayText }),
            ("Photos folder", Row(_photoFolder, _photoBrowse)),
            ("Change every", Row(_photoInterval, new Label { Text = "seconds", AutoSize = true, Margin = new Padding(3, 6, 3, 3) })),
            ("Fit", _photoFit),
            ("", _photoShuffle));
        var clockGrid = Grid(
            ("Clock", _clock),
            ("Temperature", _units),
            ("", _blink));
        var ledGrid = Grid(
            ("", _ledOn),
            ("Effect", _ledMode),
            ("Brightness", Row(_ledBrightness, _brightnessValue)),
            ("Speed", Row(_ledSpeed, _speedValue)),
            ("Port", _ledPort));

        var tabs = new TabControl { Margin = new Padding(0) };
        var pages = new[] { ("Screen", screenGrid), ("Widgets", widgetsGrid), ("Clock && units", clockGrid), ("LED strip", ledGrid) };
        Size biggest = Size.Empty;
        foreach (var (title, grid) in pages)
        {
            var page = new TabPage(title) { Padding = new Padding(10), UseVisualStyleBackColor = true };
            grid.Dock = DockStyle.Top;
            page.Controls.Add(grid);
            tabs.TabPages.Add(page);
            var pref = grid.GetPreferredSize(Size.Empty);
            biggest = new Size(Math.Max(biggest.Width, pref.Width), Math.Max(biggest.Height, pref.Height));
        }
        tabs.Size = new Size(biggest.Width + 36, Math.Max(biggest.Height + 60, 380));
        var left = tabs;

        _picture.Size = new Size(360, 360);
        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(12, 6, 0, 0) };
        right.Controls.Add(new Label { Text = "Preview (exactly as the panel shows it)", AutoSize = true });
        right.Controls.Add(_picture);
        right.Controls.Add(_engineStatus);
        right.Controls.Add(_problem);

        var saveButton = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.None };
        var apply = new Button { Text = "Apply", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var openFile = new LinkLabel { Text = "Open settings file", AutoSize = true, Margin = new Padding(3, 8, 20, 3) };
        saveButton.Click += (_, _) => { if (Commit()) Close(); };
        apply.Click += (_, _) => Commit();
        openFile.LinkClicked += (_, _) =>
        {
            if (File.Exists(settingsPath))
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{settingsPath}\"") { UseShellExecute = true });
            else
                MessageBox.Show(this, $"No settings file yet. Save once to create:\n{settingsPath}", Text);
        };
        AcceptButton = saveButton;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.AddRange([cancel, apply, saveButton, openFile]);

        var root = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);
        root.Controls.Add(buttons, 0, 1);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);

        LoadControls(_working);
        WireEvents();
        RebuildPreview();

        _tick.Tick += (_, _) =>
        {
            RenderPreview();
            if (_detect.Enabled && _locationStatus.Text != "Looking up…")
                _locationStatus.Text = LocationService.Get(_working.Location) is var st && st.Status == LocationStatus.Ready
                    ? $"✓ {st.Location}" : st.Message;
        };
        _tick.Start();
        FormClosed += (_, _) =>
        {
            _tick.Stop();
            _preview?.Dispose();
            _picture.Image?.Dispose();
        };
    }

    public void ShowEngineStatus(EngineStatus status) => _engineStatus.Text = $"Panel: {status}";

    // ───────────── building blocks ─────────────

    private static ComboBox DropDown() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };

    private static TrackBar Slider() => new() { Minimum = 1, Maximum = 5, TickFrequency = 1, LargeChange = 1, Width = 200, AutoSize = false, Height = 32 };

    private static GroupBox Group(string title, Control content)
    {
        var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), Margin = new Padding(0, 0, 0, 8), Dock = DockStyle.Fill };
        content.Dock = DockStyle.Fill;
        g.Controls.Add(content);
        return g;
    }

    private static TableLayoutPanel Grid(params (object Label, Control Control)[] rows)
    {
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var (label, control) in rows)
        {
            t.RowCount++;
            var l = label as Label ?? new Label { Text = (string)label };
            l.AutoSize = true;
            l.MinimumSize = new Size(96, 0);
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(3, 7, 3, 3);
            t.Controls.Add(l);
            t.Controls.Add(control);
        }
        return t;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        f.Controls.AddRange(controls);
        return f;
    }

    private void FillChoices()
    {
        foreach (var l in LayoutRegistry.All)
            _layout.Items.Add(new Choice(l.Name, $"{char.ToUpper(l.Name[0])}{l.Name[1..]} - {l.Description}"));
        _layout.DropDownWidth = 460;

        foreach (var (name, description) in S1Panel.Core.Rendering.Widgets.WidgetRegistry.All)
        {
            _widgetA.Items.Add(new Choice(name, description));
            _widgetB.Items.Add(new Choice(name, description));
        }

        string seasonNow = SceneArt.SeasonOf(DateTime.Now).ToString().ToLowerInvariant();
        _scene.Items.AddRange([
            new Choice("auto", $"Follow the season (now: {seasonNow})"),
            new Choice("winter", "Winter"), new Choice("spring", "Spring"),
            new Choice("summer", "Summer"), new Choice("autumn", "Autumn"),
        ]);
        _orientation.Items.AddRange([
            new Choice("landscape", "Landscape (PC lying flat)"), new Choice("portrait", "Portrait (PC standing up)"),
            new Choice("landscape-flipped", "Landscape, upside down"), new Choice("portrait-flipped", "Portrait, upside down"),
        ]);
        _textSize.Items.AddRange([new Choice("normal", "Normal"), new Choice("large", "Large"), new Choice("larger", "Larger")]);

        bool h12 = PanelSettings.ParseClock12("auto"), f = PanelSettings.ParseFahrenheit("auto");
        _clock.Items.AddRange([
            new Choice("auto", $"Windows setting ({(h12 ? "12-hour" : "24-hour")})"),
            new Choice("12", "12-hour (1:30 PM)"), new Choice("24", "24-hour (13:30)"),
        ]);
        _units.Items.AddRange([
            new Choice("auto", $"Windows region ({(f ? "°F" : "°C")})"),
            new Choice("metric", "Celsius (°C)"), new Choice("imperial", "Fahrenheit (°F)"),
        ]);

        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            _drive.Items.Add(new Choice(d.Name.TrimEnd('\\'), $"{d.Name.TrimEnd('\\')}  {d.VolumeLabel}".Trim()));
        _photoFit.Items.AddRange([new Choice("cover", "Fill (crop edges)"), new Choice("contain", "Whole picture (letterbox)")]);

        _ledMode.Items.AddRange([
            new Choice("breathing", "Breathing"), new Choice("rainbow", "Rainbow"),
            new Choice("cycle", "Colour cycle"), new Choice("auto", "Automatic"), new Choice("off", "Off"),
        ]);
        _ledPort.Items.Add(new Choice("", "Auto-detect"));
        foreach (string port in LedStrip.FindPorts())
            _ledPort.Items.Add(new Choice(port, port));
    }

    private static void Select(ComboBox box, string? value)
    {
        foreach (Choice c in box.Items)
        {
            if (string.Equals(c.Value, value ?? "", StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = c;
                return;
            }
        }
        if (value is { Length: > 0 }) // a value from the file we don't list (e.g. text size 1.2, an unplugged drive)
        {
            var extra = new Choice(value, value);
            box.Items.Add(extra);
            box.SelectedItem = extra;
        }
        else if (box.Items.Count > 0)
        {
            box.SelectedIndex = 0;
        }
    }

    private static string Level(int v, string low, string high) => v switch { 1 => $"1 ({low})", 5 => $"5 ({high})", _ => v.ToString() };

    private static string Value(ComboBox box) => (box.SelectedItem as Choice)?.Value ?? "";

    // ───────────── data flow ─────────────

    private void LoadControls(PanelSettings s)
    {
        _loading = true;
        Select(_layout, s.Layout);
        Select(_scene, s.Scene);
        Select(_widgetA, s.Widgets.ElementAtOrDefault(0));
        Select(_widgetB, s.Widgets.ElementAtOrDefault(1));
        _background.Text = s.Background ?? "";
        Select(_orientation, s.Orientation);
        Select(_textSize, s.TextSize);
        _interval.Value = Math.Clamp(s.IntervalMs / 1000m, _interval.Minimum, _interval.Maximum);
        Select(_drive, s.Drive.TrimEnd('\\'));
        Select(_clock, s.Clock);
        Select(_units, s.Units);
        _blink.Checked = s.Blink;
        var led = s.Led ?? new LedSettings();
        _ledOn.Checked = s.Led is not null;
        Select(_ledMode, led.Mode);
        _ledBrightness.Value = Math.Clamp(led.Intensity, 1, 5);
        _ledSpeed.Value = Math.Clamp(led.Speed, 1, 5);
        Select(_ledPort, led.Port ?? "");
        _location.Text = s.Location == "auto" ? "" : s.Location;
        _photoFolder.Text = s.Slideshow.Folder ?? "";
        _photoFolder.PlaceholderText = s.Slideshow.ResolvedFolder;
        _photoInterval.Value = Math.Clamp(s.Slideshow.IntervalSeconds, (int)_photoInterval.Minimum, (int)_photoInterval.Maximum);
        _photoShuffle.Checked = s.Slideshow.Shuffle;
        Select(_photoFit, s.Slideshow.Fit);
        _brightnessValue.Text = Level(_ledBrightness.Value, "dim", "bright");
        _speedValue.Text = Level(_ledSpeed.Value, "slow", "fast");
        _loading = false;
        UpdateEnabled();
    }

    private PanelSettings ReadControls()
    {
        var s = SettingsStore.Clone(_working); // keeps fields not shown here (device, timings)
        s.Layout = Value(_layout);
        s.Scene = Value(_scene);
        s.Widgets = [Value(_widgetA), Value(_widgetB)];
        s.Background = string.IsNullOrWhiteSpace(_background.Text) ? null : _background.Text.Trim();
        s.Orientation = Value(_orientation);
        s.TextSize = Value(_textSize);
        s.IntervalMs = (int)(_interval.Value * 1000);
        s.Drive = Value(_drive) is { Length: > 0 } d ? d : s.Drive;
        s.Clock = Value(_clock);
        s.Units = Value(_units);
        s.Blink = _blink.Checked;
        s.Led = _ledOn.Checked
            ? new LedSettings { Mode = Value(_ledMode), Intensity = _ledBrightness.Value, Speed = _ledSpeed.Value, Port = Value(_ledPort) is { Length: > 0 } p ? p : null }
            : null;
        s.Location = string.IsNullOrWhiteSpace(_location.Text) ? "auto" : _location.Text.Trim();
        s.Slideshow = new SlideshowSettings
        {
            Folder = string.IsNullOrWhiteSpace(_photoFolder.Text) ? null : _photoFolder.Text.Trim(),
            IntervalSeconds = (int)_photoInterval.Value,
            Shuffle = _photoShuffle.Checked,
            Fit = Value(_photoFit) is { Length: > 0 } fit ? fit : "cover",
        };
        return s;
    }

    private void WireEvents()
    {
        foreach (var box in new[] { _layout, _widgetA, _widgetB, _scene, _orientation, _textSize, _drive, _clock, _units, _ledMode, _ledPort })
            box.SelectedIndexChanged += (_, _) => Changed();
        foreach (var check in new[] { _blink, _ledOn, _photoShuffle })
            check.CheckedChanged += (_, _) => Changed();
        _interval.ValueChanged += (_, _) => Changed();
        _photoInterval.ValueChanged += (_, _) => Changed();
        _photoFit.SelectedIndexChanged += (_, _) => Changed();
        _location.Leave += (_, _) => Changed();
        _photoFolder.Leave += (_, _) => Changed();
        _photoBrowse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Pictures for the photos widget", InitialDirectory = _photoFolder.PlaceholderText };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _photoFolder.Text = dlg.SelectedPath;
                Changed();
            }
        };
        _detect.Click += async (_, _) =>
        {
            string query = string.IsNullOrWhiteSpace(_location.Text) ? "auto" : _location.Text.Trim();
            _detect.Enabled = false;
            _locationStatus.Text = "Looking up…";
            var state = await LocationService.ResolveAsync(query, CancellationToken.None);
            _locationStatus.Text = state.Message;
            _detect.Enabled = true;
            Changed();
        };
        _ledBrightness.ValueChanged += (_, _) => Changed();
        _ledSpeed.ValueChanged += (_, _) => Changed();
        _ledBrightness.ValueChanged += (_, _) => _brightnessValue.Text = Level(_ledBrightness.Value, "dim", "bright");
        _ledSpeed.ValueChanged += (_, _) => _speedValue.Text = Level(_ledSpeed.Value, "slow", "fast");
        _background.Leave += (_, _) => Changed();
        _clearBg.Click += (_, _) => { _background.Text = ""; Changed(); };
        _browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*", Title = "Scene picture" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _background.Text = dlg.FileName;
                Changed();
            }
        };
    }

    private void Changed()
    {
        if (_loading)
            return;
        UpdateEnabled();
        RebuildPreview();
    }

    private void UpdateEnabled()
    {
        bool split = Value(_layout) == "split";
        _widgetA.Enabled = _widgetB.Enabled = split;
        bool portrait = Value(_orientation).StartsWith("portrait");
        _widgetALabel.Text = portrait ? "Top widget" : "Left widget";
        _widgetBLabel.Text = portrait ? "Bottom widget" : "Right widget";
        bool scene = Value(_layout) == "scene";
        _scene.Enabled = scene && string.IsNullOrWhiteSpace(_background.Text);
        _background.Enabled = _browse.Enabled = _clearBg.Enabled = scene;
        bool clockLayout = Value(_layout) == "clock";
        _blink.Enabled = !clockLayout;
        _blink.Text = clockLayout ? "Blink the colon (the clock layout never blinks)" : "Blink the colon between hours and minutes";
        _ledMode.Enabled = _ledBrightness.Enabled = _ledSpeed.Enabled = _ledPort.Enabled = _ledOn.Checked;
    }

    /// <summary>Validates the controls; on success rebuilds the preview composer, otherwise shows why.</summary>
    private bool RebuildPreview()
    {
        PanelSettings s;
        try
        {
            s = ReadControls();
            s.Validate();
            var composer = new FrameComposer(s);
            _preview?.Dispose();
            _preview = composer;
            _working = s;
            _problem.Text = "";
        }
        catch (ArgumentException ex)
        {
            _problem.Text = ex.Message;
            return false;
        }
        RenderPreview();
        return true;
    }

    private void RenderPreview()
    {
        if (_preview is null)
            return;
        _preview.ComposeNext();
        using var shown = _preview.RenderAsPanelShows();
        // 2x nearest-neighbour so the preview shows real panel pixels
        using var big = new SKBitmap(shown.Width * 2, shown.Height * 2);
        shown.ScalePixels(big, new SKSamplingOptions(SKFilterMode.Nearest));
        using var data = big.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        var old = _picture.Image;
        _picture.Image = new Bitmap(ms);
        old?.Dispose();
    }

    private bool Commit()
    {
        if (!RebuildPreview())
            return false;
        string? error = _save(_working);
        if (error is not null)
        {
            _problem.Text = error;
            return false;
        }
        _problem.Text = "";
        return true;
    }
}
