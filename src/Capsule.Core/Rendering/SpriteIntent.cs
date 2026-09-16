using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One sprite as the simulation wants it drawn. The renderer interpolates
/// <see cref="PreviousPosition"/> to <see cref="Position"/> and lands the frame's pivot there,
/// turned by <see cref="PreviousRotation"/> interpolated to <see cref="Rotation"/> along the
/// shortest arc.
/// </summary>
/// <param name="Sprite">The frame drawn, and the pivot its position anchors and its rotation turns about.</param>
/// <param name="PreviousPosition">
/// Where the pivot sat at the end of the previous step, in the drawn space's units.
/// </param>
/// <param name="Position">Where the pivot sits now, in the drawn space's units.</param>
/// <param name="PreviousRotation">
/// The turn about the pivot at the end of the previous step, in radians, clockwise positive.
/// </param>
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
/// <param name="Color">Multiplied into every texel; <see cref="ColorRgba.White"/> draws the texture as it is.</param>
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
    // The anchor a backend draws from, in region texels. Mirrored on a flipped axis, so the pivot
    // texel stays on the position whichever way the frame faces.
    internal Vector2 DrawOrigin => new(
        FlipX ? Sprite.Region.Width - Sprite.Pivot.X : Sprite.Pivot.X,
        FlipY ? Sprite.Region.Height - Sprite.Pivot.Y : Sprite.Pivot.Y);

    // The world rect this sprite sweeps between its two positions, or false where it draws nothing
    // testable: a non-positive extent, a region with no texels, a non-finite rotation, or a
    // non-finite rect. Unturned at both ends, the rect is the drawn rect swept; turned at either,
    // it is the sweep of the frame's bounding circle about the pivot, which covers the frame at
    // every angle without evaluating a sine the determinism contract keeps out of this tier.
    internal bool TryGetSweptBounds(out Rect swept)
    {
        swept = default;

        TextureRegion region = Sprite.Region;

        // Tested on the extents themselves, never left to the swept rect: travel widens that rect,
        // so a sprite moving further than a negative extent would measure positive area there and
        // be drawn inverted. NaN fails these comparisons too.
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
            swept = new Rect(
                MathF.Min(PreviousPosition.X, Position.X) - corner.X,
                MathF.Min(PreviousPosition.Y, Position.Y) - corner.Y,
                MathF.Max(PreviousPosition.X, Position.X) - corner.X + Size.X,
                MathF.Max(PreviousPosition.Y, Position.Y) - corner.Y + Size.Y);
        }
        else
        {
            // The farthest corner from the pivot on each axis is whichever side of it is longer;
            // the square root is correctly rounded on every platform, so this stays deterministic.
            float reachX = MathF.Max(corner.X, Size.X - corner.X);
            float reachY = MathF.Max(corner.Y, Size.Y - corner.Y);
            float radius = MathF.Sqrt((reachX * reachX) + (reachY * reachY));

            swept = new Rect(
                MathF.Min(PreviousPosition.X, Position.X) - radius,
                MathF.Min(PreviousPosition.Y, Position.Y) - radius,
                MathF.Max(PreviousPosition.X, Position.X) + radius,
                MathF.Max(PreviousPosition.Y, Position.Y) + radius);
        }

        // What remains for IsEmpty is a corner or an extent that is not finite.
        return !swept.IsEmpty;
    }
}
