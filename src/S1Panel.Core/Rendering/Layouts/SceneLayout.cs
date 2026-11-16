using SkiaSharp;

namespace S1Panel.Core.Rendering.Layouts;

/// <summary>
/// A picture with a large clock over it (stock app's "scene" layout). The picture is one of four
/// procedurally drawn seasonal scenes (<see cref="SceneArt"/>), or your own photo via <see cref="Image"/>.
/// The background is drawn once and cached; per-second cost is just the clock.
/// </summary>
public sealed class SceneLayout : ILayout
{
    private SKBitmap? _cached;
    private (int W, int H, Season Season) _cachedKey;
    private SKBitmap? _image;

    public string Name => "scene";
    public string Description => "Seasonal picture (winter, spring, summer, autumn, or your own image) with a large clock";

    /// <summary>Optional custom background, scaled to fill. Overrides <see cref="Season"/>.</summary>
    public SKBitmap? Image
    {
        get => _image;
        set { _image = value; Invalidate(); }
    }

    /// <summary>Which seasonal scene to draw; null = follow the calendar.</summary>
    public Season? Season
    {
        get;
        set { field = value; Invalidate(); }
    }

    public void Draw(SKCanvas c, LayoutContext ctx)
    {
        int w = ctx.Width, h = ctx.Height;
        var t = ctx.Stats.Time;
        var season = Season ?? SceneArt.SeasonOf(t);

        if (_cached is null || _cachedKey != (w, h, season))
        {
            _cached?.Dispose();
            _cached = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var bc = new SKCanvas(_cached);
            if (_image is not null)
                Scenes.Image(bc, w, h, _image, cover: true);
            else
                SceneArt.Draw(bc, w, h, season);
            _cachedKey = (w, h, season);
        }
        c.DrawBitmap(_cached, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);

        float timeY = ctx.Portrait ? h - 70 : h * 0.62f;
        float timeSize = ctx.Portrait ? 56 : 64;
        float dateY = timeY + (ctx.Portrait ? 26 : 24);

        // Bright scenes get a soft dark band behind the text so white stays readable.
        byte scrim = _image is null ? SceneArt.ScrimAlpha(season) : (byte)0;
        if (scrim > 0)
        {
            float top = timeY - timeSize * 0.85f, bottom = dateY + 8;
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, top), new SKPoint(0, bottom),
                [SKColors.Black.WithAlpha(0), SKColors.Black.WithAlpha(scrim), SKColors.Black.WithAlpha(scrim), SKColors.Black.WithAlpha(0)],
                [0f, 0.3f, 0.75f, 1f], SKShaderTileMode.Clamp);
            using var p = new SKPaint { Shader = shader };
            c.DrawRect(0, top, w, bottom - top, p);
        }

        Gfx.TimeCentered(c, t, ctx.Clock, w / 2f, timeY, timeSize, Gfx.Light, SKColors.White, w - 16, shadow: true);
        ShadowText(c, t.ToString("dddd d MMMM"), w / 2f, dateY, ctx.Portrait ? 13 : 15, Gfx.Regular);
    }

    private void Invalidate()
    {
        _cached?.Dispose();
        _cached = null;
    }

    private static void ShadowText(SKCanvas c, string text, float x, float y, float size, SKTypeface face)
    {
        using var font = Gfx.Font(face, size);
        using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, size / 14);
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 150), IsAntialias = true, MaskFilter = blur };
        c.DrawText(text, x + 1, y + 2, SKTextAlign.Center, font, shadow);
        using var p = new SKPaint { Color = SKColors.White, IsAntialias = true };
        c.DrawText(text, x, y, SKTextAlign.Center, font, p);
    }
}
