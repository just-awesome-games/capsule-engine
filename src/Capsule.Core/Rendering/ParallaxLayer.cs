using System.Numerics;

namespace Capsule.Rendering;

// One run of a frame's world list drawn by a scroll factor, from FirstSprite, FirstLine and FirstLight
// up to the next layer's indices or the end of the lists. The intent in the run sits at authored world
// positions. A renderer draws it as if by the frame's camera with its top-left corner moved to
// ScrollOrigin + (Corner - ScrollOrigin) * ScrollFactor, where the corner is that of the world rect the
// frame places, interpolated and confined as the camera is. A factor of one on an axis moves with the
// world.
internal readonly record struct ParallaxLayer(int FirstSprite, int FirstLine, Vector2 ScrollFactor, int FirstLight = 0);
