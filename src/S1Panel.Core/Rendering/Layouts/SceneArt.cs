using SkiaSharp;

namespace S1Panel.Core.Rendering.Layouts;

public enum Season { Winter, Spring, Summer, Autumn }

/// <summary>
/// Procedurally drawn backgrounds for the scene layout, one per season. Everything is deterministic
/// (seeded), so a background never changes between frames and costs nothing to keep on screen.
/// All four work at 320x170 and 170x320, and keep the centre/lower-middle calm for the clock.
/// </summary>
public static class SceneArt
{
    /// <summary>Season for a date, northern hemisphere (Dec-Feb winter, Mar-May spring, Jun-Aug summer, Sep-Nov autumn).</summary>
    public static Season SeasonOf(DateTime t) => t.Month switch
    {
        12 or 1 or 2 => Season.Winter,
        3 or 4 or 5 => Season.Spring,
        6 or 7 or 8 => Season.Summer,
        _ => Season.Autumn,
    };

    /// <summary>autumn | winter | spring | summer | auto. Returns null for auto.</summary>
    public static Season? Parse(string value) => value.Trim().ToLowerInvariant() switch
    {
        "auto" => null,
        "winter" => Season.Winter,
        "spring" => Season.Spring,
        "summer" => Season.Summer,
        "autumn" or "fall" => Season.Autumn,
        _ => throw new ArgumentException($"scene must be autumn, winter, spring, summer or auto (got '{value}')."),
    };

    /// <summary>How much to darken behind the clock so white text stays readable (0 = none).</summary>
    public static byte ScrimAlpha(Season s) => s switch
    {
        Season.Spring => 0x50,
        Season.Summer => 0x58,
        _ => 0,
    };

    public static void Draw(SKCanvas c, int w, int h, Season season)
    {
        switch (season)
        {
            case Season.Winter: Winter(c, w, h); break;
            case Season.Spring: Spring(c, w, h); break;
            case Season.Summer: Summer(c, w, h); break;
            default: Autumn(c, w, h); break;
        }
    }

    // ───────────────────────────── Autumn: dusk over purple mountains ─────────────────────────────

    public static void Autumn(SKCanvas c, int w, int h)
    {
        float horizon = h * 0.72f;
        Sky(c, w, horizon, h,
            [new SKColor(0x1C, 0x2A, 0x55), new SKColor(0x5B, 0x4A, 0x8C), new SKColor(0xE8, 0x8E, 0x7A), new SKColor(0xF6, 0xC9, 0x8E)],
            [0f, 0.45f, 0.82f, 1f]);

        var sun = new SKPoint(w * 0.7f, horizon - h * (h > w ? 0.42f : 0.5f));
        float sunR = Math.Min(w, h) * 0.07f;
        Glow(c, w, h, sun, Math.Max(w, h) * 0.4f, new SKColor(0xFF, 0xD8, 0xA0, 0x90));
        Disc(c, sun, sunR, new SKColor(0xFF, 0xF4, 0xDC));

        // Far to near: lighter/hazier to darker. Peaks rise toward the far layers.
        (float Base, float Amp, uint Seed, SKColor Color)[] layers =
        [
            (horizon - h * 0.18f, h * 0.22f, 7, new SKColor(0x8C, 0x6E, 0x9E)),
            (horizon - h * 0.06f, h * 0.18f, 21, new SKColor(0x5E, 0x48, 0x7A)),
            (horizon + h * 0.06f, h * 0.16f, 42, new SKColor(0x37, 0x2B, 0x52)),
            (horizon + h * 0.18f, h * 0.12f, 99, new SKColor(0x1A, 0x15, 0x2B)),
        ];
        foreach (var (baseY, amp, seed, color) in layers)
        {
            using var path = Ridge(w, h, baseY, amp, seed);
            using var p = new SKPaint { Color = color, IsAntialias = true };
            c.DrawPath(path, p);
        }
    }

    // ───────────────────────────── Winter: moonlit snowy peaks and pines ─────────────────────────────

