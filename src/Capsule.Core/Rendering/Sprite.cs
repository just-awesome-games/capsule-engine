using System.Numerics;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>One drawable frame: a region of a texture, the point inside it a position anchors, and the named points a sheet set on it.</summary>
/// <param name="Texture">The texture the region is cut from.</param>
/// <param name="Region">The region drawn, in texels of <paramref name="Texture"/>.</param>
/// <param name="Pivot">
/// The anchor, in texels of <paramref name="Region"/> from its top-left corner, and the corner itself
/// by default. This texel lands on whatever position the frame is drawn at, and a flip mirrors the
/// region about it. A pivot halfway across an axis flips in place, while a pivot at the edge swings the
/// region across the position.
/// </param>
/// <param name="Sockets">
/// The named points this frame carries, in <paramref name="Pivot"/>'s texel space, and empty by
/// default. It is a view over memory the caller keeps, and a generated frame reads one shared table
/// without allocating. Names are unique within a frame.
/// </param>
public readonly record struct Sprite(
    TextureHandle Texture,
    TextureRegion Region,
    Vector2 Pivot = default,
    ReadOnlyMemory<SpriteSocket> Sockets = default)
{
    /// <summary>
    /// The engine's white texel, anchored at its corner. Flat colour is drawn from this frame,
    /// stretched to whatever extent the intent asks for and tinted by its colour.
    /// </summary>
    public static Sprite White => new(TextureHandle.White, new TextureRegion(0, 0, 1, 1));

    /// <summary>The engine's radial light: full at the centre, falling to nothing at the edge, anchored at its centre. A <see cref="LightIntent"/> with no other sprite draws this one.</summary>
    public static Sprite Light => new(TextureHandle.Light, new TextureRegion(0, 0, 128, 128), new Vector2(64f, 64f));
}
