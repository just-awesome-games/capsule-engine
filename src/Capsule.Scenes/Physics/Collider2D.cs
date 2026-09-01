using System.Numerics;
using System.Runtime.CompilerServices;
using Capsule.Collision;

namespace Capsule.Scenes.Physics;

/// <summary>
/// Gives its entity a shape in the scene's <see cref="Scene.Collision"/> world. It registers when
/// its entity joins a scene and unregisters when it leaves, and it follows the entity's
/// <see cref="Scenes.Entity.Position"/> — direct writes and teleports included — so a query never
/// sees a stale one.
/// <para>
/// The shape itself belongs to the subclass: <see cref="BoxCollider2D"/>,
/// <see cref="CircleCollider2D"/>, <see cref="CapsuleCollider2D"/> and
/// <see cref="PolygonCollider2D"/> each hold their own authoring state and rebuild
/// <see cref="Shape"/> from it. Where that shape sits relative to the entity's position is each
/// subclass's own convention.
/// </para>
/// </summary>
public abstract class Collider2D : Component
{
    private readonly List<string> _detects = [];

    private Shape2D _shape;

    // The shape as the world holds it: this shape at this offset.
    private Shape2D _local;
    private Vector2 _offset;
    private string _layer = CollisionWorld2D.DefaultLayerName;
    private bool _enabled = true;
    private bool _reportsContacts;

    private CollisionWorld2D? _world;
    private Scene? _scene;
    private ColliderHandle _handle;

    private Contact2D[] _found = new Contact2D[16];
    private ColliderContact2D[] _touching = new ColliderContact2D[16];
    private ColliderContact2D[] _wasTouching = new ColliderContact2D[16];
    private int _touchingCount;
    private int _wasTouchingCount;

    // True while this collider's own enter and exit handlers are running. What they are being told
    // about must not change underneath them, so the setters that would change it throw.
    private bool _dispatching;

    /// <summary>The shape this collider starts out holding, expressed relative to the entity's position.</summary>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, which is no shape at all.</exception>
    protected Collider2D(in Shape2D shape)
    {
        RequireShape(shape);

        _shape = shape;
        _local = shape;
    }

    /// <summary>
    /// Raised for each thing this collider began touching since the previous step. A handler may
    /// not reconfigure the collider it is being raised for; see <see cref="Enabled"/>.
    /// </summary>
    public event Action<ColliderContact2D>? ContactEntered;

    /// <summary>
    /// Raised for each thing this collider stopped touching since the previous step, and for
    /// everything it was still touching when it left its scene or was disabled. Handlers are bound
    /// by the same rule as <see cref="ContactEntered"/>.
    /// </summary>
    public event Action<ColliderContact2D>? ContactExited;

    /// <summary>The shape, in the collider's own space; <see cref="Offset"/> and the entity's position place it.</summary>
    public Shape2D Shape => _shape;

