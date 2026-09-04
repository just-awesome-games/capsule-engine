using System.Numerics;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>
/// One drawable frame: a region of a texture plus the point inside it that a position anchors.
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
public readonly record struct Sprite(TextureHandle Texture, TextureRegion Region, Vector2 Pivot = default);
