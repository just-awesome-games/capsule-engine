using System.Numerics;

namespace Capsule.UI;

/// <summary>
/// A screen entity that places its child screen entities in rows of <see cref="Columns"/> cells, in child
/// order, from the near corner of its padded inside.
/// </summary>
/// <remarks>
/// Each column is as wide as the largest preferred width among its cells, and each row as tall as the
/// largest preferred height. A screen entity's preferred extent is its <see cref="ScreenEntity.Size"/>, and
/// a container's is its fitted extent. A child's slot is its cell, and the tracks never stretch to fill
/// the grid. The grid never overrides a child. The child's own <see cref="ScreenEntity.Anchor"/> places it
/// in its cell as it would in a plain parent's padded rect, and its <see cref="Scenes.Entity.Position"/>
/// stays an offset from that anchor that never moves a sibling. A child whose own
/// <see cref="Scenes.Entity.Visible"/> is false takes no cell. A plain entity child is not placed.
/// <para>
/// On each axis the grid is at least its <see cref="ScreenEntity.Size"/> and grows past it to fit its
/// tracks and its <see cref="ScreenEntity.Padding"/>. An axis its own anchor spans takes the span instead.
/// The layout is cached and correct whenever it is read.
/// </para>
/// </remarks>
public class GridContainer : ScreenEntity
{
    /// <summary>How many cells each row holds, at least 1.</summary>
    public int Columns
    {
        get => Layout!.Columns;

        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            Layout!.Columns = value;
            Reflow(this);
        }
    }

    /// <summary>Canvas pixels between neighbouring columns on X and rows on Y, zero by default.</summary>
    public Vector2 Spacing
    {
        get => Layout!.Spacing;

        set
        {
            Guard.NonNegative(value, nameof(value));
            Layout!.Spacing = value;
            Reflow(this);
        }
    }

    /// <param name="columns">How many cells each row holds, at least 1.</param>
    /// <param name="anchor">Where the grid sits in the slot its parent gives it, or in the canvas for a root.</param>
    /// <param name="offset">Canvas pixels from the anchor to the same point of the grid's rect.</param>
    public GridContainer(int columns, Anchor anchor, Vector2 offset)
        : base(anchor, offset)
    {
        Layout = new Tracks();
        Columns = columns;
    }
}
