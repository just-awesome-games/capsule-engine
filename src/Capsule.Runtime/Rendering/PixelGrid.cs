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
    // exact, and a midpoint rounds down by a margin wider than the float noise the origin carries.
    // Rounding away from zero would send the two sides of zero opposite ways.
    internal static Vector2 SnapOffset(Vector2 origin, Vector2 value, float scale) => new(
        SnapOffset(origin.X, value.X, scale),
        SnapOffset(origin.Y, value.Y, scale));

    private static float SnapOffset(float origin, float value, float scale) =>
        (float)(Pixel(origin, value, scale) / scale);

    // How near a pixel edge a followed point may round from: wider than the float noise between it and the
    // corner out to a world extent in the tens of thousands of pixels, and half the midpoint margin.
    private const double AnchorMargin = MidpointMargin / 2.0;

    // A world rect's corner as a pixel-perfect frame draws it. Frame is the whole surface pixel the corner
    // rounds to, where the picture is placed, and Anchor the origin the frame's points snap from. Snapped
    // from one origin, two things standing still in the world never shift against each other as the view
    // moves, short of one sitting within the margin of its own rounding edge. Anchor is Frame except within
    // the margin of a pixel edge, where it moves by up to the margin. A point a whole number of pixels from
    // the unsnapped corner, as a followed sprite is, then never rounds from nearer an edge than the margin,
    // and float noise cannot move it as the corner crosses. A point any other distance from the corner
    // moves a pixel on the frame at each crossing.
    internal static (Vector2 Frame, Vector2 Anchor) Corners(Vector2 corner, float scale)
    {
        (float frameX, float anchorX) = Corners(corner.X, scale);
        (float frameY, float anchorY) = Corners(corner.Y, scale);

        return (new Vector2(frameX, frameY), new Vector2(anchorX, anchorY));
    }

    private static (float Frame, float Anchor) Corners(float corner, float scale)
    {
        double pixels = (double)corner * scale;
        double whole = Math.Floor(pixels + 0.5 - MidpointMargin);

        // How far the corner sits into the span of positions that round to its pixel, from 0 up to 1.
        double into = pixels - whole + 0.5 - MidpointMargin;
        double held = Math.Clamp(into, AnchorMargin, 1.0 - AnchorMargin);

        return ((float)(whole / scale), (float)((whole + (into - held)) / scale));
    }

    // The whole surface pixel value lands on, counted from origin.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Pixel(float origin, float value, float scale) =>
        Math.Floor((((double)value - origin) * scale) + 0.5 - MidpointMargin);
}
