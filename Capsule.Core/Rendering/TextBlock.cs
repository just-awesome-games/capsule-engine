using Capsule.Text;

namespace Capsule.Rendering;

/// <summary>A laid-out string placed in the viewport at a given cell scale and colour.</summary>
public readonly record struct TextBlock
{
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cellPixels"/> is not positive.</exception>
    public TextBlock(PixelTextLayout layout, int cellPixels, Anchor anchor, ColorRgba color)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellPixels);

        Layout = layout;
        CellPixels = cellPixels;
        Anchor = anchor;
        Color = color;
    }

    public PixelTextLayout Layout { get; }

    /// <summary>Screen pixels per grid cell.</summary>
    public int CellPixels { get; }

    public Anchor Anchor { get; }

    public ColorRgba Color { get; }
}
