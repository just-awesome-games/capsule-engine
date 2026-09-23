using System.Numerics;
using Capsule;
using Capsule.Animation;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// A low block that slides side to side along the floor on an eased swing. A body moved by its layer
/// rides it and is shoved by it.
/// </summary>
public sealed class Shuttle : Entity
{
    private static readonly Vector2 Size = new(24f, 8f);
    private const float Travel = 48f;
    private const int SwingTicks = 120;

    private readonly Vector2 _left;
    private Tween _swing;

    public Shuttle(EntitySpawn spawn)
        : base(spawn)
    {
        _left = Position;
        Add(new BoxCollider2D(Size) { Layer = CollisionLayers.Platform });
        Add(new ColorRect(Size) { Color = ColorRgba.FromHex("#5a8870") });
        _swing.Start(SwingTicks, Ease.InOutSine, TweenLoop.PingPong);
    }

    protected override void OnStep(in StepContext context)
    {
        _swing.Step();
        Position = _left + new Vector2(Travel * _swing.Value, 0f);
    }
}
