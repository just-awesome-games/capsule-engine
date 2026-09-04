using System.Numerics;
using System.Runtime.CompilerServices;
using Capsule.Collision;

namespace Capsule.Scenes.Physics;

/// <summary>
/// Gives its entity a shape in the scene's <see cref="Scene.Collision"/> world. It registers when
/// its entity joins a scene and unregisters when it leaves, and follows the entity's
/// <see cref="Scenes.Entity.Position"/> — direct writes and teleports included — so a query never
/// sees a stale one. The shape, and where it sits relative to the position, belong to the subclass.
/// <para>
/// While this collider is dispatching its own contact handlers, what they are being told about is
/// fixed: <see cref="Enabled"/>, <see cref="Offset"/>, <see cref="Layer"/>,
/// <see cref="ReportsContacts"/>, <see cref="Detects"/> and a subclass's shape are all refused for
/// the whole dispatch, nested ones included. A handler may detach the collider, which takes it out
/// of the world, gives it the exits it owes and ends its enters for the step; re-attaching before
/// the dispatch ends is refused.
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

    // How many entries at the head of _touching have been announced through ContactEntered, and so
    // are owed a ContactExited. SettleContacts orders carried-over contacts first, which is what
    // keeps the announced set a prefix while the enter loop walks the new ones.
    private int _announcedCount;

    // True while this collider's own enter and exit handlers are running. Each dispatch scope
    // restores the value it found rather than clearing: a handler that detaches this collider
    // dispatches its exits from inside the outer dispatch, which stays armed across that.
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
    /// Raised for each thing this collider began touching since the previous step, in the order an
    /// overlap query would return them. A handler may not reconfigure the collider it is raised
    /// for; it may detach it, which ends the dispatch, leaving the contacts the loop had not
    /// reached unannounced.
    /// </summary>
    public event Action<ColliderContact2D>? ContactEntered;

    /// <summary>
    /// Raised for each thing this collider stopped touching since the previous step, and for
    /// everything it had announced entering when it left its scene, was disabled, stopped
    /// reporting contacts, or was detached from its entity. Exits come in <see cref="Touching"/>
    /// order, and handlers are bound by the same rule as <see cref="ContactEntered"/>. The pairing
    /// is exact for handlers that return; one that throws leaves the exits owed behind it unraised.
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
    /// <exception cref="InvalidOperationException">The collider's contacts are being dispatched.</exception>
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
    /// Whether contacts are settled each step and announced through <see cref="ContactEntered"/>
    /// and <see cref="ContactExited"/>. Off by default. Turning it off raises
    /// <see cref="ContactExited"/> for every announced contact before returning; turning it on
    /// announces afresh on the next step.
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
                EndAnnouncedContacts();
            }
        }
    }

    /// <summary>The world this collider is registered with, or null while disabled or in no scene.</summary>
    public CollisionWorld2D? World => _world;

    /// <summary>This collider's identity in <see cref="World"/>; <see cref="ColliderHandle.None"/> while it is in no scene.</summary>
    public ColliderHandle Handle => _handle;

    /// <summary>
    /// What this collider's contact queries may detect, in <see cref="World"/>'s terms. Rebuilt
    /// from the names given to <see cref="Detects"/> each time the collider joins a scene, so one
    /// carried between scenes filters against the world it is in; None while it is in none.
    /// </summary>
    public CollisionFilter Filter { get; private set; }

    /// <summary>
    /// The layer this collider is on, which is what other queries' filters match. Defaults to
    /// <see cref="CollisionWorld2D.DefaultLayerName"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The name is null, empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The world cannot intern the name, or contacts are being dispatched.</exception>
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
    public Aabb2D Bounds =>
        Entity is { } entity
            ? _local.Translated(entity.Position).Bounds
            : throw new InvalidOperationException("A Collider2D that is attached to no entity has no place in the world.");

    /// <summary>
    /// Everything this collider was touching as of the last step, while
    /// <see cref="ReportsContacts"/> is on; empty otherwise. Never abridged, and ordered
    /// carried-over contacts first, then newly entered ones, each group in overlap-query order.
    /// Mid-dispatch it can hold contacts whose <see cref="ContactEntered"/> has not been raised:
    /// the enter/exit pairing is a promise about the events, not about this span.
    /// </summary>
    public ReadOnlySpan<ColliderContact2D> Touching => _touching.AsSpan(0, _touchingCount);

    /// <summary>
    /// Replaces what this collider's contact queries detect. Detection does not block movement;
    /// <see cref="KinematicBody2D.BlocksOn"/> owns that independent filter.
    /// </summary>
    /// <param name="names">The layer names to hit; an empty list hits nothing.</param>
    /// <exception cref="ArgumentException">A name is null, empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The world cannot intern a name, or contacts are being dispatched.</exception>
    public void Detects(params ReadOnlySpan<string> names)
    {
        RequireNotDispatching();

        // Every name is checked, and every one interned, before the list this collider filters by
        // is touched: a bad name half way along must leave the old list intact.
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
    /// <see cref="CollisionWorld2D.ContactSkin"/>, matching <see cref="Filter"/>, never itself —
    /// written into <paramref name="contacts"/>, returning how many, never more than it holds.
    /// </summary>
    /// <exception cref="InvalidOperationException">The collider is in no scene.</exception>
    public int Overlap(Span<Contact2D> contacts) => RequireWorld().OverlapCollider(_handle, contacts);

    /// <summary>
    /// Sweeps this collider's own shape from where it stands along <paramref name="translation"/>
    /// and reports the first thing it meets, under <see cref="Filter"/> and never itself. Nothing
    /// moves. A surface already being touched is reported at fraction 0 when the sweep drives into
    /// it, and passed by when the sweep runs along it or away from it.
    /// </summary>
    /// <param name="translation">How far and which way to sweep, in world units.</param>
    /// <param name="hit">The nearest thing met, when there is one.</param>
    /// <returns>Whether the sweep met anything.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The translation, or the box the sweep covers, is not finite.</exception>
    /// <exception cref="InvalidOperationException">The collider is disabled or in no scene.</exception>
    public bool Cast(Vector2 translation, out ShapeCastHit2D hit) => Cast(translation, Filter, out hit);

    /// <summary>
    /// Sweeps this collider's own shape against <paramref name="filter"/> instead of
    /// <see cref="Filter"/>, for this call alone; <see cref="Detects"/> is untouched. Bound in
    /// every other way by <see cref="Cast(Vector2, out ShapeCastHit2D)"/>.
    /// </summary>
    /// <param name="translation">How far and which way to sweep, in world units.</param>
    /// <param name="filter">What the sweep may hit; <see cref="CollisionFilter.None"/> hits nothing.</param>
    /// <param name="hit">The nearest thing met, when there is one.</param>
    /// <returns>Whether the sweep met anything.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The translation, or the box the sweep covers, is not finite.</exception>
    /// <exception cref="ArgumentException">The filter was built from another collision world's layers.</exception>
    /// <exception cref="InvalidOperationException">The collider is disabled or in no scene.</exception>
    public bool Cast(Vector2 translation, CollisionFilter filter, out ShapeCastHit2D hit) =>
        RequireWorld().ShapeCast(_local, Entity!.Position, translation, filter, out hit, _handle);

    /// <summary>
    /// Takes <paramref name="shape"/> as the collider's shape and resyncs whatever world holds it,
    /// so a registered collider is queried as its new shape from the moment the call returns. A
    /// refusal leaves the collider exactly as it was.
    /// </summary>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, or is unplaceable here.</exception>
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

    // Re-attaching mid-dispatch would put the collider back in the world, where whether it settles
    // again this step depends on where the next reporting collider sits in the scene's list.
    internal override void OnAttachedTo(Entity entity)
    {
        if (_dispatching)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} cannot be attached to an entity while its own contacts are being dispatched.");
        }

        entity.TrackMovement(1);
    }

    // No dispatch guard: a handler detaching its own collider is legal, and by the time this runs
    // Entity.Remove has already taken the collider out of the world through LeaveScene.
    internal override void OnDetachingFrom(Entity entity) => entity.TrackMovement(-1);

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

        _scene?.UntrackContacts(this);
        world.Remove(_handle);

        Filter = CollisionFilter.None;
        _world = null;
        _handle = ColliderHandle.None;

        EndAnnouncedContacts();
    }

    // Ends every announced contact and only those, leaving this collider holding none. The counts
    // are cleared before the first handler runs: a handler that detaches the collider from in here
    // finds nothing left owing rather than exiting the same contacts twice.
    private void EndAnnouncedContacts()
    {
        ColliderContact2D[] announced = _touching;
        int announcedCount = _announcedCount;

        _touchingCount = 0;
        _wasTouchingCount = 0;
        _announcedCount = 0;

        bool wasDispatching = _dispatching;
        _dispatching = true;
        try
        {
            for (int index = 0; index < announcedCount; index++)
            {
                ContactExited?.Invoke(announced[index]);
            }
        }
        finally
        {
            _dispatching = wasDispatching;
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

        // Carried-over contacts were announced last step and are owed an exit from here on;
        // everything after them owes nothing until the enter loop announces it, one at a time.
        _touchingCount = DescribeCarriedFirst(
            world,
            _found.AsSpan(0, count),
            ref _touching,
            _wasTouching,
            _wasTouchingCount,
            out int carried);
        _announcedCount = carried;

        ColliderContact2D[] entered = _touching;
        int enteredCount = _touchingCount;
        ColliderContact2D[] left = _wasTouching;
        int leftCount = _wasTouchingCount;

        bool wasDispatching = _dispatching;
        _dispatching = true;
        try
        {
            // Exits before enters, so a handler reading Touching sees the settled set either way.
            // This loop runs to the end even if a handler detaches the collider: its contacts and
            // the unregister sweep's are disjoint, so nothing is exited twice or dropped.
            for (int index = 0; index < leftCount; index++)
            {
                if (!Holds(entered, enteredCount, left[index].Target))
                {
                    ContactExited?.Invoke(left[index]);
                }
            }

            for (int index = carried; index < enteredCount; index++)
            {
                // Re-read after every handler: one that detached this collider took it out of the
                // world and swept the exits it owed, so it has nothing left to enter.
                if (_world is null || _announcedCount != index)
                {
                    break;
                }

                // Announced before the handler runs, so a handler that detaches from inside it
                // still counts this contact among the ones owed an exit.
                _announcedCount = index + 1;
                ContactEntered?.Invoke(entered[index]);
            }
        }
        finally
        {
            // A handler that threw leaves the tail of the settled set unannounced. Forgetting it
            // here is what keeps the pairing exact: carried into the next step it would count as
            // announced, and could be given an exit for a contact that never entered.
            if (_touchingCount > _announcedCount)
            {
                _touchingCount = _announcedCount;
            }

            _dispatching = wasDispatching;
        }
    }

    // Two passes over the gather rather than a scratch buffer, stable within each group.
    private static int DescribeCarriedFirst(
        CollisionWorld2D world,
        ReadOnlySpan<Contact2D> found,
        ref ColliderContact2D[] into,
        ColliderContact2D[] previous,
        int previousCount,
        out int carried)
    {
        if (into.Length < found.Length)
        {
            Array.Resize(ref into, found.Length);
        }

        int written = 0;
        for (int index = 0; index < found.Length; index++)
        {
            if (Holds(previous, previousCount, found[index].Target))
            {
                into[written++] = Describe(world, found[index]);
            }
        }

        carried = written;

        for (int index = 0; index < found.Length; index++)
        {
            if (!Holds(previous, previousCount, found[index].Target))
            {
                into[written++] = Describe(world, found[index]);
            }
        }

        return written;
    }

    // The shape as the world would have to hold it, checked before anything is committed, so an
    // unplaceable offset is refused where it is set rather than as a failure to join a scene.
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
            into[index] = Describe(world, found[index]);
        }

        return found.Length;
    }

    private static ColliderContact2D Describe(CollisionWorld2D world, in Contact2D contact)
    {
        object? owner = world.UserDataOf(contact.Target.Collider);
        Collider2D? otherCollider = contact.Target.IsGridCell ? null : owner as Collider2D;
        GridCellContact2D? cell = contact.Target.IsGridCell
            ? new GridCellContact2D(
                world.GridOf(contact.Target.Collider)!,
                contact.Target.CellX,
                contact.Target.CellY,
                owner)
            : null;

        return new ColliderContact2D(
            world,
            contact.Target,
            contact.Point,
            contact.Normal,
            otherCollider,
            cell);
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
