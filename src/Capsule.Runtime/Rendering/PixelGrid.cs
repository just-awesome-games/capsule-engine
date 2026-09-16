using System.Numerics;

namespace Capsule.Runtime.Rendering;

// Point sampling fetches one texel per pixel centre; snapping keeps every texel boundary between
// centres, where float error cannot drop a column and double its neighbour.
internal static class PixelGrid
{
    // value is world units, scale surface pixels per world unit.
    internal static Vector2 Snap(Vector2 value, float scale) => SnapOffset(Vector2.Zero, value, scale);

    // value snapped to the grid anchored at origin — the camera's corner — so a point is a whole
    // number of pixels from where the surface begins, whatever fraction the corner itself sits at.
    // Snapping the corner and the point separately would let their two roundings disagree by a
    // pixel as the point advances.
    internal static Vector2 SnapFrom(Vector2 origin, Vector2 value, float scale) => origin + SnapOffset(origin, value, scale);

    // The whole-pixel offset of value from origin, in world units: what SnapFrom adds to the origin.
    // Two points a whole number of pixels apart snap a whole number of pixels apart wherever the
    // origin sits, and a point holding its distance from the origin holds its pixel: the offset and
    // its scaling are taken in double, where float inputs are exact, and a midpoint rounds down by
    // a margin wider than the float noise the origin carries. A camera whose span is an odd number
    // of pixels puts every followed sprite on a midpoint, where rounding by the noise alone would
    // flip it between two pixels, and rounding away from zero would send the two sides of zero
    // opposite ways.
    internal static Vector2 SnapOffset(Vector2 origin, Vector2 value, float scale) => new(
        SnapOffset(origin.X, value.X, scale),
        SnapOffset(origin.Y, value.Y, scale));

    // Below what a display can show, above what float error at any plausible world extent reaches.
    private const double MidpointMargin = 1.0 / 32.0;

    private static float SnapOffset(float origin, float value, float scale) =>
        (float)(Math.Floor((((double)value - origin) * scale) + 0.5 - MidpointMargin) / scale);
}
