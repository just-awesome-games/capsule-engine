using System.Numerics;
using Capsule.Collision;

namespace Capsule.Scenes.Physics;

/// <summary>
/// Sweeps one selected <see cref="Collider2D"/> through the scene's collision world, stopping and
/// sliding against the independently configured set of blocking layers, and reports which way it
/// was stopped. The caller owns velocity, acceleration, and every gameplay response; this component
/// applies no force and never touches a velocity.
/// <para>
/// The collider is a selection, not merely a requirement: an entity may carry hurt and hit boxes
/// beside the one body shape that sweeps, and the constructor argument names which. One body per
/// entity — two would each write the entity's position from their own sweep.
/// </para>
/// <para>
/// <see cref="IsOnFloor"/>, <see cref="IsOnWall"/> and <see cref="IsOnCeiling"/> are state as of
/// the last <see cref="Move(Vector2)"/> and nothing more — a move that pressed into nothing clears
/// them, and so does a zero translation, which sweeps nothing at all.
/// </para>
/// </summary>
public sealed class KinematicBody2D : Component
{
    // Y-down, so up is negative Y. A property only once a consumer flips gravity.
    private static readonly Vector2 Up = new(0f, -1f);

    // Within 45° of up is a floor and within 45° of down a ceiling; everything between is a wall.
    // The axis-separated sweep resolves no motion along a slope, so no floor-angle knob is exposed.
    private const float FloorDot = 0.7071f;

    private readonly Collider2D _collider;
    private readonly List<string> _blocksOn = [];

    private Contact2D[] _found = new Contact2D[16];
    private ColliderContact2D[] _moveContacts = new ColliderContact2D[16];
    private int _moveContactCount;

    /// <param name="collider">
    /// The collider whose shape this body sweeps. It must be attached to the same entity as the
    /// body by the time that entity joins a scene; the two may be attached in either order.
    /// </param>
    public KinematicBody2D(Collider2D collider)
    {
        ArgumentNullException.ThrowIfNull(collider);
        _collider = collider;
    }

    /// <summary>The collider whose shape this body sweeps.</summary>
    public Collider2D Collider => _collider;

    /// <summary>
    /// The world-specific filter built from <see cref="BlocksOn"/> while this component is in a
    /// scene; <see cref="CollisionFilter.None"/> otherwise.
    /// </summary>
    public CollisionFilter Filter { get; private set; }

    /// <summary>The surfaces the most recent <see cref="Move(Vector2)"/> reached.</summary>
    public ReadOnlySpan<ColliderContact2D> MoveContacts => _moveContacts.AsSpan(0, _moveContactCount);

    /// <summary>Whether the last move was stopped by a floor: a blocking contact whose normal points up.</summary>
    public bool IsOnFloor { get; private set; }

    /// <summary>Whether the last move was stopped by a wall: a blocking contact whose normal is neither floor nor ceiling.</summary>
    public bool IsOnWall { get; private set; }

    /// <summary>Whether the last move was stopped by a ceiling: a blocking contact whose normal points down.</summary>
    public bool IsOnCeiling { get; private set; }

    /// <summary>The floor contact's normal, or zero when <see cref="IsOnFloor"/> is false.</summary>
    public Vector2 FloorNormal { get; private set; }

    /// <summary>
    /// The wall contact's normal, or zero when <see cref="IsOnWall"/> is false. Its X sign says
    /// which side the wall is on.
    /// </summary>
    public Vector2 WallNormal { get; private set; }

