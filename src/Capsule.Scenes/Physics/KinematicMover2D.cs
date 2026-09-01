using System.Numerics;
using Capsule.Collision;

namespace Capsule.Scenes.Physics;

/// <summary>
/// Attempts translations for one entity through a <see cref="Collider2D"/>, stopping and sliding
/// against the independently configured set of blocking tags. The caller owns movement intent,
/// velocity, acceleration, pushing, and every other gameplay rule; this component only resolves
/// one requested translation.
/// <para>
/// The collider is a selection, not merely a requirement: an entity may carry hurt and hit boxes
/// beside the one body shape that sweeps, and the constructor argument names which. One mover per
/// entity — two would each write the entity's position from their own sweep.
/// </para>
/// </summary>
[DisallowMultipleComponent]
public sealed class KinematicMover2D : Component
{
    private readonly Collider2D _collider;
    private readonly List<string> _blocksOn = [];

    private Contact2D[] _found = new Contact2D[16];
    private ColliderContact2D[] _moveContacts = new ColliderContact2D[16];
    private int _moveContactCount;

    /// <param name="collider">
    /// The collider whose shape this mover sweeps. It must be attached to the same entity as the
    /// mover by the time that entity joins a scene; the two may be attached in either order.
    /// </param>
    public KinematicMover2D(Collider2D collider)
    {
        ArgumentNullException.ThrowIfNull(collider);
        _collider = collider;
    }

    /// <summary>The collider whose shape this mover sweeps.</summary>
    public Collider2D Collider => _collider;

    /// <summary>
    /// The world-specific filter built from <see cref="BlocksOn"/> while this component is in a
    /// scene; <see cref="CollisionFilter.None"/> otherwise.
    /// </summary>
    public CollisionFilter Filter { get; private set; }

    /// <summary>The blocking surfaces encountered by the most recent <see cref="Move"/>.</summary>
    public ReadOnlySpan<ColliderContact2D> MoveContacts => _moveContacts.AsSpan(0, _moveContactCount);

    /// <summary>
    /// Replaces the tags that stop this mover. This is independent of
    /// <see cref="Collider2D.Detects"/>: a collider can report an overlap without that overlap
    /// changing movement.
    /// </summary>
    /// <param name="tags">The tag names that block movement; an empty list blocks on nothing.</param>
    /// <exception cref="ArgumentException">A name is null, empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name.</exception>
    public void BlocksOn(params ReadOnlySpan<string> tags)
    {
        foreach (string tag in tags)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag, nameof(tags));
        }

        CollisionFilter filter = CollisionFilter.None;
        if (Entity?.Scene?.Collision is { } world)
        {
            foreach (string tag in tags)
            {
                filter = filter.With(world.Tag(tag));
            }
        }

        _blocksOn.Clear();
        foreach (string tag in tags)
        {
            _blocksOn.Add(tag);
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
    /// The mover is not in a scene, its collider is disabled or detached, or the collider no
    /// longer belongs to the mover's entity.
    /// </exception>
    public MoveResult2D Move(Vector2 translation)
    {
        Entity entity = Entity
            ?? throw new InvalidOperationException("A KinematicMover2D attached to no entity cannot move one.");

        if (!ReferenceEquals(_collider.Entity, entity))
        {
            throw new InvalidOperationException(
                "A KinematicMover2D cannot move after its collider has left the mover's entity.");
        }

        CollisionWorld2D world = _collider.World
            ?? throw new InvalidOperationException(
                "A KinematicMover2D needs its collider enabled and registered in a scene before it can move.");

        MoveResult2D result = world.Move(
            world.ShapeOf(_collider.Handle),
            entity.Position,
            translation,
            Filter,
            _found,
            _collider.Handle);

        while (result.ContactCount == _found.Length)
        {
            Array.Resize(ref _found, _found.Length * 2);
            result = world.Move(
                world.ShapeOf(_collider.Handle),
                entity.Position,
                translation,
                Filter,
                _found,
                _collider.Handle);
        }

        entity.Position += result.Translation;
        _moveContactCount = Collider2D.Describe(
            world,
            _found.AsSpan(0, result.ContactCount),
            ref _moveContacts);

        return result;
    }

    // Asked as the whole entity is judged, not as the mover is attached: a constructor is free to
    // add the mover before the collider it sweeps, and only by the time the entity joins a scene
    // does the pair have to be on the same entity. Refusing here leaves the entity outside the
    // scene with nothing registered, which is what OnAddingTo already promises.
    internal override void OnAddingTo(Scene scene, Entity entity, List<string> tags)
    {
        if (!ReferenceEquals(_collider.Entity, entity))
        {
            throw new InvalidOperationException(
                "A KinematicMover2D's collider must be attached to the same entity before that entity joins a scene.");
        }

        for (int index = 0; index < _blocksOn.Count; index++)
        {
            Want(scene.Collision, tags, _blocksOn[index]);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnAddedToScene() => Filter = ResolveFilter(Entity!.Scene!.Collision);

    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        Filter = CollisionFilter.None;
        _moveContactCount = 0;
    }

    private CollisionFilter ResolveFilter(CollisionWorld2D world)
    {
        CollisionFilter filter = CollisionFilter.None;
        foreach (string tag in _blocksOn)
        {
            filter = filter.With(world.Tag(tag));
        }

        return filter;
    }

    private static void Want(CollisionWorld2D world, List<string> tags, string name)
    {
        if (!world.TryFindTag(name, out _) && !tags.Contains(name))
        {
            tags.Add(name);
        }
    }
}
