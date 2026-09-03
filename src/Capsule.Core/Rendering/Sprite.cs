using System.Numerics;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>
/// One drawable frame: a region of a texture plus the point inside it that a position anchors.
/// </summary>
/// <param name="Texture">The texture the region is cut from.</param>
/// <param name="Region">The region drawn, in texels of <paramref name="Texture"/>.</param>
/// <param name="Pivot">
/// The anchor, in texels of <paramref name="Region"/> from its top-left corner, and zero — the
/// top-left corner itself — by default. Whatever world position the frame is drawn at is where
/// this texel lands, so a body's feet or a wheel's hub can be the coordinate the game reasons in.
/// A flip mirrors the region about it on that axis and leaves it on the same world point: a
/// horizontal flip mirrors on X, a vertical flip on Y, and a pivot halfway across the region on an
/// axis therefore flips in place on it. A pivot at the region's left edge instead swings the whole
/// region across to the right of the position when flipped horizontally.
/// </param>
public readonly record struct Sprite(TextureHandle Texture, TextureRegion Region, Vector2 Pivot = default);
