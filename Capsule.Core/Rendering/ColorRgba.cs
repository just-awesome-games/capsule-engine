namespace Capsule.Rendering;

/// <summary>A straight (non-premultiplied) 8-bit-per-channel colour.</summary>
public readonly record struct ColorRgba(byte R, byte G, byte B, byte A)
{
    /// <summary>Fully opaque.</summary>
    public ColorRgba(byte r, byte g, byte b)
        : this(r, g, b, byte.MaxValue)
    {
    }

    /// <summary>Opaque black.</summary>
    public static ColorRgba Black => new(0, 0, 0);

    /// <summary>Opaque white.</summary>
    public static ColorRgba White => new(255, 255, 255);
}
