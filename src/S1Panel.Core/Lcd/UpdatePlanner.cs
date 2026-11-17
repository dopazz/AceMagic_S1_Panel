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

namespace S1Panel.Core.Lcd;

/// <summary>A rectangle in native framebuffer pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int W, int H)
{
    public int Area => W * H;
    public int Right => X + W;
    public int Bottom => Y + H;

    public PixelRect Union(PixelRect o)
    {
        int x = Math.Min(X, o.X), y = Math.Min(Y, o.Y);
        return new PixelRect(x, y, Math.Max(Right, o.Right) - x, Math.Max(Bottom, o.Bottom) - y);
    }
}

/// <summary>
/// Decides which rectangles to send for a frame. Every partial-update write costs the same (~80 ms for a full
/// 4 KB report) no matter how many pixels it carries, so the goal is the fewest rectangles that cover every
/// changed pixel, each within the report limit (w*h*2 ≤ 4096 bytes, sides ≤ 255).
///
/// Two strategies, and the cheaper one wins:
///  - fixed 64x32 tiles (the original approach; good for scattered changes);
///  - "merged": changed 8x8 cells grouped into row runs, then greedily merged while the union still fits one
///    report (good for changes that straddle tile edges, e.g. text across x=64/128/192/256).
/// </summary>
public static class UpdatePlanner
{
    public const int MaxPixels = LcdPanel.PayloadSize / 2; // 2048
    public const int MaxSide = 255;
    private const int TileW = 64, TileH = 32;
    private const int Cell = 8;
    private const int MaxRunsToMerge = 160; // beyond this, merging is slow and tiles are as good

    private const int W = LcdPanel.Width, H = LcdPanel.Height;

    public static List<PixelRect> Plan(ReadOnlySpan<byte> shown, ReadOnlySpan<byte> next)
    {
        var tiles = Tiles(shown, next);
        if (tiles.Count <= 1)
            return tiles;
        var merged = Merged(shown, next);
        return merged is not null && merged.Count <= tiles.Count ? merged : tiles;
    }

    /// <summary>The original fixed-grid plan: every changed 64x32 tile (exactly one report each).</summary>
    public static List<PixelRect> Tiles(ReadOnlySpan<byte> shown, ReadOnlySpan<byte> next)
    {
        var list = new List<PixelRect>();
        for (int ty = 0; ty < H; ty += TileH)
        {
            int th = Math.Min(TileH, H - ty);
            for (int tx = 0; tx < W; tx += TileW)
            {
                int tw = Math.Min(TileW, W - tx);
                if (Changed(shown, next, tx, ty, tw, th))
                    list.Add(new PixelRect(tx, ty, tw, th));
            }
        }
        return list;
    }

    /// <summary>Fine-grained plan, or null if the change is too widespread for merging to be worthwhile.</summary>
    public static List<PixelRect>? Merged(ReadOnlySpan<byte> shown, ReadOnlySpan<byte> next)
    {
        // 1. Row runs of changed 8x8 cells.
        var rects = new List<PixelRect>();
        int cols = (W + Cell - 1) / Cell, rows = (H + Cell - 1) / Cell;
        int maxRunCells = MaxSide / Cell; // 31 cells = 248 px
        for (int r = 0; r < rows; r++)
        {
            int y = r * Cell, h = Math.Min(Cell, H - y);
            int c = 0;
            while (c < cols)
            {
                if (!Changed(shown, next, c * Cell, y, Math.Min(Cell, W - c * Cell), h)) { c++; continue; }
                int start = c;
                while (c < cols && c - start < maxRunCells && Changed(shown, next, c * Cell, y, Math.Min(Cell, W - c * Cell), h))
                    c++;
                int x = start * Cell;
                rects.Add(new PixelRect(x, y, Math.Min(c * Cell, W) - x, h));
                if (rects.Count > MaxRunsToMerge)
                    return null;
            }
        }

        // 2. Greedy merge: repeatedly join the pair whose union fits one report and wastes the fewest pixels.
        while (rects.Count > 1)
        {
            int bi = -1, bj = -1;
            long bestWaste = long.MaxValue;
            for (int i = 0; i < rects.Count; i++)
            {
                for (int j = i + 1; j < rects.Count; j++)
                {
                    var u = rects[i].Union(rects[j]);
                    if (u.W > MaxSide || u.H > MaxSide || u.Area > MaxPixels)
                        continue;
                    long waste = u.Area - rects[i].Area - rects[j].Area;
                    if (waste < bestWaste)
                    {
                        bestWaste = waste;
                        bi = i;
                        bj = j;
                    }
                }
            }
            if (bi < 0)
                break;
            rects[bi] = rects[bi].Union(rects[bj]);
            rects.RemoveAt(bj);
        }
        return rects;
    }

    private static bool Changed(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int x, int y, int w, int h)
    {
        for (int row = y; row < y + h; row++)
        {
            int o = (row * W + x) * 2;
            if (!a.Slice(o, w * 2).SequenceEqual(b.Slice(o, w * 2)))
                return true;
        }
        return false;
    }
}
