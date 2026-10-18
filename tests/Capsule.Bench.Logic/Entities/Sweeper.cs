namespace Capsule.Bench.Logic.Entities;

/// <summary>A box that overlaps every collider under it once a step, as a screen-clearing bomb would, and folds the count into <see cref="Found"/>, which keeps the query from being elided.</summary>
public sealed class Sweeper : Entity
{
    private readonly Vector2 _size;
    private readonly Contact2D[] _contacts = new Contact2D[4_096];

    public long Found { get; private set; }

    public Sweeper(Vector2 position, Vector2 size)
        : base(position) => _size = size;

    protected override void OnStep(in StepContext context) =>
        Found += Scene!.Collision.OverlapBoxAll(Aabb2D.FromCorner(Position - (_size / 2f), _size), CollisionFilter.Everything, _contacts);
}
