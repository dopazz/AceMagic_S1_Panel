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

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace S1Panel.Tray;

/// <summary>About S1 Panel: version, build time, credits, license and the Ko-fi link.</summary>
internal sealed class AboutForm : Form
{
    public const string KoFiUrl = "https://ko-fi.com/ddopaz";
    public const string UpstreamUrl = "https://github.com/tjaworski/AceMagic-S1-LED-TFT-Linux";
    public const string GplUrl = "https://www.gnu.org/licenses/gpl-3.0.html";

    private const int TextWidth = 400;

    public AboutForm()
    {
        Text = "About S1 Panel";
        Icon = TrayIcons.Create(TrayIcons.Ok);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);

        var title = new Label
        {
            Text = "S1 Panel",
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size * 1.8f, FontStyle.Bold),
            Margin = new Padding(3, 0, 3, 2),
        };
        var build = Para($"Version {VersionText()}\nBuilt {BuildText()}");
        build.ForeColor = SystemColors.GrayText;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 0) };
        ok.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Size = new Size(TextWidth, 34),
            Margin = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.Add(ok);

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        layout.Controls.AddRange([
            title,
            build,
            Para("Shows a clock, system gauges, weather, scenes, photos and more on the AceMagic S1's front panel, " +
                 "and controls its LED strip, without administrator rights."),
            Para($"S1 Panel is built upon the work shared at {UpstreamUrl}, which worked out how the S1's panel and LED strip " +
                 "are driven. Many thanks to tjaworski for figuring it out and sharing it with everyone.",
                 (UpstreamUrl, UpstreamUrl)),
            Para("Copyright (C) 2026 dopaz. S1 Panel is free software under the GNU General Public License, version 3 " +
                 "or later, and comes with NO WARRANTY. Its source code is included in the download (s1panel-source.zip).",
                 ("GNU General Public License, version 3", GplUrl)),
            Para("Developed with Claude Code (Claude Opus 5.5)."),
            Para("If S1 Panel is useful to you, you can buy me a coffee on Ko-fi.", ("buy me a coffee on Ko-fi", KoFiUrl)),
            buttons,
        ]);
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = ok;
    }

    /// <summary>"1.0.0 (a938bbe)": the version, and the commit it was built from when known.</summary>
    internal static string VersionText()
    {
        string? v = typeof(AboutForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(v))
            return typeof(AboutForm).Assembly.GetName().Version?.ToString(3) ?? "?";
        int plus = v.IndexOf('+');
        return plus < 0 ? v : $"{v[..plus]} ({v[(plus + 1)..Math.Min(v.Length, plus + 8)]})";
    }

    /// <summary>When this exe was built, in UTC: "2026-11-17 17:09 UTC" (the BuildTimestamp the project stamps in).</summary>
    internal static string BuildText()
    {
        string? stamp = typeof(AboutForm).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildTimestamp")?.Value;
        return DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
            : "unknown";
    }

    /// <summary>A wrapping paragraph; each (text, url) pair turns that part of it into a link.</summary>
    private static LinkLabel Para(string text, params (string Text, string Url)[] links)
    {
        var label = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(TextWidth, 0),
            Margin = new Padding(3, 8, 3, 0),
            LinkArea = new LinkArea(0, 0),
        };
        foreach (var (part, url) in links)
        {
            int at = text.IndexOf(part, StringComparison.Ordinal);
            if (at >= 0)
                label.Links.Add(at, part.Length, url);
        }
        label.LinkClicked += (_, e) => Open((string)e.Link!.LinkData!);
        return label;
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show($"Couldn't open {url}:\n{ex.Message}", "S1 Panel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