    public static void Winter(SKCanvas c, int w, int h)
    {
        float horizon = h * 0.70f;
        Sky(c, w, horizon, h,
            [new SKColor(0x07, 0x0D, 0x26), new SKColor(0x16, 0x26, 0x52), new SKColor(0x3E, 0x5C, 0x95), new SKColor(0x8F, 0xAE, 0xD4)],
            [0f, 0.45f, 0.8f, 1f]);

        // Stars, denser toward the top.
        var rng = new Rng(2024);
        using (var star = new SKPaint { IsAntialias = true })
        {
            int count = w * h / 700;
            for (int i = 0; i < count; i++)
            {
                float x = rng.Next() * w, y = rng.Next() * rng.Next() * horizon * 0.85f;
                star.Color = new SKColor(0xFF, 0xFF, 0xFF, (byte)(90 + rng.Next() * 165));
                c.DrawCircle(x, y, 0.35f + rng.Next() * 0.75f, star);
            }
        }

        // Full moon with a cold halo and a few soft "seas".
        var moon = new SKPoint(w * (h > w ? 0.72f : 0.8f), h * (h > w ? 0.14f : 0.2f));
        float moonR = Math.Min(w, h) * 0.075f;
        Glow(c, w, h, moon, moonR * 6, new SKColor(0xC8, 0xDC, 0xFF, 0x70));
        Disc(c, moon, moonR, new SKColor(0xF4, 0xF6, 0xFF), haloScale: 1.6f);
        // Lunar "seas": soft, irregular, off-centre patches (deliberately not arranged like a face).
        using (var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, moonR * 0.08f))
        using (var maria = new SKPaint { IsAntialias = true, Color = new SKColor(0xB8, 0xC4, 0xDC, 0x90), MaskFilter = blur })
        {
            c.Save();
            using (var clipBuilder = new SKPathBuilder())
            {
                clipBuilder.AddCircle(moon.X, moon.Y, moonR * 0.98f);
                using var clip = clipBuilder.Detach();
                c.ClipPath(clip, antialias: true);
            }
            c.DrawOval(SKRect.Create(moon.X - moonR * 0.75f, moon.Y - moonR * 0.55f, moonR * 0.85f, moonR * 0.55f), maria);
            c.DrawOval(SKRect.Create(moon.X - moonR * 0.1f, moon.Y - moonR * 0.35f, moonR * 0.5f, moonR * 0.75f), maria);
            c.DrawOval(SKRect.Create(moon.X - moonR * 0.55f, moon.Y + moonR * 0.15f, moonR * 0.45f, moonR * 0.3f), maria);
            c.Restore();
        }

