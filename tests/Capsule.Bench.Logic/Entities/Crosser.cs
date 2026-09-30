using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>A small actor that drifts in a straight line through everything and turns back at the edges of its room.</summary>
public sealed class Crosser : Entity
{
    private readonly Vector2 _room;
    private Vector2 _velocity;

    public Crosser(Vector2 position, Vector2 velocity, Vector2 room)
        : base(position)
    {
        _velocity = velocity;
        _room = room;

        BoxCollider2D collider = new(new Vector2(8f, 8f)) { Layer = CollisionLayers.Actor };
        Add(collider);
    }

    protected override void OnStep(in StepContext context)
    {
        Vector2 next = Position + _velocity;
        if (next.X < 0f || next.X > _room.X)
        {
            _velocity.X = -_velocity.X;
        }

        if (next.Y < 0f || next.Y > _room.Y)
        {
            _velocity.Y = -_velocity.Y;
        }

        Position = Vector2.Clamp(next, Vector2.Zero, _room);
    }
}
