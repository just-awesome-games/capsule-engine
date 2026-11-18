using System.Numerics;
using System.Runtime.CompilerServices;
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

    // Where a point at previous and current lands, as the vertex stage places a sprite's pivot. A point and a
    // placement that both hold still have two equal ends, which Between returns unchanged, and the point is counted once.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Vector2 Place(Vector2 previous, Vector2 current) =>
        Grid > 0f && previous == current && PreviousLayerCorner == LayerCorner && Grids.X == Grids.Y && Shift == Vector2.Zero
            ? OnGrid(PixelGrid.Pixels(LayerCorner, current, Grids.Y))
            : Carry(previous, current);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vector2 Carry(Vector2 previous, Vector2 current)
    {
        if (!(Grid > 0f))
        {
            return FrameCorner + (StepInterpolation.Interpolate(previous, current, Alpha) - LayerCorner);
        }

        return OnGrid(PixelGrid.Between(
            PixelGrid.Pixels(PreviousLayerCorner, previous, Grids.X),
            PixelGrid.Pixels(LayerCorner, current, Grids.Y) + Shift,
            Alpha));
    }

    // Whole pixels drawn at Grid, from FrameCorner.
    private Vector2 OnGrid(Vector2 pixels) =>
        FrameCorner + new Vector2((float)(pixels.X / (double)Grid), (float)(pixels.Y / (double)Grid));
}
