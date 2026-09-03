using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One sprite as the simulation wants it drawn. The renderer interpolates
/// <see cref="PreviousPosition"/> to <see cref="Position"/> and lands the frame's pivot there.
/// </summary>
/// <param name="Sprite">The frame drawn, and the pivot its position anchors.</param>
/// <param name="PreviousPosition">Where the pivot sat at the end of the previous step, in world units.</param>
/// <param name="Position">Where the pivot sits now, in world units.</param>
/// <param name="Size">
/// The world extent the region is drawn at. Equal to the region's texel size draws one texel per
/// world unit.
/// </param>
/// <param name="FlipX">Whether the region is mirrored horizontally about the pivot.</param>
/// <param name="FlipY">Whether the region is mirrored vertically about the pivot.</param>
/// <param name="Color">Multiplied into every texel; <see cref="ColorRgba.White"/> draws the texture as it is.</param>
public readonly record struct SpriteIntent(
    Sprite Sprite,
    Vector2 PreviousPosition,
    Vector2 Position,
    Vector2 Size,
    bool FlipX,
    bool FlipY,
    ColorRgba Color)
{
    // The anchor a backend draws from, in region texels: mirroring it on a flipped axis is what
    // moves the drawn rect, so the pivot texel stays on the position whichever way the frame faces.
    internal Vector2 DrawOrigin => new(
        FlipX ? Sprite.Region.Width - Sprite.Pivot.X : Sprite.Pivot.X,
        FlipY ? Sprite.Region.Height - Sprite.Pivot.Y : Sprite.Pivot.Y);

    /// <summary>
    /// The world rect this sprite sweeps between its two positions, or false where it draws
    /// nothing testable: an extent that is not positive, a region with no texels, or a rect some
    /// coordinate of which is not finite.
    /// </summary>
    internal bool TryGetSweptBounds(out ViewBounds swept)
    {
        swept = default;

        TextureRegion region = Sprite.Region;

        // Tested on the extents themselves, never left to the swept rect: travel widens that rect,
        // so a sprite moving further than a negative extent still measures positive area there and
        // would reach the renderer to be drawn inverted. A region with no texels has no scale to
        // place the pivot by. NaN fails these comparisons too.
        if (!(Size.X > 0f) || !(Size.Y > 0f) || region.Width <= 0 || region.Height <= 0)
        {
            return false;
        }

        // The world offset from the position back to the drawn rect's top-left corner.
        Vector2 corner = DrawOrigin * new Vector2(Size.X / region.Width, Size.Y / region.Height);

        swept = new ViewBounds(
            MathF.Min(PreviousPosition.X, Position.X) - corner.X,
            MathF.Min(PreviousPosition.Y, Position.Y) - corner.Y,
            MathF.Max(PreviousPosition.X, Position.X) - corner.X + Size.X,
            MathF.Max(PreviousPosition.Y, Position.Y) - corner.Y + Size.Y);

        // What remains for IsEmpty is a corner or an extent that is not finite.
        return !swept.IsEmpty;
    }
}
