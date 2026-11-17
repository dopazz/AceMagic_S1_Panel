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

using S1Panel.Core.Rendering.Layouts;
using S1Panel.Core.Services;
using SkiaSharp;

namespace S1Panel.Core.Rendering.Widgets;

/// <summary>
/// Album art, title, artist and a progress bar for whatever Windows' media flyout shows (Spotify, browsers,
/// Media Player...). Only the progress bar and elapsed time change each second while a track plays.
/// </summary>
public sealed class NowPlayingWidget : IWidget, IPendingWork
{
    private static readonly SKColor Accent = new(0x1E, 0xD7, 0x60);

    public string Name => "now-playing";
    public string Description => "Album art, title, artist and progress of the music or video playing";
    public bool Pending { get; private set; }

    public void Draw(SKCanvas c, SKRect area, LayoutContext ctx)
    {
        var state = NowPlayingService.Get();
        Pending = !state.Ready;
        float W = area.Width, H = area.Height, S = Math.Min(W, H);
        float top = area.Top + (H - 160) / 2; // design is 160 tall; centre in a 170-tall half

        if (state.Media is not { } m)
        {
            NoteGlyph(c, new SKPoint(area.MidX, top + 62), S * 0.2f, Gfx.Muted.WithAlpha(150));
            Gfx.TextCentered(c, state.Ready ? state.Problem ?? "Nothing playing" : "Checking…", area.MidX, top + 118, 12, Gfx.Regular, Gfx.Muted, W - 16);
            return;
        }

        // Art (or a placeholder), with a pause badge when paused.
        float a = 80;
        var art = SKRect.Create(area.MidX - a / 2, top + 6, a, a);
        Art(c, art, m.Art);
        if (!m.Playing)
            PauseBadge(c, new SKPoint(art.Right - 4, art.Bottom - 4), 11);

        float maxW = W - 14;
        Gfx.TextCentered(c, Clip(m.Title, 13, Gfx.SemiBold, maxW), area.MidX, top + 104, 13, Gfx.SemiBold, Gfx.Text, maxW);
        string by = m.Artist.Length > 0 ? m.Artist : m.Album;
        if (by.Length > 0)
            Gfx.TextCentered(c, Clip(by, 11, Gfx.Regular, maxW), area.MidX, top + 120, 11, Gfx.Regular, Gfx.Muted, maxW);

        float barY = top + 132;
        if (m.Duration > TimeSpan.Zero)
        {
            var pos = m.PositionNow(DateTime.UtcNow);
            var bar = new SKRect(area.Left + 12, barY, area.Right - 12, barY + 3);
            using var p = new SKPaint { IsAntialias = true, Color = Gfx.Track };
            c.DrawRoundRect(bar, 1.5f, 1.5f, p);
            float f = (float)Math.Clamp(pos / m.Duration, 0, 1);
            if (f > 0)
            {
                p.Color = m.Playing ? Accent : Gfx.Muted;
                c.DrawRoundRect(new SKRect(bar.Left, bar.Top, bar.Left + bar.Width * f, bar.Bottom), 1.5f, 1.5f, p);
            }
            float ty = barY + 15;
            Gfx.TextAt(c, Clock(pos), bar.Left, ty, 9.5f, Gfx.Regular, Gfx.Muted, SKTextAlign.Left);
            Gfx.TextAt(c, Clock(m.Duration), bar.Right, ty, 9.5f, Gfx.Regular, Gfx.Muted, SKTextAlign.Right);
            if (m.App.Length > 0)
                Gfx.TextCentered(c, m.App, area.MidX, ty, 9, Gfx.Regular, Gfx.Muted.WithAlpha(170), W * 0.38f);
        }
        else if (m.App.Length > 0)
        {
            Gfx.TextCentered(c, m.App, area.MidX, barY + 12, 9.5f, Gfx.Regular, Gfx.Muted.WithAlpha(170), maxW);
        }
    }

    private static void Art(SKCanvas c, SKRect r, SKImage? img)
    {
        c.Save();
        using (var clip = new SKRoundRect(r, 8))
            c.ClipRoundRect(clip, antialias: true);
        if (img is not null)
        {
            // cover: fill the square, cropping the longer side
            float k = Math.Max(r.Width / img.Width, r.Height / img.Height);
            float w = img.Width * k, h = img.Height * k;
            c.DrawImage(img, SKRect.Create(r.MidX - w / 2, r.MidY - h / 2, w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
        else
        {
            using var shader = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom),
                [new SKColor(0x2B, 0x3A, 0x5C), new SKColor(0x4A, 0x2B, 0x5C)], SKShaderTileMode.Clamp);
            using var p = new SKPaint { Shader = shader };
            c.DrawRect(r, p);
            NoteGlyph(c, new SKPoint(r.MidX, r.MidY), r.Width * 0.24f, Gfx.Text.WithAlpha(200));
        }
        c.Restore();
        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = Gfx.CardEdge };
        c.DrawRoundRect(Gfx.Inset(r, 0.5f), 8, 8, edge);
    }

    /// <summary>A beamed pair of eighth notes, centred on <paramref name="center"/>.</summary>
    internal static void NoteGlyph(SKCanvas c, SKPoint center, float size, SKColor color)
    {
        using var p = new SKPaint { IsAntialias = true, Color = color };
        float s = size;
        var l = new SKPoint(center.X - s * 0.45f, center.Y + s * 0.6f);
        var r = new SKPoint(center.X + s * 0.55f, center.Y + s * 0.45f);
        c.DrawOval(SKRect.Create(l.X - s * 0.3f, l.Y - s * 0.21f, s * 0.6f, s * 0.42f), p);
        c.DrawOval(SKRect.Create(r.X - s * 0.3f, r.Y - s * 0.21f, s * 0.6f, s * 0.42f), p);
        float stem = s * 0.1f;
        c.DrawRect(l.X + s * 0.2f, l.Y - s * 1.25f, stem, s * 1.25f, p);
        c.DrawRect(r.X + s * 0.2f, r.Y - s * 1.25f, stem, s * 1.25f, p);
        using var b = new SKPathBuilder();
        b.MoveTo(l.X + s * 0.2f, l.Y - s * 1.25f);
        b.LineTo(r.X + s * 0.2f + stem, r.Y - s * 1.4f);
        b.LineTo(r.X + s * 0.2f + stem, r.Y - s * 1.1f);
        b.LineTo(l.X + s * 0.2f, l.Y - s * 0.95f);
        b.Close();
        using var beam = b.Detach();
        c.DrawPath(beam, p);
    }

    private static void PauseBadge(SKCanvas c, SKPoint center, float r)
    {
        using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0x10, 0x14, 0x1D, 230) };
        c.DrawCircle(center, r, p);
        p.Color = Gfx.Text;
        c.DrawRect(center.X - r * 0.4f, center.Y - r * 0.42f, r * 0.27f, r * 0.84f, p);
        c.DrawRect(center.X + r * 0.13f, center.Y - r * 0.42f, r * 0.27f, r * 0.84f, p);
    }

    /// <summary>Trims long text with "…" so it needs at most 20% shrinking to fit (tiny text is unreadable on the panel).</summary>
    internal static string Clip(string text, float size, SKTypeface face, float maxWidth)
    {
        using var font = Gfx.Font(face, size);
        float limit = maxWidth * 1.25f;
        if (font.MeasureText(text) <= limit)
            return text;
        int lo = 1, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (font.MeasureText(text[..mid].TrimEnd() + "…") <= limit) lo = mid; else hi = mid - 1;
        }
        return text[..lo].TrimEnd() + "…";
    }

    private static string Clock(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
}
