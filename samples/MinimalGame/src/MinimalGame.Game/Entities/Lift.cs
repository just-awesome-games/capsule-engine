using System.Numerics;
using Capsule;
using Capsule.Animation;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// A slab that rises and sinks on an eased swing. A body moved by its layer rides it and is shoved
/// by it: the lift only moves itself.
/// </summary>
public sealed class Lift : Entity
{
    private static readonly Vector2 Size = new(32f, 6f);
    private const int SwingTicks = 150;

    private readonly Vector2 _bottom;
    private Tween _swing;

    /// <summary>How far the lift climbs above where it rests, in world units. room.scene.json sets it per lift.</summary>
    [Authorable]
    public float Rise { get; set; } = 64f;

    /// <summary>Whether the lift swings, defaulting to true.</summary>
    [Authorable]
    public bool Running { get; set; } = true;

    public Lift(EntitySpawn spawn)
        : base(spawn)
    {
        _bottom = Position;
        Add(new BoxCollider2D(Size) { Layer = CollisionLayers.Platform });
        Add(new ColorRect(Size) { Color = ColorRgba.FromHex("#5a6488") });
        _swing.Start(SwingTicks, Ease.InOutSine, TweenLoop.PingPong);
    }

    protected override void OnStep(in StepContext context)
    {
        if (!Running)
        {
            return;
        }

        _swing.Step();
        Position = _bottom - new Vector2(0f, Rise * _swing.Value);
    }
}
