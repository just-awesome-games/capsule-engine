using System.Numerics;
using Capsule.Rendering;

namespace Capsule.UI;

/// <summary>
/// A screen entity that lines its child screen entities up in rows, in child order, starting a row wherever
/// the next child would pass the width of its padded inside.
/// </summary>
/// <remarks>
/// Each visible child gets a cell as wide as its preferred width and as tall as its row, and each row is as
/// tall as its tallest cell. A row holds at least one child, and a child wider than the inside overflows a
/// row of its own.
/// <para>
/// The flow prefers the width of its widest row and its padding, once the rows break under the tighter of its
/// <see cref="ScreenEntity.MaxSize"/> and the width its parent gives it. With neither bound it prefers one row.
/// That width is then bounded as any screen entity's is: by <see cref="ScreenEntity.MaxSize"/>, by
/// <see cref="ScreenEntity.Size"/>, and by a span of its anchor. The rows break again at the width it takes.
/// </para>
/// </remarks>
/// <example>
/// A row of tags along the canvas's bottom edge that wraps onto lines ending at its right edge:
/// <code>
/// FlowContainer tags = new(Anchor.BottomRight, new Vector2(-4f, -4f))
/// {
///     MaxSize = new Vector2(160f, float.PositiveInfinity),
///     Spacing = new Vector2(6f, 2f),
///     Alignment = HorizontalAlignment.Right,
/// };
/// </code>
/// </example>
public class FlowContainer : ScreenEntity
{
    /// <summary>Canvas pixels between neighbouring children in a row on X and between rows on Y, zero by default.</summary>
    public Vector2 Spacing
    {
        get => Layout!.Spacing;

        set
        {
            Layout!.Spacing = Guard.NonNegative(value, nameof(value));
            Reflow(this);
        }
    }

    /// <summary>Where each row sits across the padded inside, <see cref="HorizontalAlignment.Left"/> by default.</summary>
    public HorizontalAlignment Alignment
    {
        get => Layout!.Alignment;

        set
        {
            if ((uint)value > (uint)HorizontalAlignment.Right)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Expected HorizontalAlignment.Left, Center or Right.");
            }

            Layout!.Alignment = value;
            Reflow(this);
        }
    }

    /// <param name="anchor">Where the flow sits in the slot its parent gives it, or in the canvas for a root.</param>
    /// <param name="offset">Canvas pixels from the anchor to the same point of the flow's rect.</param>
    public FlowContainer(Anchor anchor, Vector2 offset)
        : base(anchor, offset) =>
        Layout = new Tracks { Flows = true };
}
