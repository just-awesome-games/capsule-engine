using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Rendering;

namespace MinimalGame.Game.UI;

/// <summary>
/// Two flat rects: a dark bed, and a fill as wide a share of it as <see cref="Fraction"/> says.
/// Nothing here is a widget, and whose share it is showing is its display's business.
/// </summary>
public sealed class HealthBar : ScreenEntity
{
    // The bed's extent in canvas pixels, and the fill's at full.
    private static readonly Vector2 Span = new(64f, 6f);

    private static readonly ColorRgba BedColor = new(24, 24, 32);
    private static readonly ColorRgba FillColor = new(222, 96, 96);

    private readonly ColorRect _fill = new(Span) { Color = FillColor, ZIndex = 1 };

    /// <param name="anchor">The point on the canvas <paramref name="offset"/> is measured from.</param>
    /// <param name="offset">Canvas pixels from that point to this bar's top-left corner.</param>
    public HealthBar(Anchor anchor, Vector2 offset)
        : base(anchor, offset)
    {
        // Both rects are on one entity, so the renderer's own band is what layers them.
        Add(new ColorRect(Span) { Color = BedColor });
        Add(_fill);
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
            _fill.Size = new Vector2(Span.X * field, Span.Y);
        }
    } = 1f;
}
