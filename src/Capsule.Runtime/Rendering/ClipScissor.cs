using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Runtime.Rendering;

// Maps a clip's canvas-pixel rect to the surface pixels the device scissors to.
internal static class ClipScissor
{
    // What a clip shows at `alpha` through the step: each level moved between its two ends, then all of them
    // intersected. Interpolating first matters. The intersection of the ends is not the end of the
    // intersection when an inner level moves inside an outer one.
    internal static Rect Shown(ReadOnlySpan<ClipRegion> levels, float alpha)
    {
        Rect shown = new(float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity);
        for (int i = 0; i < levels.Length; i++)
        {
            Vector2 low = StepInterpolation.Interpolate(levels[i].Previous.Position, levels[i].Current.Position, alpha);
            Vector2 high = StepInterpolation.Interpolate(
                new Vector2(levels[i].Previous.Right, levels[i].Previous.Bottom),
                new Vector2(levels[i].Current.Right, levels[i].Current.Bottom),
                alpha);

            shown = Rect.Intersection(shown, new Rect(low.X, low.Y, high.X, high.Y));
        }

        return shown;
    }

    // The rect placed on the surface with its edges rounded to whole pixels, then cut to the surface.
    // False when nothing is left, which is a rect with no area, one off the surface, and one that rounds
    // to no width or height. Rounding the edges and not the corner and size keeps two abutting clips on
    // one shared pixel boundary.
    internal static bool TryMap(in Rect canvas, in ScreenPlacement placement, int surfaceWidth, int surfaceHeight, out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        if (canvas.IsEmpty)
        {
            return false;
        }

        float scale = placement.Scale;
        float left = MathF.Round((canvas.Left * scale) + placement.Origin.X);
        float top = MathF.Round((canvas.Top * scale) + placement.Origin.Y);
        float right = MathF.Round((canvas.Right * scale) + placement.Origin.X);
        float bottom = MathF.Round((canvas.Bottom * scale) + placement.Origin.Y);

        int clippedLeft = (int)Math.Clamp(left, 0f, surfaceWidth);
        int clippedTop = (int)Math.Clamp(top, 0f, surfaceHeight);
        int clippedRight = (int)Math.Clamp(right, 0f, surfaceWidth);
        int clippedBottom = (int)Math.Clamp(bottom, 0f, surfaceHeight);
        if (clippedRight <= clippedLeft || clippedBottom <= clippedTop)
        {
            return false;
        }

        x = clippedLeft;
        y = clippedTop;
        width = clippedRight - clippedLeft;
        height = clippedBottom - clippedTop;

        return true;
    }
}
