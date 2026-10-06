using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.UI;

/// <summary>
/// An entity on the frame's screen layer, placed as a rect in canvas pixels, Y-down. Every renderer it
/// holds draws over the world layer whatever the two layers' bands are.
/// </summary>
/// <remarks>
/// A screen entity is placed in the slot its parent gives it, or in the canvas when it is a root, by its
/// <see cref="Anchor"/> and its <see cref="Entity.Position"/>. A plain parent's slot is its rect inset by
/// its <see cref="Padding"/>, and a container's is the child's cell. The canvas is the run's
/// (<see cref="Run.Canvas"/>), not the window's. Its <see cref="Entity.Parent"/> may
/// be another screen entity but never a plain one. A plain entity parented under one joins the group
/// and draws on the screen layer in canvas pixels from this entity's top-left corner.
/// </remarks>
/// <example>
/// A panel centred on the canvas, with a caption along the top of its padded inside:
/// <code>
/// ScreenEntity panel = new(Anchor.Center, Vector2.Zero) { Size = new Vector2(120f, 60f), Padding = new Insets(6f) };
/// panel.Add(new ColorRect { Color = PanelColor });
/// ScreenEntity title = new(Anchor.TopWide, Vector2.Zero) { Parent = panel, Size = new Vector2(0f, 12f) };
/// title.Add(new Label(font, "Paused") { HorizontalAlignment = HorizontalAlignment.Center });
/// </code>
/// </example>
public class ScreenEntity : Entity
{
    /// <summary>
    /// Where this entity sits in the slot its parent gives it, or in the canvas for a root,
    /// <see cref="Anchor.TopLeft"/> by default.
    /// </summary>
    public Anchor Anchor { get; set; }

    /// <summary>
    /// This entity's rect extent in canvas pixels, where the default of zero makes the entity a point.
    /// </summary>
    /// <remarks>
    /// An axis the <see cref="Anchor"/> spans takes the span's extent and ignores this one. A container
    /// grows past it to fit its children.
    /// </remarks>
    public Vector2 Size
    {
        get;

        set
        {
            Guard.NonNegative(value, nameof(value));
            if (field == value)
            {
                return;
            }

            field = value;
            Reflow(Layout is null ? Parent : this);
        }
    }

    /// <summary>
    /// The insets from this entity's rect to the inner rect its child screen entities are placed in, where
    /// the default of zero places them in the whole rect.
    /// </summary>
    /// <remarks>
    /// The entity's own components fill its outer rect. An inner extent smaller than zero is zero.
    /// </remarks>
    public Insets Padding
    {
        get;

        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Reflow(this);
        }
    }

    // The tracks a container places its children with, or null on any other screen entity.
    internal Tracks? Layout { get; private protected init; }

    // This entity's cell in its container parent's last solve, or -1 while it is hidden and takes none.
    internal int Cell { get; set; }

    /// <param name="anchor">Where this entity sits in the slot its parent gives it, or in the canvas for a root.</param>
    /// <param name="offset">
    /// Canvas pixels from the anchor to the same point of this entity's rect. A negative component
    /// measures back towards the top-left.
    /// </param>
    public ScreenEntity(Anchor anchor, Vector2 offset)
        : base(offset)
    {
        Anchor = anchor;
        OnScreen = true;
    }

    // The rect's extent in this entity's units. It reads the canvas through the root's scene, and an
    // entity in no scene places its root in a canvas of zero.
    internal Vector2 Extent
    {
        get
        {
            if (Parent is not ScreenEntity parent)
            {
                return Anchor.Extent(Canvas, Preferred);
            }

            parent.Slot(this, parent.Extent, out Vector2 slot);

            return Anchor.Extent(slot, Preferred);
        }
    }

    // The extent this entity takes on an axis its anchor does not span. A container's fits its children.
    internal Vector2 Preferred => Layout?.Preferred(this) ?? Size;

    private Vector2 Canvas => Root.SceneOrNull?.RunOrNull?.Canvas ?? Vector2.Zero;

    // Returns `size` with each zero axis taken from the rect of the screen entity holding the component.
    // A component on a plain entity keeps its size.
    internal static Vector2 Fill(Entity? entity, Vector2 size)
    {
        if ((size.X != 0f && size.Y != 0f) || entity is not ScreenEntity screen)
        {
            return size;
        }

        Vector2 extent = screen.Extent;

        return new Vector2(size.X == 0f ? extent.X : size.X, size.Y == 0f ? extent.Y : size.Y);
    }

    // The space origin of `entity`, which a screen root's tree holds. The anchors resolve on read and
    // never interpolate, as the canvas does not. The transforms are read from the step `previous` names.
    internal static Vector2 OriginOf(Entity entity, bool previous)
    {
        // A screen entity is never under a plain one. The nearest screen entity holds every anchor above.
        Entity holder = entity;
        while (holder is not ScreenEntity)
        {
            holder = holder.Parent!;
        }

        return ((ScreenEntity)holder).Origin(previous, out _);
    }

    // Marks the container `from` stale and walks up while its parent is a container too. A walk that meets
    // a stale container stops there, because a stale container's container ancestors are stale too.
    internal static void Reflow(Entity? from)
    {
        while (from is ScreenEntity { Layout: { } tracks } container && tracks.Invalidate())
        {
            from = container.Parent;
        }
    }

    // Returns the top-left corner of the slot `child` is placed in, in this entity's units, and writes the
    // slot's extent. `extent` is this entity's own. A container's layout picks the slot, and any other
    // entity's slot is its padded inside.
    internal Vector2 Slot(ScreenEntity child, Vector2 extent, out Vector2 slot)
    {
        if (Layout is { } tracks)
        {
            return tracks.Slot(this, child, extent, out slot);
        }

        slot = Padding.Inside(extent);

        return new Vector2(Padding.Left, Padding.Top);
    }

    // The canvas point the rect's top-left corner lands on before this entity's own transform, and the
    // rect's extent. The entity's world turn and scale carry the corner about the anchor point.
    private Vector2 Origin(bool previous, out Vector2 extent)
    {
        if (Parent is not ScreenEntity parent)
        {
            return Anchor.Place(Vector2.Zero, Canvas, Preferred, out extent) - Lean(previous, extent);
        }

        Vector2 above = parent.Origin(previous, out Vector2 outer);
        Vector2 corner = parent.Slot(this, outer, out Vector2 slot);
        Vector2 point = Anchor.Place(corner, slot, Preferred, out extent);

        // The anchor point is in the parent's units. The parent's turn and scale carry it onto the canvas, and
        // the lean is already in canvas pixels.
        ref readonly Transform2D placed = ref previous ? ref parent.PreviousWorld : ref parent.World;

        return above + placed.TransformPoint(point) - placed.Position - Lean(previous, extent);
    }

    // The anchor point's offset from the rect's top-left corner after this entity's world turn and scale,
    // which is the transform its renderers draw by.
    private Vector2 Lean(bool previous, Vector2 extent)
    {
        ref readonly Transform2D own = ref previous ? ref PreviousWorld : ref World;

        return own.TransformPoint(Anchor.Pivot(extent)) - own.Position;
    }
}
