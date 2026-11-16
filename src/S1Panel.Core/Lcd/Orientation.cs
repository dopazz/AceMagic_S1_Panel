namespace S1Panel.Core.Lcd;

/// <summary>How the content is laid out relative to the native 320x170 framebuffer.</summary>
public enum Orientation
{
    /// <summary>320 wide x 170 tall, native.</summary>
    Landscape,
    /// <summary>170 wide x 320 tall (the S1 standing upright, as in the original software).</summary>
    Portrait,
    /// <summary>Landscape rotated 180 degrees.</summary>
    LandscapeFlipped,
    /// <summary>Portrait rotated 180 degrees.</summary>
    PortraitFlipped,
}

public static class OrientationExtensions
{
    public static bool IsPortrait(this Orientation o) => o is Orientation.Portrait or Orientation.PortraitFlipped;

    /// <summary>Size of the canvas you draw on for this orientation.</summary>
    public static (int Width, int Height) LogicalSize(this Orientation o) =>
        o.IsPortrait() ? (LcdPanel.Height, LcdPanel.Width) : (LcdPanel.Width, LcdPanel.Height);

    public static Orientation Parse(string s) => s.Trim().ToLowerInvariant().Replace("_", "-") switch
    {
        "landscape" or "l" => Orientation.Landscape,
        "portrait" or "p" => Orientation.Portrait,
        "landscape-flipped" or "lf" => Orientation.LandscapeFlipped,
        "portrait-flipped" or "pf" => Orientation.PortraitFlipped,
        _ => throw new ArgumentException($"Unknown orientation '{s}'. Use landscape, portrait, landscape-flipped or portrait-flipped."),
    };
}
