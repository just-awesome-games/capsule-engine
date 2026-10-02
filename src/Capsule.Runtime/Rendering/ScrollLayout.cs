using System.Numerics;
using System.Runtime.CompilerServices;

namespace Capsule.Runtime.Rendering;

// Where a scrolled layer's intent lands on the frame, as arithmetic over the placed world rect. No
// device, no state, nothing drawn. A layer is drawn by a virtual camera whose centre is the frame's,
// moved by the layer's factor about the scroll centre. The frame draws a sprite at its offset from that
// corner, measured from the frame's own corner and snapped there under point sampling, so the layer's
// grid and the world's coincide.
internal static class ScrollLayout
{
    // A turn this close to a whole number of quarter turns draws as that quarter turn. It moves a
    // corner of the largest plausible sprite by a small fraction of a pixel.
    private const float QuarterTurnTolerance = 1e-4f;

    // How far a factor-zero layer's corner sits from the frame's: the scroll centre less the centre of
    // the frame's world rect, which has its corner at topLeft and spans span.
    internal static Vector2 Parallax(Vector2 topLeft, Vector2 span, Vector2 scrollCenter) =>
        scrollCenter - (topLeft + (span / 2f));

    // The virtual camera's top-left corner for a layer at factor, on a frame whose world rect has its
    // corner at topLeft. A factor of one gives the frame's own corner, and zero the corner of the
    // frame centred on the scroll centre.
    internal static Vector2 Corner(Vector2 topLeft, Vector2 parallax, Vector2 factor) =>
        topLeft + (parallax * (Vector2.One - factor));

    // Where an intent at position in its layer lands in the frame's world rect: the same offset from
    // the frame's corner that it has from the layer's, quantised to whole surface pixels where snap is
    // set. With the layer's corner at the frame's, this is the world's own placement.
    internal static Vector2 Place(Vector2 position, Vector2 layerCorner, Vector2 frameCorner, bool snap, float scale) =>
        frameCorner + (snap ? PixelGrid.SnapOffset(layerCorner, position, scale) : position - layerCorner);

    // Places a quad turned by a whole number of quarter turns with every edge on the frame's pixel grid,
    // or returns false for any other turn. Two quads that abut in the layer then share their edge
    // exactly at any scale, and a quad's drawn extent may differ by a pixel with where it sits. The quad
    // is size wide and tall before turning, turns clockwise about position, and origin is position's
    // offset from its unturned top-left. corner is where its unturned top-left lands, pixels its
    // unturned extent in whole surface pixels, and turns the whole quarter turns it is drawn at. A turn
    // admitted within the tolerance draws at the quarter turn it rounds to.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryPlaceSnapped(
        Vector2 position,
        Vector2 origin,
        Vector2 size,
        float rotation,
        Vector2 layerCorner,
        Vector2 frameCorner,
        float scale,
        out Vector2 corner,
        out Vector2 pixels,
        out int turns)
    {
        // The unturned quad, which nearly every sprite is, stays inline. The turned path is apart.
        if (rotation == 0f)
        {
            Vector2 topLeft = position - origin;
            corner = frameCorner + PixelGrid.SnapSpan(layerCorner, topLeft, topLeft + size, scale, out pixels);
            turns = 0;

            return true;
        }

        return TryPlaceTurned(position, origin, size, rotation, layerCorner, frameCorner, scale, out corner, out pixels, out turns);
    }

    private static bool TryPlaceTurned(
        Vector2 position,
        Vector2 origin,
        Vector2 size,
        float rotation,
        Vector2 layerCorner,
        Vector2 frameCorner,
        float scale,
        out Vector2 corner,
        out Vector2 pixels,
        out int turns)
    {
        float quarters = rotation * (2f / MathF.PI);
        float whole = MathF.Round(quarters);
        if (!(MathF.Abs(quarters - whole) <= QuarterTurnTolerance))
        {
            corner = default;
            pixels = default;
            turns = 0;

            return false;
        }

        // The turned quad's bounds about position. A clockwise quarter turn takes (x, y) to (-y, x).
        turns = (((int)whole % 4) + 4) % 4;
        Vector2 a = -origin;
        Vector2 b = size - origin;
        (Vector2 min, Vector2 max) = turns switch
        {
            0 => (a, b),
            1 => (new Vector2(-b.Y, a.X), new Vector2(-a.Y, b.X)),
            2 => (-b, -a),
            _ => (new Vector2(a.Y, -b.X), new Vector2(b.Y, -a.X)),
        };

        Vector2 low = frameCorner + PixelGrid.SnapSpan(layerCorner, position + min, position + max, scale, out Vector2 extent);
        Vector2 high = low + (extent / scale);
        pixels = (turns & 1) == 0 ? extent : new Vector2(extent.Y, extent.X);
        corner = turns switch
        {
            0 => low,
            1 => new Vector2(high.X, low.Y),
            2 => high,
            _ => new Vector2(low.X, high.Y),
        };

        return true;
    }
}
