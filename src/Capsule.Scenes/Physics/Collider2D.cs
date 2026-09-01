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
    private const int NotDispatching = int.MaxValue;

    private readonly List<string> _detects = [];

    private Shape2D _shape;

    // The shape as the world holds it: this shape at this offset. Kept rather than recomputed,
    // because the placement preflight runs on every write to a tracking entity's position and has
    // to compose exactly the translations the commit does — from here, that is one of them.
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

    // How far the enter dispatch has got through _touching. Teardown reads it to tell an entry
    // whose beginning has been announced from one further down the list that has not: the second
    // kind must not be reported as having ended. Outside a dispatch every entry counts as
    // announced, which is what NotDispatching means.
    private int _entersDispatched = NotDispatching;

    // Which registration the contacts being dispatched belong to. Bumped whenever this collider
    // stops reporting for whatever reason, so a handler that tears it down and stands it back up —
    // re-attaching it, or toggling reporting off and on — cannot make an interrupted dispatch look
    // live again and resume delivering a set that described the registration before it.
    private int _reportingEpoch;

    /// <summary>The shape this collider starts out holding, expressed relative to the entity's position.</summary>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, which is no shape at all.</exception>
    protected Collider2D(in Shape2D shape)
    {
        RequireShape(shape);

        _shape = shape;
        _local = shape;
    }

    /// <summary>
    /// Raised for each thing this collider began touching since the previous step. No event is
    /// delivered once the collider has left its scene or <see cref="ReportsContacts"/> has been
    /// turned off — including for the step in which a handler is what caused either, so a handler
    /// may tear its own collider down and be sure nothing further arrives.
    /// </summary>
    public event Action<ColliderContact2D>? ContactEntered;

    /// <summary>
    /// Raised for each thing this collider stopped touching since the previous step, and for
    /// everything it was still touching when it left its scene. Every one of them pairs with a
    /// <see cref="ContactEntered"/> that was delivered: a contact whose beginning was never
    /// announced is never announced as ending. Silent under the same conditions as
    /// <see cref="ContactEntered"/>.
    /// </summary>
    public event Action<ColliderContact2D>? ContactExited;

    /// <summary>The shape, in the collider's own space; <see cref="Offset"/> and the entity's position place it.</summary>
    public Shape2D Shape => _shape;

    /// <summary>Added to the entity's position to place the shape; zero by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The offset is not finite.</exception>
    /// <exception cref="ArgumentException">The shape cannot be placed at this offset.</exception>
    public Vector2 Offset
    {
        get => _offset;
        set
        {
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
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

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
    public bool ReportsContacts
    {
        get => _reportsContacts;
        set
        {
            if (_reportsContacts == value)
            {
                return;
            }

            _reportsContacts = value;
            _reportingEpoch++;

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
    public CollisionWorld2D? World => _world;

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
    /// <exception cref="InvalidOperationException">The world has no room left to intern the name.</exception>
    public string Layer
    {
        get => _layer;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

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

    // Whether this collider is still owed contact events. A handler is free to detach the collider,
    // detach its entity, or turn reporting off; each makes this false.
    private bool IsReporting => _world is not null && _reportsContacts;

    // Whether a dispatch that began in <paramref name="epoch"/> may still deliver. Reporting has to
    // be live and it has to be the same stretch of reporting: standing the collider back up gives
    // it a new one, and the interrupted dispatch belongs to the old.
    private bool IsDispatching(int epoch) => _reportingEpoch == epoch && IsReporting;

    /// <summary>
    /// Replaces what this collider's contact queries detect. Detection does not block movement;
    /// <see cref="KinematicBody2D.BlocksOn"/> owns that independent filter. Layer names are
    /// setup-time text, interned once and never compared during a query.
    /// </summary>
    /// <param name="names">The layer names to hit; an empty list hits nothing.</param>
    /// <exception cref="ArgumentException">A name is null, empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name.</exception>
    public void Detects(params ReadOnlySpan<string> names)
    {
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
    protected void SetShape(in Shape2D shape)
    {
        RequireShape(shape);
        RequirePlaceable(shape, _offset);

        _shape = shape;
        _local = shape.Translated(_offset);
        Resync();
    }

    // Movement is tracked from the moment this collider joins an entity, not from the moment that
    // entity joins a scene: whether a shape can be placed where the entity stands is a fact about
    // the two of them, and a position that would have no place for it is refused wherever it is
    // written. Registering after the check means a refused attach leaves no interest behind.
    internal override void OnAttachingTo(Entity entity) => RequirePlaceableAt(_local, entity.Position);

    internal override void OnAttachedTo(Entity entity) => entity.TrackMovement(1);

    internal override void OnDetachingFrom(Entity entity) => entity.TrackMovement(-1);

    // Everything registration needs, asked of a world this collider has not touched yet: that its
    // shape has a place where the entity stands, and that the world's layer table has room for
    // every name it will have to intern. The names are counted rather than interned — a preflight
    // that reserved table entries would be changing the world it is only supposed to be asking
    // about.
    internal override void OnAddingTo(Scene scene, Entity entity, List<string> layers)
    {
        CollisionWorld2D world = scene.Collision;

        RequirePlaceableAt(_local, entity.Position);

        // Named, not counted: the room for them is judged against everything else being admitted
        // alongside this collider, and interning here would spend the very capacity being tested.
        Want(world, layers, _layer);
        for (int index = 0; index < _detects.Count; index++)
        {
            Want(world, layers, _detects[index]);
        }
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
        CollisionFilter filter = ResolveFilter(world);
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
        int announced = _entersDispatched;
        ColliderContact2D[] previous = _wasTouching;
        int previousCount = _wasTouchingCount;

        _reportingEpoch++;
        _scene?.UntrackContacts(this);
        world.Remove(_handle);

        Filter = CollisionFilter.None;
        _world = null;
        _handle = ColliderHandle.None;
        _touchingCount = 0;
        _wasTouchingCount = 0;

        for (int index = 0; index < touchingCount; index++)
        {
            if (index < announced || Holds(previous, previousCount, touching[index].Target))
            {
                ContactExited?.Invoke(touching[index]);
            }
        }
    }
    // The two things CollisionWorld2D.SetPosition derives and can refuse, checked here while
    // nothing has moved: the shape placed at the new position, and the step taken to reach it. What
    // the world holds for this collider is exactly the shape at this offset, standing at the
    // entity's current position, so this preflight sees the same values the commit will — which is
    // what lets OnEntityMoved be a write that cannot fail.
    internal override void OnEntityMoving(Vector2 position)
    {
        // Whether the shape has a place at all is world-independent, so it is asked wherever the
        // position is written — in a scene or out of one. Only the step between two positions is
        // the world's business, and only a registered collider takes one.
        RequirePlaceableAt(_local, position);

        if (_world is null)
        {
            return;
        }

        if (!IsFinite(position - Entity!.Position))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "A collider on this entity cannot step from where it is to there; the two positions are each finite but the distance between them is not.");
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

        // Local copies, because a handler may detach this collider and clear the fields.
        ColliderContact2D[] entered = _touching;
        int enteredCount = _touchingCount;
        ColliderContact2D[] left = _wasTouching;
        int leftCount = _wasTouchingCount;

        // The registration these contacts describe. Liveness alone would not do: a handler can
        // detach this collider and immediately re-attach it, or toggle reporting off and back on,
        // and leave it live again but registered afresh — with this set describing nobody.
        int epoch = _reportingEpoch;

        try
        {
            // None of the new set has been announced yet.
            _entersDispatched = 0;

            // Exits before enters, so a handler reading Touching sees the settled set either way.
            // Both loops stop the moment a handler ends this registration, whether by taking the
            // collider out of the scene or by turning reporting off: past that point the rest of
            // the set describes something nobody is reporting on any more.
            for (int index = 0; index < leftCount && IsDispatching(epoch); index++)
            {
                if (!Holds(entered, enteredCount, left[index].Target))
                {
                    ContactExited?.Invoke(left[index]);
                }
            }

            for (int index = 0; index < enteredCount && IsDispatching(epoch); index++)
            {
                _entersDispatched = index + 1;

                if (!Holds(left, leftCount, entered[index].Target))
                {
                    ContactEntered?.Invoke(entered[index]);
                }
            }
        }
        finally
        {
            _entersDispatched = NotDispatching;
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

    // The bounds a shape at this offset would cover standing at a position, read off the shape's
    // own bounds rather than by building the placed shapes: this runs on every write to every
    // tracking entity's position, and the point sets are not what is in question. The world holds
    // exactly this shape at this offset, so its bounds are the first translation's result — the
    // same float values, composed the same way, that CollisionWorld2D would check.
    // The commit's own arithmetic, run for its refusals. The world holds this shape at this offset
    // and translates that by the position, so composing the two translations the same way is what
    // makes the preflight exact rather than an approximation of it: bounds off the end of the float
    // range, an extent that rounding collapses, a hull whose points land on each other — whatever
    // the commit would refuse is refused here, which is the whole of the promise that a preflight
    // once passed cannot fail.
    private static void RequirePlaceableAt(in Shape2D local, Vector2 position)
    {
        _ = local.Translated(position);
    }

    // Notes a name the world would have to intern. One it already holds costs nothing, and one
    // already on the list — whether this collider asked for it twice or a sibling asked first —
    // costs nothing more.
    private static void Want(CollisionWorld2D world, List<string> layers, string name)
    {
        if (!world.TryFindLayer(name, out _) && !layers.Contains(name))
        {
            layers.Add(name);
        }
    }

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

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

    private CollisionFilter ResolveFilter(CollisionWorld2D world)
    {
        CollisionFilter filter = CollisionFilter.None;
        foreach (string name in _detects)
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
