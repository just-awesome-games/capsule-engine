using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

public sealed class Spark : Entity
{
    private static readonly Sprite Frame = new(CapsuleAssets.Textures.Terrain, new TextureRegion(0, 0, 4, 4));

    private readonly Vector2 _velocity;
    private int _life = 180;

    public Spark(Vector2 origin, Vector2 velocity)
        : base(origin)
    {
        _velocity = velocity;
        Add(new SpriteRenderer(Frame));
    }

    protected override void OnStep(in StepContext context)
    {
        Position += _velocity;

        if (--_life <= 0)
        {
            Scene!.Remove(this);
        }
    }
}
