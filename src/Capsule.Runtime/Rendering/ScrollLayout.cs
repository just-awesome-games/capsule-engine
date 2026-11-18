using System.Numerics;

namespace Capsule.Runtime.Rendering;

// Where a scrolled layer's intent lands on the frame, as arithmetic over the placed world rect. No
// device, no state, nothing drawn. A layer is drawn by a virtual camera whose centre is the frame's,
// moved by the layer's factor about the scroll centre. The frame draws a sprite at its offset from that
// corner, measured from the frame's own corner and snapped there on a pixel-perfect frame, as
// GridPlacement places it. The layer's grid and the world's coincide.
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

    // The position in a layer at factor that the frame draws at world point, unsnapped.
    internal static Vector2 InLayer(Vector2 world, Vector2 parallax, Vector2 factor) =>
        world + (parallax * (Vector2.One - factor));
}
