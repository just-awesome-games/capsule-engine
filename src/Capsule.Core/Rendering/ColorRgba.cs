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
    /// Reads <c>#rrggbb</c> or <c>#rrggbbaa</c> in either case, with the alpha last as the debug overlay
    /// prints it.
    /// </summary>
    /// <example>
    /// <code>
    /// ColorRgba dusk = ColorRgba.FromHex("#484c68");
    /// ColorRgba glass = ColorRgba.FromHex("#ffffff80");
    /// </code>
    /// </example>
    /// <exception cref="FormatException"><paramref name="hex"/> is not one of those two forms.</exception>
    public static ColorRgba FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);

        if ((hex.Length == 7 || hex.Length == 9) && hex[0] == '#'
            && TryReadByte(hex, 1, out byte r) && TryReadByte(hex, 3, out byte g) && TryReadByte(hex, 5, out byte b))
        {
            byte a = byte.MaxValue;
            if (hex.Length == 7 || TryReadByte(hex, 7, out a))
            {
                return new ColorRgba(r, g, b, a);
            }
        }

        throw new FormatException(
            $"\"{hex}\" is not a colour. Write it as \"#rrggbb\" or as \"#rrggbbaa\" with the alpha last.");
    }

    /// <summary>
    /// The colour <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>,
    /// with each channel interpolated on its own and rounded to the nearest byte, a half rounding up.
    /// </summary>
    /// <remarks>
    /// Alpha blends like any other channel, with no premultiplication. Progress is clamped to
    /// <c>[0, 1]</c>, and NaN progress gives <paramref name="a"/>.
    /// </remarks>
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

    // Multiplies each channel of a by b's. White is an exact identity and the result is deterministic.
    internal static ColorRgba Multiply(ColorRgba a, ColorRgba b) =>
        new(Multiply(a.R, b.R), Multiply(a.G, b.G), Multiply(a.B, b.B), Multiply(a.A, b.A));

    // One channel times another, both read as fractions of 255, rounded to the nearest byte. A factor
    // of 255 returns the other channel unchanged.
    internal static byte Multiply(byte a, byte b) => (byte)(((a * b) + 127) / 255);

    private static bool TryReadByte(string hex, int start, out byte value)
    {
        char high = hex[start];
        char low = hex[start + 1];
        if (!char.IsAsciiHexDigit(high) || !char.IsAsciiHexDigit(low))
        {
            value = 0;
            return false;
        }

        value = (byte)((HexDigit(high) << 4) | HexDigit(low));
        return true;
    }

    // Folding to lowercase with 0x20 is safe here because the caller has already checked the digit.
    private static int HexDigit(char digit) => digit <= '9' ? digit - '0' : (digit | 0x20) - 'a' + 10;

    // The result stays within [0, 255] for any t in (0, 1), so rounding is the only conversion needed.
    private static byte Channel(byte from, byte to, float t) =>
        (byte)MathF.Round(from + ((to - from) * t), MidpointRounding.AwayFromZero);
}
