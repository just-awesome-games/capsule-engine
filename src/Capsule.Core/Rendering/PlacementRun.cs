using System.Numerics;

namespace Capsule.Rendering;

// One run of a frame's world list drawn by one placement, from FirstSprite, FirstLine and FirstLight up
// to the next run's indices or the end of the lists. Intent before the first run draws with the world.
//
// A run of the world's own sits at authored world positions. A renderer draws it as if by the frame's
// camera with its centre moved to ScrollCenter + (Centre - ScrollCenter) * ScrollFactor, where the centre
// is that of the world rect the frame places, interpolated and confined as the camera is. A factor of one
// on an axis moves with the world. A Canvas run holds a screen root sorted with the world: it sits in
// canvas pixels, placed as the screen layer is, and its factor is one.
internal readonly record struct PlacementRun(int FirstSprite, int FirstLine, Vector2 ScrollFactor, int FirstLight = 0, bool Canvas = false);
