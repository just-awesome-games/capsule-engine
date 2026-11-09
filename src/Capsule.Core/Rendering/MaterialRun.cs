namespace Capsule.Rendering;

// One run of a layer's sprites and polygons drawn with one material, from FirstSprite and FirstPolygon up to
// the next run's or the end of each list. A null material is the engine's own sprite shader. Intent before the
// first run draws with it too.
internal readonly record struct MaterialRun(int FirstSprite, Material? Material, int FirstPolygon = 0);
