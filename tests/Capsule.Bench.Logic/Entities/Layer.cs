using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Bench.Logic.Entities;

// One band of the strip repeating without bound along X; the document's scroll factor is its depth
// and its id picks the band.
public sealed class Layer : Entity
{
    public Layer(EntitySpawn spawn)
        : base(spawn)
    {
        Sprite band = new(CapsuleAssets.Textures.Strip, new TextureRegion(0, ((spawn.Id - 1) % 8) * 16, 128, 16));

        Add(new SpriteRenderer(band) { Tiling = new Vector2(float.PositiveInfinity, 0f) });
    }
}
