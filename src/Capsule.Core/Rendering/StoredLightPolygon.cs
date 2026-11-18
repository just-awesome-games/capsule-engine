namespace Capsule.Rendering;

// One polygon light as the frame stored it: Count points at the end of the previous step from FirstPoint in the
// frame's point list, then the same Count points now. Color carries the running entity's tint, and its RGB scaled
// by its alpha and by Intensity is added into the light.
internal readonly record struct StoredLightPolygon(int FirstPoint, int Count, ColorRgba Color, float Intensity);
