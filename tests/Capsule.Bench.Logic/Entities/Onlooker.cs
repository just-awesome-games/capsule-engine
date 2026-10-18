namespace Capsule.Bench.Logic.Entities;

/// <summary>A body that crates move, standing still where it is placed.</summary>
public sealed class Onlooker : Entity
{
    public Onlooker(Vector2 position)
        : base(position)
    {
        BoxCollider2D collider = new(new Vector2(12f, 24f)) { Layer = CollisionLayers.Actor };
        Add(collider);

        KinematicBody2D body = new(collider);
        body.BlockedBy = new(CollisionLayers.Solid, CollisionLayers.Crate);
        body.MovedBy = new(CollisionLayers.Crate);
        Add(body);
    }
}
