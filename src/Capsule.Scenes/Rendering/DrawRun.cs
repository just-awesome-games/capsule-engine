using Capsule.Scenes;

namespace Capsule.Rendering;

// The world sprites one entity's renderers stored in a frame, from FirstSprite up to the next run's.
internal readonly record struct DrawRun(Entity Entity, int FirstSprite);
