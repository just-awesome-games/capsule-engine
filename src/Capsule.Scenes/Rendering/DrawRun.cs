using Capsule.Scenes;

namespace Capsule.Rendering;

// The world sprites and polygons one entity's renderers stored in a frame, from FirstSprite and FirstPolygon
// up to the next run's.
internal readonly record struct DrawRun(Entity Entity, int FirstSprite, int FirstPolygon);
