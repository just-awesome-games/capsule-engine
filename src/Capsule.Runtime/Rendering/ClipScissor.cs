using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Runtime.Rendering;

// Maps a clip's canvas-pixel rect to the surface pixels the device scissors to.
internal static class ClipScissor
{
    // What a clip shows at grid's alpha, in whole surface pixels cut to the surface. Each level's corners are placed
    // by grid, as a sprite on the canvas is, then put on the surface at placement and intersected, and each edge is
    // rounded. Placing each level first matters. The intersection of the ends is not the end of the intersection when
    // an inner level moves inside an outer one. A snapped edge changes pixel on the frame a sprite moving with it
    // does. Rounding the edges and not the corner and size keeps two abutting clips on one shared pixel boundary.
    // False when nothing is left, which is a rect with no area, one off the surface, and one that rounds to no width
    // or height.
    internal static bool TryShow(
        ReadOnlySpan<ClipRegion> levels,
        in GridPlacement grid,
        in ScreenPlacement placement,
        int surfaceWidth,
        int surfaceHeight,
        out int x,
        out int y,
        out int width,
        out int height)
    {
        x = y = width = height = 0;
        Rect shown = Rect.FromEdges(float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity);
        for (int i = 0; i < levels.Length; i++)
        {
            Rect previous = levels[i].Previous;
            Rect current = levels[i].Current;
            Vector2 low = grid.Place(previous.Position, current.Position);
            Vector2 high = grid.Place(new Vector2(previous.Right, previous.Bottom), new Vector2(current.Right, current.Bottom));
            shown = Rect.Intersection(shown, Rect.FromCorners(low, high));
        }

        if (shown.IsEmpty)
        {
            return false;
        }

        Vector2 topLeft = OnSurface(shown.Position, in grid, in placement);
        Vector2 bottomRight = OnSurface(new Vector2(shown.Right, shown.Bottom), in grid, in placement);
        int left = (int)Math.Clamp(topLeft.X, 0f, surfaceWidth);
        int top = (int)Math.Clamp(topLeft.Y, 0f, surfaceHeight);
        int right = (int)Math.Clamp(bottomRight.X, 0f, surfaceWidth);
        int bottom = (int)Math.Clamp(bottomRight.Y, 0f, surfaceHeight);
        if (right <= left || bottom <= top)
        {
            return false;
        }

        x = left;
        y = top;
        width = right - left;
        height = bottom - top;

        return true;
    }

    // A canvas point on the surface pixel it lands on, by the midpoint rule a snapped sprite rounds by, or to the
    // nearest otherwise.
    private static Vector2 OnSurface(Vector2 canvas, in GridPlacement grid, in ScreenPlacement placement)
    {
        Vector2 surface = (canvas * placement.Scale) + placement.Origin;

        return grid.Grid > 0f ? PixelGrid.Whole(surface) : new Vector2(MathF.Round(surface.X), MathF.Round(surface.Y));
    }
}