    /// <summary>Added to the entity's position to place the shape; zero by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The offset is not finite.</exception>
    /// <exception cref="ArgumentException">The shape cannot be placed at this offset.</exception>
    /// <exception cref="InvalidOperationException">The collider's contacts are being dispatched.</exception>
    public Vector2 Offset
    {
        get => _offset;
        set
        {
            RequireNotDispatching();

            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A collider's offset must be finite.");
            }

            RequirePlaceable(_shape, value);

            _offset = value;
            _local = _shape.Translated(value);
            Resync();
        }
    }

    /// <summary>
    /// Whether this collider participates in its scene's collision world. A disabled collider
    /// remains attached to its entity but cannot be hit, queried, or report contacts.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The collider's contacts are being dispatched: a <see cref="ContactEntered"/> or
    /// <see cref="ContactExited"/> handler cannot reconfigure the collider it was raised for.
    /// </exception>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            RequireNotDispatching();
            _enabled = value;
            if (_scene is null)
            {
                return;
            }

            if (value)
            {
                Register();
            }
            else
            {
                Unregister();
            }
        }
    }

    /// <summary>
    /// Whether this collider computes what it is touching every step and raises
    /// <see cref="ContactEntered"/> and <see cref="ContactExited"/> for the difference. Off by
    /// default: a collider that nobody is listening to costs nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">The collider's contacts are being dispatched.</exception>
    public bool ReportsContacts
    {
        get => _reportsContacts;
        set
        {
            if (_reportsContacts == value)
            {
                return;
            }

            RequireNotDispatching();
            _reportsContacts = value;

            if (_world is null)
            {
                return;
            }

            if (value)
            {
                _scene!.TrackContacts(this);
            }
            else
            {
                _scene!.UntrackContacts(this);
                _touchingCount = 0;
            }
        }
    }

    /// <summary>The world this collider is registered with, or null while disabled or in no scene.</summary>
    internal CollisionWorld2D? World => _world;

    /// <summary>This collider's identity in <see cref="World"/>; <see cref="ColliderHandle.None"/> while it is in no scene.</summary>
    public ColliderHandle Handle => _handle;

    /// <summary>
    /// What this collider's contact queries may detect, in the terms of
    /// <see cref="World"/>. Rebuilt from the layer names given to <see cref="Detects"/> each
    /// time the collider joins a scene, so a collider carried between scenes filters against the
    /// world it is in; <see cref="CollisionFilter.None"/> while it is in none.
    /// </summary>
    public CollisionFilter Filter { get; private set; }

    /// <summary>
    /// The layer this collider is on, which is what other queries' filters match. Defaults to
    /// <see cref="CollisionWorld2D.DefaultLayerName"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The name is null, empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// The world has no room left to intern the name, or the collider's contacts are being dispatched.
    /// </exception>
    public string Layer
    {
        get => _layer;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            RequireNotDispatching();

            if (_scene?.Collision is { } world)
            {
                // Interned first: a world with no layer slots left refuses the name here, while the
                // collider is still filtering by the one it had.
                CollisionLayer layer = world.Layer(value);

                _layer = value;
                if (_world is not null)
                {
                    world.SetFilter(_handle, layer, Filter);
                }

                return;
            }

            _layer = value;
        }
    }

    /// <summary>Where the shape sits in the world right now.</summary>
    /// <exception cref="InvalidOperationException">The collider is attached to no entity.</exception>
    public Aabb2D Bounds => _shape.Translated(Origin).Bounds;

    /// <summary>
    /// Everything this collider was touching as of the last step, while
    /// <see cref="ReportsContacts"/> is on; empty otherwise. Never abridged, so every entry has had
    /// its <see cref="ContactEntered"/> and will get its <see cref="ContactExited"/>.
    /// </summary>
    public ReadOnlySpan<ColliderContact2D> Touching => _touching.AsSpan(0, _touchingCount);

    private Vector2 Origin =>
        Entity is { } entity
            ? entity.Position + _offset
            : throw new InvalidOperationException("A Collider2D that is attached to no entity has no place in the world.");

    /// <summary>
    /// Replaces what this collider's contact queries detect. Detection does not block movement;
    /// <see cref="KinematicBody2D.BlocksOn"/> owns that independent filter. Layer names are
    /// setup-time text, interned once and never compared during a query.
    /// </summary>
    /// <param name="names">The layer names to hit; an empty list hits nothing.</param>
    /// <exception cref="ArgumentException">A name is null, empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// The world has no room left to intern a name, or the collider's contacts are being dispatched.
    /// </exception>
    public void Detects(params ReadOnlySpan<string> names)
    {
        RequireNotDispatching();

        // Every name is checked, and every one interned, before the list this collider filters by
        // is touched: a bad name half way along used to leave the old list already cleared.
        foreach (string name in names)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(names));
        }

        CollisionFilter filter = CollisionFilter.None;
        if (_scene?.Collision is { } world)
        {
            foreach (string name in names)
            {
                filter = filter.With(world.Layer(name));
            }
        }

        _detects.Clear();
        foreach (string name in names)
        {
            _detects.Add(name);
        }

        if (_world is { } attached)
        {
            Filter = filter;
            attached.SetFilter(_handle, attached.Layer(_layer), filter);
        }
    }

    /// <summary>
    /// Everything this collider is touching right now — within
    /// <see cref="CollisionWorld2D.ContactSkin"/>, matching <see cref="Filter"/>, never itself.
    /// Written into <paramref name="contacts"/>; the return is how many, never more than the span
    /// holds.
    /// </summary>
    /// <exception cref="InvalidOperationException">The collider is in no scene.</exception>
    public int Overlap(Span<Contact2D> contacts) => RequireWorld().OverlapCollider(_handle, contacts);

    /// <summary>
    /// Takes <paramref name="shape"/> as the collider's shape and resyncs whatever world holds it,
    /// so a registered collider is queried as its new shape from the moment the call returns. A
    /// subclass rebuilds its shape through here whenever its authoring state changes; a refusal
    /// leaves the collider exactly as it was.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The shape is a default <see cref="Shape2D"/>, which is no shape at all, or it cannot be
    /// placed at this collider's offset and position.
    /// </exception>
    /// <exception cref="InvalidOperationException">The collider's contacts are being dispatched.</exception>
    protected void SetShape(in Shape2D shape)
    {
        RequireNotDispatching();
        RequireShape(shape);
        RequirePlaceable(shape, _offset);

        _shape = shape;
        _local = shape.Translated(_offset);
        Resync();
    }

    internal override void OnAttachedTo(Entity entity) => entity.TrackMovement(1);

    internal override void OnDetachingFrom(Entity entity)
    {
        RequireNotDispatching();
        entity.TrackMovement(-1);
    }

    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        _scene = Entity!.Scene!;
        if (_enabled)
        {
            Register();
        }
    }
    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        Unregister();
        _scene = null;
        Filter = CollisionFilter.None;
    }

    private void Register()
    {
        Scene scene = _scene!;
        CollisionWorld2D world = scene.Collision;
        CollisionFilter filter = ResolveFilter(world, _detects);
        ColliderHandle handle = world.Add(_local, Entity!.Position, world.Layer(_layer), filter, this);

        _world = world;
        Filter = filter;
        _handle = handle;

        if (_reportsContacts)
        {
            scene.TrackContacts(this);
        }
    }

    private void Unregister()
    {
        if (_world is not { } world || _handle.IsNone)
        {
            return;
        }

        ColliderContact2D[] touching = _touching;
        int touchingCount = _touchingCount;

        _scene?.UntrackContacts(this);
        world.Remove(_handle);

        Filter = CollisionFilter.None;
        _world = null;
        _handle = ColliderHandle.None;
        _touchingCount = 0;
        _wasTouchingCount = 0;

        // Everything it was standing on gets its end, so no contact is left half-reported.
        _dispatching = true;
        try
        {
            for (int index = 0; index < touchingCount; index++)
            {
                ContactExited?.Invoke(touching[index]);
            }
        }
        finally
        {
            _dispatching = false;
        }
    }

    internal override void OnEntityMoved() => _world?.SetPosition(_handle, Entity!.Position);

    internal void SettleContacts()
    {
        if (_world is not { } world)
        {
            return;
        }

        // Widened and re-asked until the answer fits, because a truncated gather is a contact
        // silently never entered and later never exited.
        int count = world.OverlapCollider(_handle, _found);
        while (count == _found.Length)
        {
            Array.Resize(ref _found, _found.Length * 2);
            count = world.OverlapCollider(_handle, _found);
        }

        (_touching, _wasTouching) = (_wasTouching, _touching);
        _wasTouchingCount = _touchingCount;
        _touchingCount = Describe(world, _found.AsSpan(0, count), ref _touching);

        ColliderContact2D[] entered = _touching;
        int enteredCount = _touchingCount;
        ColliderContact2D[] left = _wasTouching;
        int leftCount = _wasTouchingCount;

        _dispatching = true;
        try
        {
            // Exits before enters, so a handler reading Touching sees the settled set either way.
            for (int index = 0; index < leftCount; index++)
            {
                if (!Holds(entered, enteredCount, left[index].Target))
                {
                    ContactExited?.Invoke(left[index]);
                }
            }

            for (int index = 0; index < enteredCount; index++)
            {
                if (!Holds(left, leftCount, entered[index].Target))
                {
                    ContactEntered?.Invoke(entered[index]);
                }
            }
        }
        finally
        {
            _dispatching = false;
        }
    }

    // The shape as the world would have to hold it, checked before anything is committed. None of
    // it needs a world: an offset that could never be placed, or one that cannot be placed where
    // the entity stands, is refused where it is set rather than surfacing much later as a failure
    // to join a scene.
    private void RequirePlaceable(in Shape2D shape, Vector2 offset)
    {
        Shape2D local = shape.Translated(offset);

        if (Entity is { } entity)
        {
            _ = local.Translated(entity.Position);
        }
    }

    private void RequireNotDispatching()
    {
        if (_dispatching)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} cannot change while its contacts are being dispatched.");
        }
    }

    private static void RequireShape(in Shape2D shape, [CallerArgumentExpression(nameof(shape))] string? parameterName = null)
    {
        if (shape.PointCount == 0)
        {
            throw new ArgumentException(
                "A default Shape2D holds no points and is not a shape; build one with Shape2D.Box, Shape2D.Circle, Shape2D.Capsule or Shape2D.Polygon.",
                parameterName);
        }
    }

    private static bool Holds(ColliderContact2D[] contacts, int count, in CollisionTarget target)
    {
        for (int index = 0; index < count; index++)
        {
            if (contacts[index].Target == target)
            {
                return true;
            }
        }

        return false;
    }

    internal static int Describe(
        CollisionWorld2D world,
        ReadOnlySpan<Contact2D> found,
        ref ColliderContact2D[] into)
    {
        if (into.Length < found.Length)
        {
            Array.Resize(ref into, found.Length);
        }

        for (int index = 0; index < found.Length; index++)
        {
            Contact2D contact = found[index];
            object? owner = world.UserDataOf(contact.Target.Collider);
            Collider2D? otherCollider = contact.Target.IsGridCell ? null : owner as Collider2D;
            GridCellContact2D? cell = contact.Target.IsGridCell
                ? new GridCellContact2D(
                    world.GridOf(contact.Target.Collider)!,
                    contact.Target.CellX,
                    contact.Target.CellY,
                    owner)
                : null;

            into[index] = new ColliderContact2D(
                world,
                contact.Target,
                contact.Point,
                contact.Normal,
                otherCollider,
                cell);
        }

        return found.Length;
    }

    // Interning as it goes: a name the world has no room for is refused here.
    internal static CollisionFilter ResolveFilter(CollisionWorld2D world, List<string> names)
    {
        CollisionFilter filter = CollisionFilter.None;
        foreach (string name in names)
        {
            filter = filter.With(world.Layer(name));
        }

        return filter;
    }

    private void Resync()
    {
        if (_world is { } world)
        {
            world.SetShape(_handle, _local);
            world.SetPosition(_handle, Entity!.Position);
        }
    }

    private CollisionWorld2D RequireWorld() =>
        _world ?? throw new InvalidOperationException(
            "A disabled Collider2D, or one that is in no scene, has no world to query; enable it and add its entity to a scene first.");
}
