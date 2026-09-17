using System.Numerics;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>
/// One drawable frame: a region of a texture, the point inside it that a position anchors, and the
/// named points a sheet set on it.
/// </summary>
/// <param name="Texture">The texture the region is cut from.</param>
/// <param name="Region">The region drawn, in texels of <paramref name="Texture"/>.</param>
/// <param name="Pivot">
/// The anchor, in texels of <paramref name="Region"/> from its top-left corner; zero — the corner
/// itself — by default. Whatever world position the frame is drawn at is where this texel lands. A
/// flip mirrors the region about it on that axis and leaves it on the same world point, so a pivot
/// halfway across an axis flips in place on it, while one at the region's edge swings the region
/// across to the other side of the position.
/// </param>
/// <param name="Sockets">
/// The named points this frame carries, in <paramref name="Pivot"/>'s texel space; empty by
/// default. A view over memory the caller keeps: a generated frame reads one shared table, so two
/// reads of it are equal and neither allocates, and a code-authored frame passes an array it never
/// writes to again. Names are unique within a frame.
/// </param>
public readonly record struct Sprite(
    TextureHandle Texture,
    TextureRegion Region,
    Vector2 Pivot = default,
    ReadOnlyMemory<SpriteSocket> Sockets = default)
{
    /// <summary>
    /// The engine's one white texel, anchored at its corner: the frame a flat colour is drawn from,
    /// stretched to whatever extent the intent asks for and tinted by its colour.
    /// </summary>
    public static Sprite White => new(TextureHandle.White, new TextureRegion(0, 0, 1, 1));
}
