using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One sprite as the simulation wants it drawn. The renderer interpolates
/// <see cref="PreviousPosition"/> to <see cref="Position"/> and lands the frame's pivot there,
/// turned by <see cref="PreviousRotation"/> interpolated to <see cref="Rotation"/> along the
/// shortest arc.
/// </summary>
/// <param name="Sprite">The frame drawn, carrying the pivot the position anchors and the rotation turns about.</param>
/// <param name="PreviousPosition">Where the pivot sat at the end of the previous step, in the drawn space's units.</param>
/// <param name="Position">Where the pivot sits now, in the drawn space's units.</param>
/// <param name="PreviousRotation">The turn about the pivot at the end of the previous step, in radians, clockwise positive.</param>
/// <param name="Rotation">
/// The turn about the pivot now, in radians, clockwise positive in the Y-down space. A non-finite
/// rotation, at either end, draws nothing.
/// </param>
/// <param name="Size">
/// The extent the region is drawn at, in the drawn space's units. Equal to the region's texel size
/// draws one texel per unit.
/// </param>
/// <param name="FlipX">Whether the region is mirrored horizontally about the pivot.</param>
/// <param name="FlipY">Whether the region is mirrored vertically about the pivot.</param>
/// <param name="Color">Multiplied into every texel. <see cref="ColorRgba.White"/> draws the texture unchanged.</param>
public readonly record struct SpriteIntent(
    Sprite Sprite,
    Vector2 PreviousPosition,
    Vector2 Position,
    float PreviousRotation,
    float Rotation,
    Vector2 Size,
    bool FlipX,
    bool FlipY,
    ColorRgba Color)
{
    // The anchor a backend draws from, in region texels. It mirrors on a flipped axis, so the pivot texel
    // stays on the position whichever way the frame faces.
    internal Vector2 DrawOrigin => new(
        FlipX ? Sprite.Region.Width - Sprite.Pivot.X : Sprite.Pivot.X,
        FlipY ? Sprite.Region.Height - Sprite.Pivot.Y : Sprite.Pivot.Y);

    // The world rect this sprite sweeps between its two positions, or false where it draws nothing
    // testable. Unturned at both ends, the rect is the drawn rect swept. Turned at either end, it is the
    // sweep of the frame's bounding circle about the pivot, which covers the frame at every angle without
    // evaluating a sine the determinism contract keeps out of this tier.
    internal bool TryGetSweptBounds(out Rect swept)
    {
        swept = default;

        TextureRegion region = Sprite.Region;

        // Tested on the extents, not on the swept rect. Travel widens that rect. A sprite moving
        // further than a negative extent would measure positive area and be drawn inverted. NaN fails
        // these comparisons too.
        if (!(Size.X > 0f) || !(Size.Y > 0f) || region.Width <= 0 || region.Height <= 0)
        {
            return false;
        }

        if (!float.IsFinite(PreviousRotation) || !float.IsFinite(Rotation))
        {
            return false;
        }

        // The world offset from the position back to the drawn rect's top-left corner.
        Vector2 corner = DrawOrigin * new Vector2(Size.X / region.Width, Size.Y / region.Height);

        if (PreviousRotation == 0f && Rotation == 0f)
        {
            swept = Rect.Sweep(PreviousPosition, Position, -corner, Size - corner);
        }
        else
        {
            // The farthest corner from the pivot on each axis is whichever side is longer. The square root
            // is correctly rounded on every platform, so this stays deterministic.
            float reachX = MathF.Max(corner.X, Size.X - corner.X);
            float reachY = MathF.Max(corner.Y, Size.Y - corner.Y);
            float radius = MathF.Sqrt((reachX * reachX) + (reachY * reachY));

            swept = Rect.Sweep(PreviousPosition, Position, new Vector2(radius, radius));
        }

        // At this point IsEmpty can only mean a non-finite corner or extent.
        return !swept.IsEmpty;
    }
}
