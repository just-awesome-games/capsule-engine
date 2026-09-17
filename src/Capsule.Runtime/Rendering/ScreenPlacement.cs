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
// Span      the world units the camera spans on Surface: under Expand or FixedHeight on a render
//           surface, a whole number of its pixels on the axis the fit grew, the camera's own on
//           the binding axis.
// Surface   the extent the world is drawn on: the render surface where a resolution is declared, the
//           back buffer where none is.
// World     where Span lands on Surface and at what scale — exactly the declared pixels per unit
//           on a render surface, where the surface was sized to the span.
// OnSurface where a canvas pixel lands on that surface, where the screen layer is drawn on it.
// Present   where the surface lands in the back buffer; no scale where there is nothing to present.
// Layer     where a canvas pixel lands in the back buffer, which is what maps a sampled mouse
//           position back to a canvas position.
// ScreenOnSurface
//           whether the screen layer is drawn on Surface, at OnSurface, before it is presented —
//           the canvas is the render resolution, so the interface shares the world's pixels — or
//           straight into the back buffer at Layer, over the presented surface, where the canvas
//           was declared apart from the resolution.
internal readonly record struct ScreenLayout(
    Vector2 Span,
    (int Width, int Height) Surface,
    Letterbox World,
    ScreenPlacement OnSurface,
    ScreenPlacement Present,
    ScreenPlacement Layer,
    bool ScreenOnSurface);
