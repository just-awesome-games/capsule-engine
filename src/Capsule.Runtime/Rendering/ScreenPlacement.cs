using System.Numerics;

namespace Capsule.Runtime.Rendering;

// Where the screen layer lands in the window: a canvas pixel is Origin plus itself times Scale, in
// back-buffer pixels. The inverse turns a sampled mouse position into the canvas position the
// simulation reads. Identity stands in where the layer has nowhere to land, as in a window with no
// area, and a device sampled there reads window pixels instead of dividing by zero.
internal readonly record struct ScreenPlacement(Vector2 Origin, float Scale)
{
    internal static ScreenPlacement Identity => new(Vector2.Zero, 1f);

    internal Vector2 ToCanvas(Vector2 window) =>
        Scale > 0f ? (window - Origin) / Scale : window - Origin;
}

// One frame's presentation geometry, resolved from a frame view and the back buffer's extent:
//
// Span      the world units the camera spans on Surface. Under Expand or FixedHeight on a render
//           surface that is a whole number of its pixels on the axis the fit grew and the camera's
//           own on the binding axis.
// Surface   the extent the world is drawn on: the render surface where a resolution is declared, the
//           back buffer where none is.
// World     where Span lands on Surface and at what scale, which is the declared pixels per unit on a
//           render surface, since the surface was sized to the span.
// OnSurface where a canvas pixel lands on that surface, when the screen layer is drawn on it.
// Present   where the surface lands in the back buffer. No scale where there is nothing to present.
// Layer     where a canvas pixel lands in the back buffer, which maps a sampled mouse position back
//           to a canvas position.
// ScreenOnSurface
//           whether the screen layer is drawn on Surface, at OnSurface, before it is presented. That
//           happens when the canvas is the render resolution and the interface shares the world's
//           pixels. Otherwise it is drawn straight into the back buffer at Layer, over the presented
//           surface.
internal readonly record struct ScreenLayout(
    Vector2 Span,
    (int Width, int Height) Surface,
    Letterbox World,
    ScreenPlacement OnSurface,
    ScreenPlacement Present,
    ScreenPlacement Layer,
    bool ScreenOnSurface);
