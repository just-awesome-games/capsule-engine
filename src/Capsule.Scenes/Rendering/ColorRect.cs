using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Scenes.Rendering;

/// <summary>
/// Draws its entity as one rectangle of flat colour. The rectangle's top-left corner lands on the
/// entity's position plus <see cref="Offset"/>, and it spans <see cref="Size"/> from there. Y-down, in
/// world units on a world entity and canvas pixels on a screen one.
/// <para>
/// It needs no texture of its own: the engine reserves one white texel the host holds, so a filled
/// rect costs exactly one sprite and loads nothing.
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
    /// Added to the entity's position to give the rectangle's top-left corner. In the entity's own
    /// units; zero by default.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>The colour filled, straight alpha; white and opaque by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <inheritdoc/>
    public override Rect Bounds
    {
        get
        {
            if (Entity is not { } entity)
            {
                return default;
            }

            return new Rect(entity.Position + entity.SpaceOrigin + Offset, Size);
        }
    }

    /// <inheritdoc/>
    public override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        Entity entity = Entity!;
        Vector2 origin = entity.SpaceOrigin + Offset;

        view.Add(new SpriteIntent(
            Sprite.White,
            entity.PreviousPosition + origin,
            entity.Position + origin,
            Size,
            FlipX: false,
            FlipY: false,
            Color));
    }
}
