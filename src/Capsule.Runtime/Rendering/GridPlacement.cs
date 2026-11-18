using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Runtime.Rendering;

// Where the quads drawn next are placed, as the vertex stage reads it from SpriteUniforms. A point given at both
// ends of the step lands at FrameCorner plus its offset from the layer's corner, in world units. Where Grid is
// above zero the frame snaps: each end is counted in whole pixels from that end's layer corner at that end's grid,
// the current end moved by Shift, the two carried between by PixelGrid.Between and drawn at Grid. Otherwise the
// point is interpolated by Alpha and placed from LayerCorner, and PreviousLayerCorner, Shift and Grids are unread.
internal readonly record struct GridPlacement(
    Vector2 PreviousLayerCorner,
    Vector2 LayerCorner,
    Vector2 FrameCorner,
    Vector2 Shift,
    Vector2 Grids,
    float Grid,
    float Alpha)
{
    // Canvas pixels at grid surface pixels each, which hold still through the step, snapped where snap holds.
    internal static GridPlacement Canvas(float grid, bool snap, float alpha) =>
        new(Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, new Vector2(grid), snap ? grid : 0f, alpha);

    // Where a point at previous and current lands, as the vertex stage places a sprite's pivot.
    internal Vector2 Place(Vector2 previous, Vector2 current)
    {
        if (!(Grid > 0f))
        {
            return FrameCorner + (StepInterpolation.Interpolate(previous, current, Alpha) - LayerCorner);
        }

        Vector2 pixels = PixelGrid.Between(
            PixelGrid.Pixels(PreviousLayerCorner, previous, Grids.X),
            PixelGrid.Pixels(LayerCorner, current, Grids.Y) + Shift,
            Alpha);

        return FrameCorner + new Vector2((float)(pixels.X / (double)Grid), (float)(pixels.Y / (double)Grid));
    }
}
