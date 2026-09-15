using System.Numerics;

namespace Capsule.Runtime.Rendering;

// Point sampling fetches one texel per pixel centre; snapping keeps every texel boundary between
// centres, where float error cannot drop a column and double its neighbour.
internal static class PixelGrid
{
    // value is world units, scale surface pixels per world unit. Midpoints round away from zero so
    // a snapped position never depends on the parity of the pixel it falls in.
    internal static Vector2 Snap(Vector2 value, float scale) => new(
        MathF.Round(value.X * scale, MidpointRounding.AwayFromZero) / scale,
        MathF.Round(value.Y * scale, MidpointRounding.AwayFromZero) / scale);

    // value snapped to the grid anchored at origin — the camera's corner — so a point is a whole
    // number of pixels from where the surface begins, whatever fraction the corner itself sits at.
    // Snapping the corner and the point separately would let their two roundings disagree by a
    // pixel as the point advances.
    internal static Vector2 SnapFrom(Vector2 origin, Vector2 value, float scale) => origin + Snap(value - origin, scale);
}
