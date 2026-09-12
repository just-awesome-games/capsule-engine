using System.Numerics;

namespace Capsule.Runtime.Rendering;

// Where the screen layer lands in the window: a canvas pixel is Origin plus itself times Scale, in
// back-buffer pixels. The inverse is how a window position — the mouse's — becomes the canvas position
// the simulation reads. Identity stands in only where the layer has nowhere to land, as in a window
// with no area, so a device sampled there reads window pixels rather than dividing by zero.
internal readonly record struct ScreenPlacement(Vector2 Origin, float Scale)
{
    internal static ScreenPlacement Identity => new(Vector2.Zero, 1f);

    internal Vector2 ToCanvas(Vector2 window) =>
        Scale > 0f ? (window - Origin) / Scale : window - Origin;
}

// One frame's whole presentation geometry, resolved from a frame view and the back buffer's extent:
//
// Span      the world units the camera spans on the back buffer.
// Surface   the extent the world is drawn on: the render surface where a resolution is declared, the
//           back buffer where none is.
// OnSurface where a canvas pixel lands on that surface.
// Present   where the surface lands in the back buffer; no scale where there is nothing to present.
// Layer     where a canvas pixel lands in the back buffer, which is what maps a sampled mouse
//           position back to a canvas position.
internal readonly record struct ScreenLayout(
    Vector2 Span,
    (int Width, int Height) Surface,
    ScreenPlacement OnSurface,
    ScreenPlacement Present,
    ScreenPlacement Layer);
