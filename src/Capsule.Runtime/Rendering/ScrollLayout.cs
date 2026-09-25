using System.Numerics;

namespace Capsule.Runtime.Rendering;

// Where a scrolled layer's intent lands on the frame, as arithmetic over the placed world rect. No
// device, no state, nothing drawn. A layer is drawn by a virtual camera whose centre is the frame's,
// moved by the layer's factor about the scroll centre. The frame draws a sprite at its offset from that
// corner, measured from the frame's own corner and snapped there under point sampling, so the layer's
// grid and the world's coincide.
internal static class ScrollLayout
{
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
}
