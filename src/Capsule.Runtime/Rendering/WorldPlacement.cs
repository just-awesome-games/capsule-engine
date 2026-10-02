using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Runtime.Rendering;

// Where a drawn frame's world landed in the back buffer. TopLeft is the world rect's corner, where the
// frame's pixel grid is anchored. Fit is where that rect landed on the surface, Present where the
// surface landed in the back buffer, and Snap whether the frame quantised to the surface's pixel grid.
internal readonly record struct WorldPlacement(Vector2 TopLeft, Letterbox Fit, ScreenPlacement Present, bool Snap)
{
    internal float PixelsPerUnit => Fit.Scale * Present.Scale;

    // The back-buffer pixel the world rect's top-left corner landed on.
    private Vector2 Corner => Present.Origin + (new Vector2(Fit.X, Fit.Y) * Present.Scale);

    // The world point drawn under a back-buffer pixel. A pixel off the world's region extrapolates.
    internal Vector2 ToWorld(Vector2 window) => TopLeft + ((window - Corner) / PixelsPerUnit);

    // Whether a back-buffer pixel lies on the world's region, not on a bar beside it.
    internal bool Contains(Vector2 window)
    {
        Vector2 corner = Corner;
        Vector2 extent = new Vector2(Fit.Width, Fit.Height) * Present.Scale;

        return window.X >= corner.X && window.Y >= corner.Y
            && window.X < corner.X + extent.X && window.Y < corner.Y + extent.Y;
    }
}
