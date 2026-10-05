using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.UI;

/// <summary>
/// An entity on the frame's screen layer, placed as a rect in canvas pixels, Y-down. Every renderer it
/// holds draws over the world layer whatever the two layers' bands are.
/// </summary>
/// <remarks>
/// A screen entity is placed in its parent's rect inset by that parent's <see cref="Padding"/>, or in
/// the canvas when it is a root, by its <see cref="Anchor"/> and its <see cref="Entity.Position"/>. The
/// canvas is the run's (<see cref="Run.Canvas"/>), not the window's. Its <see cref="Entity.Parent"/> may
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
    /// Where this entity sits in its parent's padded rect, or in the canvas for a root,
    /// <see cref="Anchor.TopLeft"/> by default.
    /// </summary>
    public Anchor Anchor { get; set; }

    /// <summary>
    /// This entity's rect extent in canvas pixels, where the default of zero makes the entity a point.
    /// </summary>
    /// <remarks>An axis the <see cref="Anchor"/> spans takes the span's extent and ignores this one.</remarks>
    public Vector2 Size
    {
        get;

        set
        {
            Guard.NonNegative(value, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// The insets from this entity's rect to the inner rect its child screen entities are placed in, where
    /// the default of zero places them in the whole rect.
    /// </summary>
    /// <remarks>
    /// The entity's own components fill its outer rect. An inner extent smaller than zero is zero.
    /// </remarks>
    public Insets Padding { get; set; }

    /// <param name="anchor">Where this entity sits in its parent's padded rect, or in the canvas for a root.</param>
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
    internal Vector2 Extent => Anchor.Extent(Parent is ScreenEntity parent ? parent.Padding.Inside(parent.Extent) : Canvas, Size);

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

    // The canvas point the rect's top-left corner lands on before this entity's own transform, and the
    // rect's extent. The entity's world turn and scale carry the corner about the anchor point.
    private Vector2 Origin(bool previous, out Vector2 extent)
    {
        if (Parent is not ScreenEntity parent)
        {
            return Anchor.Place(Vector2.Zero, Canvas, Size, out extent) - Lean(previous, extent);
        }

        Vector2 above = parent.Origin(previous, out Vector2 outer);
        Vector2 point = Anchor.Place(new Vector2(parent.Padding.Left, parent.Padding.Top), parent.Padding.Inside(outer), Size, out extent);

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
