namespace Capsule.Bench.Logic.Entities;

/// <summary>A child that reports its contacts with walls and tracks whether it is on screen, as a shed limb or a regrown part would.</summary>
public sealed class Bud : Entity
{
    public Bud()
        : base(new Vector2(0f, -6f))
    {
        BoxCollider2D collider = new(new Vector2(6f, 6f)) { ReportsContacts = true };
        collider.Detects = new(CollisionLayers.Solid);
        Add(collider);
        Add(new VisibleOnScreenNotifier2D { Rect = new Rect(Vector2.Zero, new Vector2(6f, 6f)) });
    }
}