    /// <summary>
    /// Replaces the layers that stop this body. This is independent of
    /// <see cref="Collider2D.Detects"/>: a collider can report an overlap without that overlap
    /// changing movement.
    /// </summary>
    /// <param name="names">The layer names that block movement; an empty list blocks on nothing.</param>
    /// <exception cref="ArgumentException">A name is null, empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name.</exception>
    public void BlocksOn(params ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(names));
        }

        CollisionFilter filter = CollisionFilter.None;
        if (Entity?.Scene?.Collision is { } world)
        {
            foreach (string name in names)
            {
                filter = filter.With(world.Layer(name));
            }
        }

        _blocksOn.Clear();
        foreach (string name in names)
        {
            _blocksOn.Add(name);
        }

        if (InScene)
        {
            Filter = filter;
        }
    }

    /// <summary>
    /// Attempts <paramref name="translation"/>, writes the collision-resolved translation to the
    /// entity, and returns what was applied. This performs no implicit velocity or force update.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The body is not in a scene, its collider is disabled or detached, or the collider no
    /// longer belongs to the body's entity.
    /// </exception>
    public MoveResult2D Move(Vector2 translation) => MoveWith(translation, Filter);

    /// <summary>
    /// Attempts <paramref name="translation"/> against <paramref name="blocking"/> instead of
    /// <see cref="Filter"/>, for this call alone. <see cref="BlocksOn"/> is untouched, so the next
    /// plain <see cref="Move(Vector2)"/> resolves against it again.
    /// <para>
    /// For a body whose blocking set is a decision rather than a setting: where what stops it
    /// depends on the state of the step — which way it is travelling, what it already overlaps,
    /// what the game has just resolved — the caller composes the filter and passes it here.
    /// </para>
    /// </summary>
    /// <param name="translation">The move to attempt.</param>
    /// <param name="blocking">What may stop it; <see cref="CollisionFilter.None"/> stops it on nothing.</param>
    /// <exception cref="InvalidOperationException">
    /// The body is not in a scene, its collider is disabled or detached, or the collider no
    /// longer belongs to the body's entity.
    /// </exception>
    /// <exception cref="ArgumentException">The filter was built from another collision world's layers.</exception>
    public MoveResult2D Move(Vector2 translation, CollisionFilter blocking) => MoveWith(translation, blocking);

    private MoveResult2D MoveWith(Vector2 translation, CollisionFilter blocking)
    {
        Entity entity = Entity
            ?? throw new InvalidOperationException("A KinematicBody2D attached to no entity cannot move one.");

        if (!ReferenceEquals(_collider.Entity, entity))
        {
            throw new InvalidOperationException(
                "A KinematicBody2D cannot move after its collider has left the body's entity.");
        }

        CollisionWorld2D world = _collider.World
            ?? throw new InvalidOperationException(
                "A KinematicBody2D needs its collider enabled and registered in a scene before it can move.");

        MoveResult2D result = world.Move(
            world.ShapeOf(_collider.Handle),
            entity.Position,
            translation,
            blocking,
            _found,
            _collider.Handle);

        while (result.ContactCount == _found.Length)
        {
            Array.Resize(ref _found, _found.Length * 2);
            result = world.Move(
                world.ShapeOf(_collider.Handle),
                entity.Position,
                translation,
                blocking,
                _found,
                _collider.Handle);
        }

        entity.Position += result.Translation;
        _moveContactCount = Collider2D.Describe(
            world,
            _found.AsSpan(0, result.ContactCount),
            ref _moveContacts);

        Classify(result);

        return result;
    }

    // The body holds the entity's one write on its position, so a second one is a mistake the
    // attach refuses.
    internal override void OnAttachedTo(Entity entity)
    {
        foreach (Component held in entity.Components)
        {
            if (!ReferenceEquals(held, this) && held is KinematicBody2D)
            {
                throw new InvalidOperationException(
                    $"A {entity.GetType().Name} already holds a KinematicBody2D; two would each write the entity's position from their own sweep.");
            }
        }
    }

    // Asked as the whole entity joins, not as the body is attached: a constructor is free to add
    // the body before the collider it sweeps, and only by the time the entity joins a scene does
    // the pair have to be on the same entity.
    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        if (!ReferenceEquals(_collider.Entity, Entity))
        {
            throw new InvalidOperationException(
                "A KinematicBody2D's collider must be attached to the same entity before that entity joins a scene.");
        }

        Filter = Collider2D.ResolveFilter(Entity!.Scene!.Collision, _blocksOn);
    }

    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        Filter = CollisionFilter.None;
        _moveContactCount = 0;
        IsOnFloor = false;
        IsOnWall = false;
        IsOnCeiling = false;
        FloorNormal = Vector2.Zero;
        WallNormal = Vector2.Zero;
    }

    // Every recorded contact sits at its axis's nearest sweep fraction, and a surface the sweep
    // moved away from is never recorded at all — but a hit landing exactly at the end of a
    // translation is, and that one stopped nothing. Only the sweep a contact belongs to says
    // whether it did: the span is the X sweep's contacts followed by the Y sweep's, and each range
    // is judged by its own axis's blocked flag.
    private void Classify(in MoveResult2D result)
    {
        IsOnFloor = false;
        IsOnWall = false;
        IsOnCeiling = false;
        FloorNormal = Vector2.Zero;
        WallNormal = Vector2.Zero;

        for (int index = 0; index < _moveContactCount; index++)
        {
            bool blocked = index < result.XContactCount ? result.BlockedX : result.BlockedY;
            if (!blocked)
            {
                continue;
            }

            Vector2 normal = _moveContacts[index].Normal;
            float upwards = Vector2.Dot(normal, Up);

            if (upwards > FloorDot)
            {
                if (!IsOnFloor)
                {
                    IsOnFloor = true;
                    FloorNormal = normal;
                }
            }
            else if (upwards < -FloorDot)
            {
                IsOnCeiling = true;
            }
            else if (!IsOnWall)
            {
                IsOnWall = true;
                WallNormal = normal;
            }
        }
    }
}
