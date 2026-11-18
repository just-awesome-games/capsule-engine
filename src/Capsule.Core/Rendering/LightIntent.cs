using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One light as the simulation wants it drawn. The renderer interpolates its position and rotation
/// as it does a <see cref="SpriteIntent"/>'s and adds the sprite into the frame's light, pivot on the position.
/// </summary>
/// <remarks>
/// The light at a pixel multiplies every lit world sprite drawn there. It starts at the scene's ambient colour,
/// and each light adds its colour times its falloff and intensity, with no ceiling: a white light of 1 on a
/// white ambient doubles what it reaches.
/// </remarks>
/// <param name="Sprite">The frame the light is drawn as. <see cref="Rendering.Sprite.Light"/> when the light carries no other sprite.</param>
/// <param name="PreviousPosition">Where the pivot sat at the end of the previous step, in world units.</param>
/// <param name="Position">Where the pivot sits now, in world units.</param>
/// <param name="PreviousRotation">The turn about the pivot at the end of the previous step, in radians, clockwise positive.</param>
/// <param name="Rotation">The turn about the pivot now, in radians, clockwise positive in the Y-down space.</param>
/// <param name="Size">The extent the region is drawn at, in world units. A non-positive or non-finite axis draws nothing.</param>
/// <param name="FlipX">Whether the region is mirrored horizontally about the pivot, on top of any mirror the frame itself carries.</param>
/// <param name="FlipY">Whether the region is mirrored vertically about the pivot, on top of any mirror the frame itself carries.</param>
/// <param name="Color">The light's colour, added into the light scaled by its alpha.</param>
/// <param name="Intensity">How many times the colour is added, 1 by default. Non-positive or non-finite draws nothing.</param>
public readonly record struct LightIntent(
    Sprite Sprite,
    Vector2 PreviousPosition,
    Vector2 Position,
    float PreviousRotation,
    float Rotation,
    Vector2 Size,
    bool FlipX,
    bool FlipY,
    ColorRgba Color,
    float Intensity = 1f)
{
    // The intent drawn additively into the light, for both the host's draw and the cull, which reuses
    // SpriteIntent's swept bounds instead of a second implementation.
    internal SpriteIntent ToSprite() => new(
        Sprite,
        PreviousPosition,
        Position,
        PreviousRotation,
        Rotation,
        Size,
        FlipX,
        FlipY,
        Color,
        BlendMode.Additive);
}
