namespace Capsule.Rendering;

// One run of a layer's sprite list drawn with one material, from FirstSprite up to the next run's first
// sprite or the end of the list. A null material is the engine's own sprite shader. Sprites before the
// first run draw with it too.
internal readonly record struct MaterialRun(int FirstSprite, Material? Material);
