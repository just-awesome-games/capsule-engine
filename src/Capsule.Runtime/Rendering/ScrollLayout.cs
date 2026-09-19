using System.Numerics;

namespace Capsule.Runtime.Rendering;

// Where a scrolled layer's intent lands on the frame, as arithmetic over the placed world rect. No
// device, no state, nothing drawn. A layer is drawn by a virtual camera whose corner is the frame's,
// moved by the layer's factor about the scroll origin. The frame draws a sprite at its offset from that
// corner, measured from the frame's own corner and snapped there under point sampling, so the layer's
// grid and the world's coincide.
internal static class ScrollLayout
{
    // The virtual camera's top-left corner for a layer at factor, on a frame whose world rect has its
    // corner at topLeft. A factor of one gives the frame's own corner, and zero gives the origin.
    internal static Vector2 Corner(Vector2 topLeft, Vector2 origin, Vector2 factor) =>
        origin + ((topLeft - origin) * factor);

    // Where an intent at position in its layer lands in the frame's world rect: the same offset from
    // the frame's corner that it has from the layer's, quantised to whole surface pixels where snap is
    // set. With the layer's corner at the frame's, this is the world's own placement.
    internal static Vector2 Place(Vector2 position, Vector2 layerCorner, Vector2 frameCorner, bool snap, float scale) =>
        frameCorner + (snap ? PixelGrid.SnapOffset(layerCorner, position, scale) : position - layerCorner);
}
