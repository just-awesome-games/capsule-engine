using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one rectangle of flat colour. The rectangle's top-left corner lands on
/// <see cref="Offset"/> placed by the entity's world transform, spans <see cref="Size"/> times the
/// transform's scale from there, and turns about that corner by the transform's rotation. A negative scale
/// axis swings the rectangle to the other side of the corner. Coordinates are Y-down, in world units under a
/// world root and canvas pixels under a screen root.
/// <para>
/// It draws the engine's white texel (<see cref="Sprite.White"/>), so it loads no asset.
/// </para>
/// </summary>
/// <param name="size">The extent the rectangle covers. A non-positive axis draws nothing.</param>
public sealed class ColorRect(Vector2 size) : Renderer
{
    /// <summary>
    /// The extent the rectangle covers, in the entity's units. A component that is not positive and
    /// finite draws nothing.
    /// </summary>
    public Vector2 Size { get; set; } = size;

    /// <summary>
    /// The point in the entity's own space where the rectangle's top-left corner lands, placed by the
    /// entity's world transform. Zero by default, which puts the corner on the entity.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>The fill colour, in straight alpha. White and opaque by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>How the rectangle's colour combines with what is already drawn. Alpha by default.</summary>
    public BlendMode Blend { get; set; }

    /// <summary>
    /// The rect the rectangle covers, under the rules <see cref="Renderer.Bounds"/> states. With a non-zero
    /// world rotation it reports the box of the rectangle's bounding circle about the corner. Reads empty
    /// when the rectangle draws nothing.
    /// </summary>
    public override Rect Bounds =>
        Entity is not null && Intent(RenderTransform, RenderTransform).TryGetSweptBounds(out Rect box) ? box : default;

    /// <inheritdoc/>
    public override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.Add(Intent(PreviousRenderTransform, RenderTransform));
    }

    // The white texel anchors at its corner, and a flip swings the rect across that corner. A negative world
    // scale axis folds into the flip flags, and the backend draws the magnitude of the extent.
    private SpriteIntent Intent(in Transform2D previous, in Transform2D current)
    {
        Vector2 size = Size * current.Scale;

        return new SpriteIntent(Sprite.White, previous.TransformPoint(Offset), current.TransformPoint(Offset), previous.Rotation, current.Rotation, Vector2.Abs(size), size.X < 0f, size.Y < 0f, Color, Blend);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Size", Size);
        panel.Field("Offset", Offset);
        panel.Field("Color", Color);
        panel.Field("Blend", Blend.ToString());
    }
}