        // Snow-capped ranges: each ridge is shaded white at the peaks fading to rock below.
        (float Base, float Amp, uint Seed, SKColor Rock, SKColor Snow)[] ranges =
        [
            (horizon - h * 0.2f, h * 0.36f, 5, new SKColor(0x4A, 0x5E, 0x85), new SKColor(0xE6, 0xEE, 0xFA)),
            (horizon - h * 0.04f, h * 0.24f, 17, new SKColor(0x2E, 0x3D, 0x62), new SKColor(0xC9, 0xD7, 0xEC)),
        ];
        foreach (var (baseY, amp, seed, rock, snow) in ranges)
        {
            using var path = Ridge(w, h, baseY, amp, seed);
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, baseY - amp * 0.75f), new SKPoint(0, baseY + amp * 0.15f),
                [snow, snow, rock], [0f, 0.35f, 1f], SKShaderTileMode.Clamp);
            using var p = new SKPaint { Shader = shader, IsAntialias = true };
            c.DrawPath(path, p);
        }

        // Snowy foreground hills.
        using (var hills = SmoothHills(w, h, horizon + h * 0.12f, h * 0.05f, 3, 31))
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, horizon), new SKPoint(0, h),
                   [new SKColor(0xDC, 0xE6, 0xF4), new SKColor(0x9F, 0xB4, 0xD3)], SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = shader, IsAntialias = true })
            c.DrawPath(hills, p);

        // Pines at the edges, keeping the middle clear for the clock.
        var trees = new Rng(77);
        using (var pine = new SKPaint { IsAntialias = true, Color = new SKColor(0x0E, 0x1A, 0x2C) })
        {
            float scale = Math.Min(w, h) / 170f;
            int n = Math.Max(5, w / 40);
            for (int i = 0; i < n; i++)
            {
                bool left = i % 2 == 0;
                float x = left ? trees.Next() * w * 0.22f : w - trees.Next() * w * 0.22f;
                float baseY = horizon + h * (0.14f + trees.Next() * 0.12f);
                float th = (22 + trees.Next() * 26) * scale;
                Pine(c, x, baseY, th, pine);
            }
        }

        // A little falling snow.
        var flakes = new Rng(909);
        using (var flake = new SKPaint { IsAntialias = true })
        {
            int count = w * h / 900;
            for (int i = 0; i < count; i++)
            {
                flake.Color = new SKColor(0xFF, 0xFF, 0xFF, (byte)(120 + flakes.Next() * 110));
                c.DrawCircle(flakes.Next() * w, flakes.Next() * h, 0.6f + flakes.Next() * 1.1f, flake);
            }
        }
    }

    // ───────────────────────────── Spring: blossom over green hills ─────────────────────────────

    public static void Spring(SKCanvas c, int w, int h)
    {
        float horizon = h * 0.66f;
        Sky(c, w, horizon, h,
            [new SKColor(0x6F, 0xB2, 0xE8), new SKColor(0xB4, 0xDA, 0xF4), new SKColor(0xFC, 0xE4, 0xEC)],
            [0f, 0.6f, 1f]);

        var sun = new SKPoint(w * (h > w ? 0.75f : 0.22f), h * (h > w ? 0.12f : 0.2f));
        Glow(c, w, h, sun, Math.Max(w, h) * 0.35f, new SKColor(0xFF, 0xFB, 0xE6, 0xB0));
        Disc(c, sun, Math.Min(w, h) * 0.06f, new SKColor(0xFF, 0xFD, 0xF2));

        // Fluffy clouds.
        var rng = new Rng(314);
        float s = Math.Min(w, h) / 170f;
        Cloud(c, w * 0.62f, h * 0.16f, 34 * s, rng);
        Cloud(c, w * 0.9f, h * 0.3f, 24 * s, rng);
        if (h > w) Cloud(c, w * 0.25f, h * 0.3f, 22 * s, rng);

        // Rolling hills, far to near.
        (float Base, float Amp, int Waves, uint Seed, SKColor Top, SKColor Bottom)[] hills =
        [
            (horizon - h * 0.04f, h * 0.05f, 2, 3, new SKColor(0x9C, 0xC8, 0xB4), new SKColor(0x86, 0xB7, 0x9F)),
            (horizon + h * 0.06f, h * 0.06f, 3, 8, new SKColor(0x86, 0xC4, 0x6C), new SKColor(0x63, 0xA8, 0x52)),
            (horizon + h * 0.18f, h * 0.05f, 2, 13, new SKColor(0x5C, 0xA6, 0x48), new SKColor(0x3F, 0x86, 0x36)),
        ];
        foreach (var (baseY, amp, waves, seed, top, bottom) in hills)
        {
            using var path = SmoothHills(w, h, baseY, amp, waves, seed);
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, baseY - amp), new SKPoint(0, h), [top, bottom], SKShaderTileMode.Clamp);
            using var p = new SKPaint { Shader = shader, IsAntialias = true };
            c.DrawPath(path, p);
        }

        // Wildflowers on the nearest hill.
        var flowers = new Rng(55);
        using (var f = new SKPaint { IsAntialias = true })
        {
            SKColor[] colors = [new(0xFF, 0xF1, 0x76), new(0xFF, 0xFF, 0xFF), new(0xF7, 0x9F, 0xC5)];
            int count = w * h / 450;
            for (int i = 0; i < count; i++)
            {
                f.Color = colors[i % colors.Length];
                c.DrawCircle(flowers.Next() * w, horizon + h * 0.2f + flowers.Next() * h * 0.14f, 0.7f + flowers.Next() * 0.9f, f);
            }
        }

        // Cherry tree reaching in from the left edge.
        BlossomTree(c, w, h, new Rng(808));

        // Drifting petals.
        var petals = new Rng(4242);
        using (var petal = new SKPaint { IsAntialias = true })
        {
            int count = w * h / 1100;
            for (int i = 0; i < count; i++)
            {
                petal.Color = new SKColor(0xF9, 0xB8, 0xD2, (byte)(150 + petals.Next() * 100));
                float x = petals.Next() * w * 0.75f, y = petals.Next() * h * 0.85f;
                c.Save();
                c.RotateDegrees(petals.Next() * 180, x, y);
                c.DrawOval(new SKRect(x - 1.8f, y - 1f, x + 1.8f, y + 1f), petal);
                c.Restore();
            }
        }
    }

    // ───────────────────────────── Summer: beach, sea and a palm ─────────────────────────────

    public static void Summer(SKCanvas c, int w, int h)
    {
        float sea = h * (h > w ? 0.56f : 0.58f);
        Sky(c, w, sea, h,
            [new SKColor(0x12, 0x6C, 0xCF), new SKColor(0x45, 0xA8, 0xEC), new SKColor(0xB6, 0xE4, 0xF8)],
            [0f, 0.55f, 1f]);

        var sun = new SKPoint(w * (h > w ? 0.3f : 0.18f), h * (h > w ? 0.12f : 0.2f));
        float sunR = Math.Min(w, h) * 0.075f;
        Glow(c, w, h, sun, Math.Max(w, h) * 0.45f, new SKColor(0xFF, 0xFA, 0xD8, 0xA0));
        Disc(c, sun, sunR, new SKColor(0xFF, 0xFC, 0xE8), haloScale: 2.2f);

        var rng = new Rng(1717);
        float s = Math.Min(w, h) / 170f;
        Cloud(c, w * 0.58f, h * 0.14f, 26 * s, rng);
        Cloud(c, w * 0.84f, h * 0.26f, 20 * s, rng);

        // Sea: lighter at the horizon, deeper toward the shore, with sun glints.
        float shore = h * (h > w ? 0.8f : 0.82f);
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, sea), new SKPoint(0, shore),
                   [new SKColor(0x3C, 0xB9, 0xD3), new SKColor(0x15, 0x8E, 0xBA), new SKColor(0x1F, 0xB0, 0xC4)], [0f, 0.6f, 1f], SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = shader })
            c.DrawRect(0, sea, w, h - sea, p);
        using (var line = new SKPaint { Color = new SKColor(0xE8, 0xF8, 0xFF, 0xC0), StrokeWidth = 1, IsAntialias = true })
            c.DrawLine(0, sea + 0.5f, w, sea + 0.5f, line);
        var glints = new Rng(66);
        using (var g = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round })
        {
            int count = w * h / 500;
            for (int i = 0; i < count; i++)
            {
                float y = sea + 2 + glints.Next() * (shore - sea - 4);
                float x = glints.Next() * w;
                float len = 2 + glints.Next() * 6;
                g.StrokeWidth = 0.8f;
                g.Color = new SKColor(0xFF, 0xFF, 0xFF, (byte)(60 + glints.Next() * 150));
                c.DrawLine(x, y, x + len, y, g);
            }
        }

        // Sand with a wavy waterline and surf.
        using var builder = new SKPathBuilder();
        builder.MoveTo(0, h);
        for (int i = 0; i <= 48; i++)
        {
            float x = w * i / 48f;
            builder.LineTo(x, shore + (float)Math.Sin(x / w * Math.PI * 3.2) * h * 0.012f + (float)Math.Sin(x / w * Math.PI * 9) * h * 0.005f);
        }
        builder.LineTo(w, h);
        builder.Close();
        using var sand = builder.Detach();
        using (var surf = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, Color = new SKColor(0xFF, 0xFF, 0xFF, 0xD0) })
        {
            c.Save();
            c.Translate(0, -1.5f);
            c.DrawPath(sand, surf);
            c.Restore();
        }
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, shore), new SKPoint(0, h),
                   [new SKColor(0xF3, 0xDC, 0xA8), new SKColor(0xE2, 0xBE, 0x80)], SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = shader, IsAntialias = true })
            c.DrawPath(sand, p);

        Palm(c, w, h);
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private static void Sky(SKCanvas c, int w, float horizon, int h, SKColor[] colors, float[] stops)
    {
        using var sky = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, horizon), colors, stops, SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = sky };
        c.DrawRect(0, 0, w, h, p);
    }

    private static void Glow(SKCanvas c, int w, int h, SKPoint center, float radius, SKColor color)
    {
        using var glow = SKShader.CreateRadialGradient(center, radius, [color, color.WithAlpha(0)], SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = glow };
        c.DrawRect(0, 0, w, h, p);
    }

    /// <summary>A solid disc with a soft edge halo (sun or moon).</summary>
    private static void Disc(SKCanvas c, SKPoint center, float r, SKColor color, float haloScale = 2.6f)
    {
        float outer = r * haloScale;
        using var halo = SKShader.CreateRadialGradient(center, outer,
            [color, color, color.WithAlpha(0)], [0f, r / outer, 1f], SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = halo, IsAntialias = true };
        c.DrawCircle(center, outer, p);
    }

    private static void Cloud(SKCanvas c, float cx, float cy, float size, Rng rng)
    {
        using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0xFF, 0xFF, 0xFF, 0xE6) };
        using var shade = new SKPaint { IsAntialias = true, Color = new SKColor(0xD5, 0xE4, 0xF2, 0xC0) };
        c.DrawOval(new SKRect(cx - size, cy + size * 0.05f, cx + size, cy + size * 0.42f), shade);
        for (int i = 0; i < 5; i++)
        {
            float x = cx + (i - 2) * size * 0.38f + (rng.Next() - 0.5f) * size * 0.1f;
            float r = size * (0.28f + rng.Next() * 0.16f) * (i is 1 or 2 or 3 ? 1.25f : 0.9f);
            c.DrawCircle(x, cy, r, p);
        }
        c.DrawOval(new SKRect(cx - size * 0.95f, cy - size * 0.05f, cx + size * 0.95f, cy + size * 0.3f), p);
    }

    private static void Pine(SKCanvas c, float x, float baseY, float height, SKPaint paint)
    {
        using var b = new SKPathBuilder();
        // three stacked tiers
        for (int tier = 0; tier < 3; tier++)
        {
            float top = baseY - height + tier * height * 0.22f;
            float bottom = baseY - height * 0.12f - (2 - tier) * height * 0.18f;
            float half = height * (0.18f + tier * 0.07f);
            b.MoveTo(x, top);
            b.LineTo(x + half, bottom);
            b.LineTo(x - half, bottom);
            b.Close();
        }
        b.AddRect(new SKRect(x - height * 0.03f, baseY - height * 0.14f, x + height * 0.03f, baseY));
        using var path = b.Detach();
        c.DrawPath(path, paint);
    }

    private static void BlossomTree(SKCanvas c, int w, int h, Rng rng)
    {
        bool portrait = h > w;
        float s = Math.Min(w, h) / 170f;
        var trunkBase = new SKPoint(-6 * s, h * (portrait ? 0.62f : 0.95f));
        var trunkTop = new SKPoint(w * (portrait ? 0.3f : 0.17f), h * (portrait ? 0.2f : 0.18f));

        using var bark = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, Color = new SKColor(0x4A, 0x32, 0x2C) };
        var ends = new List<SKPoint>();

        void Branch(SKPoint from, float angleDeg, float length, float width, int depth)
        {
            double a = angleDeg * Math.PI / 180;
            var to = new SKPoint(from.X + (float)Math.Cos(a) * length, from.Y + (float)Math.Sin(a) * length);
            bark.StrokeWidth = width;
            c.DrawLine(from, to, bark);
            if (depth == 0) { ends.Add(to); return; }
            Branch(to, angleDeg - 22 - rng.Next() * 18, length * (0.62f + rng.Next() * 0.12f), width * 0.62f, depth - 1);
            Branch(to, angleDeg + 16 + rng.Next() * 18, length * (0.58f + rng.Next() * 0.12f), width * 0.62f, depth - 1);
        }

        bark.StrokeWidth = 7 * s;
        c.DrawLine(trunkBase, trunkTop, bark);
        float trunkAngle = (float)(Math.Atan2(trunkTop.Y - trunkBase.Y, trunkTop.X - trunkBase.X) * 180 / Math.PI);
        Branch(trunkTop, trunkAngle - 20, 30 * s, 5 * s, 4);
        Branch(trunkTop, trunkAngle + 35, 26 * s, 4.5f * s, 4);
        Branch(new SKPoint((trunkBase.X + trunkTop.X) / 2, (trunkBase.Y + trunkTop.Y) / 2), trunkAngle + 55, 22 * s, 4 * s, 3);

        // Blossom clouds at the branch tips: a soft pale mass, then many small individual flowers on top.
        SKColor[] pinks = [new(0xF7, 0xB7, 0xCF), new(0xF4, 0x9A, 0xC1), new(0xFF, 0xD6, 0xE5), new(0xFF, 0xEE, 0xF4)];
        using (var mass = new SKPaint { IsAntialias = true, Color = new SKColor(0xF9, 0xC6, 0xD9, 0x70) })
            foreach (var end in ends)
                c.DrawCircle(end.X, end.Y, 7 * s, mass);
        using var bloom = new SKPaint { IsAntialias = true };
        using var heart = new SKPaint { IsAntialias = true, Color = new SKColor(0xD9, 0x5C, 0x8E) };
        foreach (var end in ends)
        {
            for (int i = 0; i < 14; i++)
            {
                bloom.Color = pinks[(int)(rng.Next() * pinks.Length) % pinks.Length].WithAlpha((byte)(215 + rng.Next() * 40));
                float r = (1.4f + rng.Next() * 1.8f) * s;
                float x = end.X + (rng.Next() - 0.5f) * 15 * s, y = end.Y + (rng.Next() - 0.5f) * 11 * s;
                c.DrawCircle(x, y, r, bloom);
                if (r > 2.4f * s)
                    c.DrawCircle(x, y, r * 0.28f, heart);
            }
        }
    }

    private static void Palm(SKCanvas c, int w, int h)
    {
        bool portrait = h > w;
        float s = Math.Min(w, h) / 170f;
        var baseP = new SKPoint(w * (portrait ? 0.86f : 0.9f), h + 4);
        var top = new SKPoint(w * (portrait ? 0.68f : 0.8f), h * (portrait ? 0.4f : 0.22f));
        var ctrl = new SKPoint(baseP.X + 10 * s, (baseP.Y + top.Y) / 2);

        // Trunk: a curved, tapering stroke drawn in segments.
        using var trunk = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, Color = new SKColor(0x6B, 0x4A, 0x2B) };
        using var ring = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, Color = new SKColor(0x52, 0x37, 0x1F), StrokeWidth = 1 };
        SKPoint prev = baseP;
        const int segs = 14;
        for (int i = 1; i <= segs; i++)
        {
            float t = i / (float)segs;
            var p = Quad(baseP, ctrl, top, t);
            trunk.StrokeWidth = (7 - 3.5f * t) * s;
            c.DrawLine(prev, p, trunk);
            if (i % 2 == 0)
            {
                float half = trunk.StrokeWidth / 2;
                c.DrawLine(p.X - half, p.Y, p.X + half, p.Y + 1, ring);
            }
            prev = p;
        }

        // Fronds: curved leaves radiating from the top.
        float[] angles = [200, 230, 260, 290, 320, 345, 15, 165];
        using var leaf = new SKPaint { IsAntialias = true };
        foreach (float deg in angles)
        {
            double a = deg * Math.PI / 180;
            float len = (portrait ? 34 : 40) * s;
            var tip = new SKPoint(top.X + (float)Math.Cos(a) * len, top.Y + (float)Math.Sin(a) * len * 0.75f + len * 0.35f);
            var mid = new SKPoint((top.X + tip.X) / 2, Math.Min(top.Y, tip.Y) - len * 0.18f);
            using var b = new SKPathBuilder();
            float wdt = 5 * s;
            b.MoveTo(top);
            b.QuadTo(new SKPoint(mid.X, mid.Y - wdt), tip);
            b.QuadTo(new SKPoint(mid.X, mid.Y + wdt), top);
            b.Close();
            using var path = b.Detach();
            leaf.Color = deg is > 250 and < 330 ? new SKColor(0x3A, 0x8F, 0x45) : new SKColor(0x2C, 0x74, 0x37);
            c.DrawPath(path, leaf);
        }
        using (var nut = new SKPaint { IsAntialias = true, Color = new SKColor(0x5A, 0x3A, 0x1E) })
        {
            c.DrawCircle(top.X - 2 * s, top.Y + 3 * s, 2.4f * s, nut);
            c.DrawCircle(top.X + 2.5f * s, top.Y + 3.5f * s, 2.2f * s, nut);
        }
    }

    private static SKPoint Quad(SKPoint a, SKPoint ctrl, SKPoint b, float t)
    {
        float u = 1 - t;
        return new SKPoint(u * u * a.X + 2 * u * t * ctrl.X + t * t * b.X, u * u * a.Y + 2 * u * t * ctrl.Y + t * t * b.Y);
    }

    /// <summary>Midpoint-displacement ridgeline from x=0 to x=w, closed down to the bottom edge.</summary>
    private static SKPath Ridge(int w, int h, float baseY, float amp, uint seed)
    {
        const int n = 64;
        var ys = new float[n + 1];
        uint state = seed * 2654435761u + 1;
        float Rand() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (state % 10000) / 10000f - 0.5f; }

        ys[0] = baseY + Rand() * amp;
        ys[n] = baseY + Rand() * amp;
        float rough = amp;
        for (int step = n; step > 1; step /= 2)
        {
            for (int i = step / 2; i < n; i += step)
                ys[i] = (ys[i - step / 2] + ys[i + step / 2]) / 2 + Rand() * rough;
            rough *= 0.55f;
        }

        using var b = new SKPathBuilder();
        b.MoveTo(0, h);
        for (int i = 0; i <= n; i++)
            b.LineTo(w * i / (float)n, ys[i]);
        b.LineTo(w, h);
        b.Close();
        return b.Detach();
    }

    /// <summary>Gentle rolling hills: a sum of a few seeded sine waves, closed down to the bottom edge.</summary>
    private static SKPath SmoothHills(int w, int h, float baseY, float amp, int waves, uint seed)
    {
        var rng = new Rng(seed);
        var parts = Enumerable.Range(0, 3).Select(i => (Freq: (waves + i * 1.7f) * (0.8f + rng.Next() * 0.4f), Phase: rng.Next() * 6.28f, Amp: amp / (i + 1))).ToArray();
        using var b = new SKPathBuilder();
        b.MoveTo(0, h);
        for (int i = 0; i <= 64; i++)
        {
            float x = w * i / 64f;
            float y = baseY;
            foreach (var (freq, phase, a) in parts)
                y += (float)Math.Sin(x / w * Math.PI * freq + phase) * a;
            b.LineTo(x, y);
        }
        b.LineTo(w, h);
        b.Close();
        return b.Detach();
    }

    /// <summary>Tiny deterministic xorshift RNG returning floats in [0, 1).</summary>
    private sealed class Rng(uint seed)
    {
        private uint _state = seed * 2654435761u + 0x9E3779B9u;

        public float Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
