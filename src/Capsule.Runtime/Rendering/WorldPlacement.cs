using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Runtime.Rendering;

// Where a drawn frame's world landed in the window. Ends is where the frame placed the world, Fit where its rect
// landed on the surface, and Present where the surface landed in the window. Parallax is ScrollLayout.Parallax of
// the drawn rect, which places each scrolled layer.
internal readonly record struct WorldPlacement(WorldEnds Ends, Letterbox Fit, ScreenPlacement Present, Vector2 Parallax)
{
    internal float PixelsPerUnit => Fit.Scale * Present.Scale;

    // The window pixel the world rect's top-left corner landed on.
    private Vector2 Corner => Present.Origin + (new Vector2(Fit.X, Fit.Y) * Present.Scale);

    // The world point drawn under a window pixel. A pixel off the world's region extrapolates.
    internal Vector2 ToWorld(Vector2 window) => Ends.Corner + ((window - Corner) / PixelsPerUnit);

    // Whether a window pixel lies on the world's region, not on a bar beside it.
    internal bool Contains(Vector2 window)
    {
        Vector2 corner = Corner;
        Vector2 extent = new Vector2(Fit.Width, Fit.Height) * Present.Scale;

        return window.X >= corner.X && window.Y >= corner.Y
            && window.X < corner.X + extent.X && window.Y < corner.Y + extent.Y;
    }
}
