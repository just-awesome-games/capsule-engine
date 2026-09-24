using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>A root that drifts up and down the viewport's height at its own speed, turning at the edges, with a shadow child drawn in its band.</summary>
public sealed class Wanderer : Entity
{
    private static readonly Sprite Frame = new(CapsuleAssets.Textures.TerrainTexture, new TextureRegion(0, 0, 8, 8));

    private float _speed;

    public Wanderer(Vector2 position, float speed)
        : base(position)
    {
        _speed = speed;

        Add(new SpriteRenderer(Frame));

        Entity shadow = new Shadow();
        shadow.Parent = this;
    }

    protected override void OnStep(in StepContext context)
    {
        float y = Position.Y + (_speed * context.DeltaSeconds);
        if (y < 0f || y > World.ViewportSize.Y)
        {
            _speed = -_speed;
            y = Math.Clamp(y, 0f, World.ViewportSize.Y);
        }

        Position = new Vector2(Position.X, y);
    }

    private sealed class Shadow : Entity
    {
        internal Shadow()
            : base(new Vector2(0f, 8f)) => Add(new SpriteRenderer(Frame) { Color = new ColorRgba(0, 0, 0, 96) });
    }
}
