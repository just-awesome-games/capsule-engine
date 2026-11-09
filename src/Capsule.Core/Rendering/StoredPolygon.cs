namespace Capsule.Rendering;

// One polygon of a list as the frame stored it. Its outline is Count points at the end of the previous step,
// from FirstPoint in the frame's point list, then the same Count points now. Before is how many sprites its
// list held when it was stored, which places it among them. Color carries the running entity's tint, Flash its
// composed Entity.Flash as a sprite's does, and Bounds the rect the outline sweeps.
internal readonly record struct StoredPolygon(int Before, int FirstPoint, int Count, ColorRgba Color, BlendMode Blend, ColorRgba Flash, Rect Bounds);
