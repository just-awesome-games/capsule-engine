namespace Capsule.Rendering;

/// <summary>
/// A straight (non-premultiplied) 8-bit-per-channel colour. Engine-owned so that
/// render intent never names a backend type.
/// </summary>
public readonly record struct ColorRgba(byte R, byte G, byte B, byte A)
{
    /// <summary>Opaque; <paramref name="a"/> defaults to fully opaque.</summary>
    public ColorRgba(byte r, byte g, byte b)
        : this(r, g, b, byte.MaxValue)
    {
    }

    public static ColorRgba Black => new(0, 0, 0);

    public static ColorRgba White => new(255, 255, 255);
}
