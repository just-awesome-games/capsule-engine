namespace Capsule.Rendering;

/// <summary>A straight (non-premultiplied) 8-bit-per-channel colour.</summary>
public readonly partial record struct ColorRgba(byte R, byte G, byte B, byte A)
{
    /// <summary>Fully opaque.</summary>
    public ColorRgba(byte r, byte g, byte b)
        : this(r, g, b, byte.MaxValue)
    {
    }

    /// <summary>
    /// The colour <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>,
    /// with each channel interpolated on its own and rounded to the nearest byte, a half rounding up.
    /// Alpha is straight, so it blends like any other channel and the colour is not premultiplied by
    /// it. Progress is clamped to <c>[0, 1]</c>, where <c>0</c> gives <paramref name="a"/> and
    /// <c>1</c> gives <paramref name="b"/>. NaN progress gives <paramref name="a"/>.
    /// </summary>
    public static ColorRgba Lerp(ColorRgba a, ColorRgba b, float t)
    {
        if (t <= 0f || float.IsNaN(t))
        {
            return a;
        }

        if (t >= 1f)
        {
            return b;
        }

        return new ColorRgba(
            Channel(a.R, b.R, t),
            Channel(a.G, b.G, t),
            Channel(a.B, b.B, t),
            Channel(a.A, b.A, t));
    }

    // The result stays within [0, 255] for any t in (0, 1), so rounding is the only conversion needed.
    private static byte Channel(byte from, byte to, float t) =>
        (byte)MathF.Round(from + ((to - from) * t), MidpointRounding.AwayFromZero);
}
