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

namespace S1Panel.Tray;

/// <summary>Asks for a ZIP/postal code when Windows can't provide a location.</summary>
internal static class ZipPrompt
{
    /// <returns>The entered code, or null if cancelled.</returns>
    public static string? Ask(string why)
    {
        using var form = new Form
        {
            Text = "S1 Panel - your location",
            Icon = TrayIcons.Create(TrayIcons.Ok),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            TopMost = true,
        };
        var text = new Label
        {
            Text = $"Weather and sunrise/sunset need your location, but Windows didn't provide one.\n\n{why}\n\nEnter your ZIP code (or a city) instead:",
            AutoSize = true,
            MaximumSize = new Size(380, 0),
        };
        var box = new TextBox { Width = 160, Margin = new Padding(3, 10, 3, 3) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Not now", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.AddRange([cancel, ok]);
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        layout.Controls.AddRange([text, box, buttons]);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }
}
