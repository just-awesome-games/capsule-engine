using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>A pooled shot that flies straight and leaves the scene on the first wall it touches, or when its time is up.</summary>
public sealed class Bullet : Entity
{
    private const int LifeSteps = 60;

    private Vector2 _velocity;
    private int _life;

    public Bullet()
        : base(Vector2.Zero)
    {
        BoxCollider2D collider = new(new Vector2(4f, 4f)) { ReportsContacts = true };
        collider.Detects = new(CollisionLayers.Solid);
        collider.ContactEntered += _ => Scene?.Remove(this);
        Add(collider);
    }

    public void Launch(Vector2 origin, Vector2 velocity)
    {
        Position = origin;
        _velocity = velocity;
        _life = LifeSteps;
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
