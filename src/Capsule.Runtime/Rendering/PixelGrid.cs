using System.Numerics;
using System.Runtime.CompilerServices;

namespace Capsule.Runtime.Rendering;

// Point sampling fetches one texel per pixel centre. At a whole scale snapping keeps every texel
// boundary between centres, where float error cannot drop a column and double its neighbour. At any
// scale it keeps a snapped edge there, where two quads sharing it leave no pixel uncovered.
internal static class PixelGrid
{
    // Below what a display can show, above what float error at any plausible world extent reaches.
    private const double MidpointMargin = 1.0 / 32.0;

    // The whole-pixel offset of value from origin, in world units, where origin is the camera's corner
    // and scale is surface pixels per world unit. Two points a whole number of pixels apart snap a
    // whole number of pixels apart wherever the origin sits, and a point holding its distance from the
    // origin holds its pixel. The offset and its scaling are taken in double, where float inputs are
    // exact, and a midpoint rounds down by a margin wider than the float noise the origin carries. A
    // camera whose span is an odd number of pixels puts every followed sprite on a midpoint, where
    // rounding by the noise alone would flip it between two pixels and rounding away from zero would
    // send the two sides of zero opposite ways.
    internal static Vector2 SnapOffset(Vector2 origin, Vector2 value, float scale) => new(
        SnapOffset(origin.X, value.X, scale),
        SnapOffset(origin.Y, value.Y, scale));

    // Snaps both ends of the span from low to high as SnapOffset snaps a point. Returns low's offset,
    // and pixels is the whole number of surface pixels between the two snapped ends.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector2 SnapSpan(Vector2 origin, Vector2 low, Vector2 high, float scale, out Vector2 pixels)
    {
        double left = Pixel(origin.X, low.X, scale);
        double top = Pixel(origin.Y, low.Y, scale);
        pixels = new Vector2((float)(Pixel(origin.X, high.X, scale) - left), (float)(Pixel(origin.Y, high.Y, scale) - top));

        return new Vector2((float)(left / scale), (float)(top / scale));
    }

    private static float SnapOffset(float origin, float value, float scale) =>
        (float)(Pixel(origin, value, scale) / scale);

    // The whole surface pixel value lands on, counted from origin.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Pixel(float origin, float value, float scale) =>
        Math.Floor((((double)value - origin) * scale) + 0.5 - MidpointMargin);
}
