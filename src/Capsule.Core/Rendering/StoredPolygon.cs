namespace Capsule.Rendering;

// One polygon of a list as the frame stored it. Its outline is Count points at the end of the previous step,
// from FirstPoint in the frame's point list, then the same Count points now. Before is how many sprites its
// list held when it was stored, which places it among them. Color carries the running entity's tint, Flash its
// composed Entity.Flash as a sprite's does, and Bounds the rect the outline sweeps.
//
// One with no points holds a chunk of tiles in place of an outline, drawn where the polygon would be. Color is then
// the running tint, Flash the running flash and Bounds the chunk's rect.
internal readonly record struct StoredPolygon(int Before, int FirstPoint, int Count, ColorRgba Color, BlendMode Blend, ColorRgba Flash, Rect Bounds)
{
    internal bool HoldsTiles => Count == 0;

    // Where the frame's TileRuns hold the chunk of one that holds tiles.
    internal int TileRunIndex => FirstPoint;
}
