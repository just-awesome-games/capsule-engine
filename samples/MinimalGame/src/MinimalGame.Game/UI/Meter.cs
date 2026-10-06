using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace MinimalGame.Game.UI;

/// <summary>
/// A dark bed with a one-pixel frame, and a fill inside the frame as wide a share of it as
/// <see cref="Fraction"/> says. Nothing here is a widget, and whose share it is showing is its display's
/// business.
/// </summary>
public sealed class Meter : ScreenEntity
{
    // The bed's extent in canvas pixels. The fill is the bed less its frame.
    private static readonly Vector2 BedSize = new(66f, 8f);

    private static readonly ColorRgba BedColor = new(24, 24, 32);
    private static readonly ColorRgba FillColor = new(222, 96, 96);

    // Spans the bed's padded inside on Y, and the share Fraction names of it on X.
    private readonly ScreenEntity _fill = new(Anchor.Fill, Vector2.Zero);

    private readonly ColorRect _bed = new() { Color = BedColor };

    /// <param name="anchor">Where the meter sits in its display's rect.</param>
    /// <param name="offset">Canvas pixels from that point to the same point of the meter.</param>
    public Meter(Anchor anchor, Vector2 offset)
        : base(anchor, offset)
    {
        Size = BedSize;
        Padding = new Insets(1f);
        Add(_bed);

        _fill.Parent = this;
        _fill.Add(new ColorRect { Color = FillColor });
    }

    /// <summary>
    /// The share of the bed the fill covers, 0 to 1; a value outside that is clamped to it. Full
    /// until something sets it.
    /// </summary>
    public float Fraction
    {
        get;

        set
        {
            field = Math.Clamp(value, 0f, 1f);
            _fill.Anchor = new Anchor(0f, 0f, field, 1f);
        }
    } = 1f;

    /// <summary>Whether <paramref name="point"/>, in canvas pixels, lies on the bed.</summary>
    public bool Contains(Vector2 point) => _bed.Bounds.Contains(point);

    /// <summary>The share of the fill's span that <paramref name="point"/> reaches on X, unclamped.</summary>
    public float FractionAt(Vector2 point)
    {
        Rect bed = _bed.Bounds;

        return (point.X - bed.Left - Padding.Left) / (bed.Size.X - Padding.Left - Padding.Right);
    }
}
