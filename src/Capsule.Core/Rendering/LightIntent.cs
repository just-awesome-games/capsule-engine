using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One light as the simulation wants it drawn. The renderer interpolates
/// <see cref="PreviousPosition"/> to <see cref="Position"/> and lands the sprite's pivot there, turned
/// by <see cref="PreviousRotation"/> interpolated to <see cref="Rotation"/> along the shortest arc, as a
/// <see cref="SpriteIntent"/> is. The sprite is drawn additively into the frame's light map, its wider
/// region axis spanning <c>2 * Radius</c> world units and the other axis by the region's aspect,
/// anchored on the sprite's pivot.
/// </summary>
/// <param name="Sprite">The frame the light is drawn as. <see cref="Rendering.Sprite.Light"/> when the light carries no other sprite.</param>
/// <param name="PreviousPosition">Where the pivot sat at the end of the previous step, in world units.</param>
/// <param name="Position">Where the pivot sits now, in world units.</param>
/// <param name="PreviousRotation">The turn about the pivot at the end of the previous step, in radians, clockwise positive.</param>
/// <param name="Rotation">The turn about the pivot now, in radians, clockwise positive in the Y-down space.</param>
/// <param name="Radius">World units from the pivot to the light's falloff edge. Non-positive or non-finite draws nothing.</param>
/// <param name="Color">The light's colour, added into the light map scaled by its alpha.</param>
/// <param name="Intensity">How many times the colour is added. Non-positive or non-finite draws nothing.</param>
public readonly record struct LightIntent(
    Sprite Sprite,
    Vector2 PreviousPosition,
    Vector2 Position,
    float PreviousRotation,
    float Rotation,
    float Radius,
    ColorRgba Color,
    float Intensity = 1f)
{
    // The extent the light is drawn at: the wider region axis spans 2 * Radius, the other axis follows
    // the region's aspect. A bad radius or an empty region draws nothing.
    internal Vector2 Size
    {
        get
        {
            if (!(Radius > 0f) || !float.IsFinite(Radius))
            {
                return Vector2.Zero;
            }

            float width = Sprite.Region.Width;
            float height = Sprite.Region.Height;

            if (width <= 0 || height <= 0)
            {
                return Vector2.Zero;
            }

            float span = 2f * Radius;

            return width >= height
                ? new Vector2(span, span * height / width)
                : new Vector2(span * width / height, span);
        }
    }

    // The intent drawn additively into the light map, for both the host's draw and the cull, which
    // reuses SpriteIntent's swept bounds instead of a second implementation.
    internal SpriteIntent ToSprite(ColorRgba color) => new(
        Sprite,
        PreviousPosition,
        Position,
        PreviousRotation,
        Rotation,
        Size,
        FlipX: false,
        FlipY: false,
        color,
        BlendMode.Additive);
}
