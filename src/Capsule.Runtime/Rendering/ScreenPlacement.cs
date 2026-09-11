using System.Numerics;

namespace Capsule.Runtime.Rendering;

// Where the screen layer landed in the window on the last frame drawn: a canvas pixel is Origin plus
// itself times Scale, in back-buffer pixels. The inverse is how a window position — the mouse's —
// becomes the canvas position the simulation reads. Scale 1 at the origin until a frame has drawn, so
// a device sampled before the first draw maps one window pixel to one canvas pixel rather than
// dividing by zero.
internal readonly record struct ScreenPlacement(Vector2 Origin, float Scale)
{
    internal static ScreenPlacement Identity => new(Vector2.Zero, 1f);

    internal Vector2 ToCanvas(Vector2 window) =>
        Scale > 0f ? (window - Origin) / Scale : window - Origin;
}
