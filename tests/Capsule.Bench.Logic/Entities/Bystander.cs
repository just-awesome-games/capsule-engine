using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Bench.Logic.Entities;

// Only every third one draws, so most are stepped and culled without submitting anything.
public sealed class Bystander : Entity
{
    private static readonly Sprite Frame = new(CapsuleAssets.Textures.Terrain, new TextureRegion(0, 0, 16, 16));

    private readonly Vector2 _drift;

    public Bystander(EntitySpawn spawn)
        : base(spawn)
    {
        _drift = new Vector2(((spawn.Id % 5) - 2) * 0.25f, 0f);

        if (spawn.Id % 3 == 0)
        {
            Add(new SpriteRenderer(Frame));
        }
    }

    protected override void OnStep(in StepContext context) => Position += _drift;
}
