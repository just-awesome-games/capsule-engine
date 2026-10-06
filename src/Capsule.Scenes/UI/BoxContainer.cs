using System.Numerics;

namespace Capsule.UI;

/// <summary>
/// A screen entity that lines its child screen entities up along an <see cref="UI.Axis"/>, in child order,
/// <see cref="Spacing"/> apart from the near edge of its padded inside.
/// </summary>
/// <remarks>
/// Each visible child gets a slot as long as the child's preferred extent along the axis and as wide as
/// the padded inside across it. A screen entity's preferred extent is its <see cref="ScreenEntity.Size"/>,
/// and a container's is its fitted extent. The box never overrides a child. The child's own
/// <see cref="ScreenEntity.Anchor"/> places it in its slot as it would in a plain parent's padded rect, so
/// <see cref="Anchor.TopWide"/> spans the slot's width and <see cref="Anchor.Center"/> centres in it. The
/// child's <see cref="Scenes.Entity.Position"/> stays an offset from that anchor and never moves a
/// sibling, so a nudge or a shake animates freely. A child whose own <see cref="Scenes.Entity.Visible"/> is
/// false takes no slot. A plain entity child is not placed.
/// <para>
/// On each axis the box is at least its <see cref="ScreenEntity.Size"/> and grows past it to fit its
/// children and its <see cref="ScreenEntity.Padding"/>. An axis its own anchor spans takes the span
/// instead. The layout is cached and correct whenever it is read.
/// </para>
/// </remarks>
/// <example>
/// A column of rows, each as wide as the widest:
/// <code>
/// BoxContainer column = new(Axis.Vertical, Anchor.Center, Vector2.Zero) { Spacing = 4f };
/// ScreenEntity row = new(Anchor.TopWide, Vector2.Zero) { Parent = column, Size = new Vector2(88f, 16f) };
/// </code>
/// </example>
public class BoxContainer : ScreenEntity
{
    /// <summary>The direction the children line up in.</summary>
    public Axis Axis
    {
        get;

        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Expected Axis.Horizontal or Axis.Vertical.");
            }

            field = value;
            Arrange();
        }
    }

    /// <summary>Canvas pixels between neighbouring children along the axis, zero by default.</summary>
    public float Spacing
    {
        get;

        set
        {
            Guard.NonNegative(value, nameof(value));
            field = value;
            Arrange();
        }
    }

    /// <param name="axis">The direction the children line up in.</param>
    /// <param name="anchor">Where the box sits in the slot its parent gives it, or in the canvas for a root.</param>
    /// <param name="offset">Canvas pixels from the anchor to the same point of the box's rect.</param>
    public BoxContainer(Axis axis, Anchor anchor, Vector2 offset)
        : base(anchor, offset)
    {
        Layout = new Tracks();
        Axis = axis;
    }

    // A vertical box is one column whose width spans the inside, and a horizontal one is one row.
    private void Arrange()
    {
        Tracks tracks = Layout!;
        bool vertical = Axis == Axis.Vertical;
        tracks.Columns = vertical ? 1 : int.MaxValue;
        tracks.Spacing = vertical ? new Vector2(0f, Spacing) : new Vector2(Spacing, 0f);
        tracks.Spans = vertical ? Axis.Horizontal : Axis.Vertical;
        Reflow(this);
    }
}
