namespace Capsule.Rendering;

/// <summary>A straight (non-premultiplied) 8-bit-per-channel colour.</summary>
public readonly partial record struct ColorRgba(byte R, byte G, byte B, byte A)
{
    /// <summary>Fully opaque.</summary>
    public ColorRgba(byte r, byte g, byte b)
        : this(r, g, b, byte.MaxValue)
    {
    }
}
