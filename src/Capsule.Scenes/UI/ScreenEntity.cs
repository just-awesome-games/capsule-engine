using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.UI;

/// <summary>
/// An entity on the frame's screen layer, placed as a rect in canvas pixels, Y-down. Every renderer it
/// holds draws over the world layer whatever the two layers' bands are, unless its root
/// <see cref="SortsWithWorld"/>.
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
    public Anchor Anchor
    {
        get;

        set
        {
            if (field != value)
            {
                field = value;
                Reflow(Parent);
            }
        }
    }

    /// <summary>
    /// Whether this root and everything beneath it draw at their band among the world layer's renderers,
    /// rather than over the whole world.
    /// </summary>
    /// <remarks>
    /// The tree stays placed, clipped and pixel-snapped in canvas pixels, and the camera never moves or
    /// zooms it. It draws at the render surface's resolution, is lit with the world, and takes no part in
    /// <see cref="Scene.YSort"/>: it draws first in its band.
    /// </remarks>
    /// <example>
    /// A panel the world's actors pass in front of, over the background at band -10:
    /// <code>
    /// ScreenEntity panel = new(Anchor.Center, Vector2.Zero) { Size = new Vector2(160f, 48f), SortsWithWorld = true, ZIndex = -5 };
    /// </code>
    /// </example>
    /// <exception cref="InvalidOperationException">Set on an entity that has a parent.</exception>
    public bool SortsWithWorld
    {
        get => SortedWithWorld;

        set
        {
            if (value && Parent is not null)
            {
                throw new InvalidOperationException(
                    $"A {GetType().Name} has a parent and draws in its root's order. Set SortsWithWorld on the root.");
            }

            SortedWithWorld = value;
        }
    }

    /// <summary>
    /// The least extent this entity's rect takes in canvas pixels, zero by default. The rect grows past it to
    /// fit what the entity holds.
    /// </summary>
    /// <remarks>
    /// The rect fits its own renderers, and a container's fits its children too, up to <see cref="MaxSize"/>.
    /// An entity that holds nothing to measure keeps this extent, and at zero it is a point. An axis the
    /// <see cref="Anchor"/> spans takes the span's extent and ignores this one.
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
            Remeasure(this);
        }
    }

    /// <summary>
    /// The most extent this entity's rect grows to in canvas pixels while it fits what it holds, unbounded by
    /// default.
    /// </summary>
    /// <remarks>
    /// Wrapped text inside the rect wraps at this width, less the padding between. Content past it overflows
    /// the rect, and a <see cref="ScrollContainer"/> scrolls it. <see cref="Size"/> outranks it, and an axis
    /// the <see cref="Anchor"/> spans ignores it.
    /// </remarks>
    public Vector2 MaxSize
    {
        get;

        set
        {
            if (!(value.X >= 0f) || !(value.Y >= 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A MaxSize component is negative or NaN. Pass zero or more, or float.PositiveInfinity for no bound.");
            }

            if (field == value)
            {
                return;
            }

            field = value;
            Remeasure(this);
        }
    } = new(float.PositiveInfinity);

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

    // The nearest clip on this entity or above, as Entity.ResolveClip last composed it.
    internal ClipRect? TreeClip { get; set; }

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
    internal Vector2 Extent => Measure(out _);

    private Vector2 Canvas => Root.SceneOrNull?.RunOrNull?.Canvas ?? Vector2.Zero;

    // The rect's extent, and the width cap its parent hands it, which is unbounded for a root.
    internal Vector2 Measure(out float given)
    {
        if (Parent is not ScreenEntity parent)
        {
            given = float.PositiveInfinity;

            return Within(Canvas, given);
        }

        Vector2 outer = parent.Measure(out float above);
        given = parent.Inner(above);

        return Within(parent.SlotExtent(this, outer, above), given);
    }

    // The rect's extent in `slot`. A spanned axis takes its share of the slot, and a point axis what the entity
    // prefers, the width first and then the height at that width.
    internal Vector2 Within(Vector2 slot, float given)
    {
        float width = WidthIn(slot.X, given);

        return new Vector2(width, Anchor.SpansY ? Anchor.Extent(slot, Vector2.Zero).Y : PreferredHeight(width, given));
    }

    // The rect's width in a slot `slot` wide: its share of the slot where the anchor spans X, and its own otherwise.
    internal float WidthIn(float slot, float given) => Anchor.SpansX ? Anchor.ShareX * slot : PreferredWidth(given);

    // The width this entity's rect fits under the width cap `given` from above. A wrapping renderer here
    // prefers no more than the tighter of that cap and this entity's own.
    internal float PreferredWidth(float given)
    {
        float content = 0f;
        float cap = Math.Min(MaxSize.X, given);

        foreach (Component component in Components)
        {
            if (component is Renderer { Visible: true } renderer)
            {
                content = Math.Max(content, renderer.MeasureWidth(cap));
            }
        }

        if (Layout is { } tracks)
        {
            content = Math.Max(content, tracks.Width(this, given));
        }
        else if (this is ScrollContainer scroll)
        {
            content = Math.Max(content, scroll.ContentWidth(Inner(given)) + Padding.Left + Padding.Right);
        }

        return Math.Max(Size.X, Math.Min(content, MaxSize.X));
    }

    // The height this entity's rect fits once it is `width` wide.
    internal float PreferredHeight(float width, float given)
    {
        float content = 0f;

        foreach (Component component in Components)
        {
            if (component is Renderer { Visible: true } renderer)
            {
                content = Math.Max(content, renderer.MeasureHeight(width));
            }
        }

        if (Layout is { } tracks)
        {
            content = Math.Max(content, tracks.Height(this, width, given));
        }
        else if (this is ScrollContainer scroll)
        {
            float inside = Math.Max(width - Padding.Left - Padding.Right, 0f);
            content = Math.Max(content, scroll.ContentHeight(inside, Inner(given)) + Padding.Top + Padding.Bottom);
        }

        return Math.Max(Size.Y, Math.Min(content, MaxSize.Y));
    }

    // The width cap this entity hands its children under the cap `given` from above: the tighter of that and
    // its own, but never under its Size, less its padding.
    internal float Inner(float given) =>
        Math.Max(Math.Max(Size.X, Math.Min(MaxSize.X, given)) - Padding.Left - Padding.Right, 0f);

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

    // The extent of the slot `child` is placed in, as Slot gives it, without the corner a scroll offset moves.
    private Vector2 SlotExtent(ScreenEntity child, Vector2 extent, float given)
    {
        if (Layout is { } tracks)
        {
            tracks.Slot(this, child, extent, given, out Vector2 cell);

            return cell;
        }

        return this is ScrollContainer ? ScrollContainer.Grown(child, Padding.Inside(extent), Inner(given)) : Padding.Inside(extent);
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

        return ((ScreenEntity)holder).Origin(previous, out _, out _);
    }

    // Marks the container `from` stale and walks up while its parent fits its children too. A walk that meets
    // a stale container stops there, because a stale container's container ancestors are stale too. A scroll
    // container keeps no solve and passes the walk on.
    internal static void Reflow(Entity? from)
    {
        while (from is ScreenEntity screen && (screen.Layout is { } tracks ? tracks.Invalidate() : screen is ScrollContainer))
        {
            from = screen.Parent;
        }
    }

    // Reflows what reads `entity`'s fit after a write that changes it: the entity itself where it fits its
    // children, as a container and a scroll container do, and its parent otherwise.
    internal static void Remeasure(Entity? entity)
    {
        if (entity is ScreenEntity screen)
        {
            Reflow(screen.Layout is not null || screen is ScrollContainer ? screen : screen.Parent);
        }
    }

    // Returns the top-left corner of the slot `child` is placed in, in this entity's units, and writes the
    // slot's extent. `extent` is this entity's own, `given` the width cap above it, and `previous` reads the
    // scroll offset of the step before. A container's layout picks the slot, a scroll container's is its
    // padded inside grown to the child and moved back by the offset, and any other entity's slot is its
    // padded inside.
    internal Vector2 Slot(ScreenEntity child, Vector2 extent, float given, bool previous, out Vector2 slot)
    {
        if (Layout is { } tracks)
        {
            return tracks.Slot(this, child, extent, given, out slot);
        }

        if (this is ScrollContainer scroll)
        {
            return scroll.Place(child, extent, Inner(given), previous, out slot);
        }

        slot = Padding.Inside(extent);

        return new Vector2(Padding.Left, Padding.Top);
    }

    // The canvas point the rect's top-left corner lands on before this entity's own transform, and the
    // rect's extent and the width cap its parent hands it. The entity's world turn and scale carry the corner
    // about the anchor point.
    private Vector2 Origin(bool previous, out Vector2 extent, out float given)
    {
        if (Parent is not ScreenEntity parent)
        {
            given = float.PositiveInfinity;

            return Anchor.Place(Vector2.Zero, Canvas, Within(Canvas, given), out extent) - Lean(previous, extent);
        }

        Vector2 above = parent.Origin(previous, out Vector2 outer, out float cap);
        Vector2 corner = parent.Slot(this, outer, cap, previous, out Vector2 slot);
        given = parent.Inner(cap);
        Vector2 point = Anchor.Place(corner, slot, Within(slot, given), out extent);

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
