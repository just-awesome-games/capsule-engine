using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one rectangle of flat colour. The rectangle's top-left corner lands on
/// <see cref="Offset"/> placed by the entity's world transform, and it spans <see cref="Size"/>
/// times the transform's scale from there, turned about that corner by its rotation; a negative
/// axis of the scale swings the rectangle to the other side of the corner. Y-down, in world units
/// under a world root and canvas pixels under a screen one.
/// <para>
/// It draws over the engine's white texel (<see cref="Sprite.White"/>), so it loads nothing.
/// </para>
/// </summary>
/// <param name="size">The extent the rectangle covers; a non-positive axis draws nothing.</param>
public sealed class ColorRect(Vector2 size) : Renderer
{
    /// <summary>
    /// The extent the rectangle covers, in the entity's units. A component that is not positive and
    /// finite draws nothing.
    /// </summary>
    public Vector2 Size { get; set; } = size;

    /// <summary>
    /// The point in the entity's own space the rectangle's top-left corner lands on, placed by the
    /// entity's world transform; zero by default, which is the entity itself.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>The colour filled, straight alpha; white and opaque by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>
    /// The rect the rectangle covers, on the terms <see cref="Renderer.Bounds"/> states: the box of
    /// its bounding circle about the corner where the entity's world rotation is not zero. Empty
    /// where it draws nothing.
    /// </summary>
    public override Rect Bounds =>
        Entity is not null && Intent(RenderTransform, RenderTransform).TryGetSweptBounds(out Rect box) ? box : default;

    /// <inheritdoc/>
    public override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.Add(Intent(PreviousRenderTransform, RenderTransform));
    }

    // The white texel is anchored at its corner, so a flip swings the rect across the corner: a
    // negative axis of the world scale folds into it, and the extent stays the magnitude drawn.
    private SpriteIntent Intent(in Transform2D previous, in Transform2D current)
    {
        Vector2 size = Size * current.Scale;

        return new SpriteIntent(Sprite.White, previous.Apply(Offset), current.Apply(Offset), previous.Rotation, current.Rotation, Vector2.Abs(size), size.X < 0f, size.Y < 0f, Color);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Size", Size);
        panel.Field("Offset", Offset);
        panel.Field("Color", Color);
    }
}
