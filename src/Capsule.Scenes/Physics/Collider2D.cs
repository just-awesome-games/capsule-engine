using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tiles;

namespace Capsule.Physics;

/// <summary>
/// Gives its entity a shape in the scene's <see cref="Scene.Collision"/> world. The collider
/// registers when its entity joins a scene, unregisters when it leaves, and follows the entity's
/// <see cref="Scenes.Entity.WorldPosition"/> every step.
/// </summary>
/// <remarks>
/// Position is the only transform it follows. Rotation and scale anywhere in the entity's ancestry
/// are refused while a collider is present. The subclass defines the shape, and
/// <see cref="Offset"/> places it relative to the position. Every query throws while the collider
/// is disabled or in no scene. A filter built from another collision world's layers throws too.
/// <para>
/// A contact handler cannot reconfigure the collider it was raised for. <see cref="Enabled"/>,
/// <see cref="Offset"/>, <see cref="Layer"/>, <see cref="ReportsContacts"/>,
/// <see cref="SetFilter"/> and a subclass's shape all throw for the length of the dispatch. A
/// handler may detach the collider. Detaching removes it from the world, raises the exits it owes,
/// and cancels the remaining enters for this step.
/// </para>
/// </remarks>
public abstract class Collider2D : Component
{
    private readonly List<string> _detects = [];

    private Shape2D _shape;

    // The shape translated by the offset. This is the form the world holds.
    private Shape2D _local;
    private Vector2 _offset;
    private string _layer = CollisionWorld2D.DefaultLayerName;
    private bool _enabled = true;
    private bool _reportsContacts;
    private bool _oneWay;
    private bool _solidSides;

    private CollisionWorld2D? _world;
    private Scene? _scene;
    private ColliderHandle _handle;

    private Contact2D[] _found = new Contact2D[16];
    private ColliderContact2D[] _touching = new ColliderContact2D[16];
    private ColliderContact2D[] _wasTouching = new ColliderContact2D[16];
    private int _touchingCount;
    private int _wasTouchingCount;

    // How many entries at the head of _touching have been raised through ContactEntered and are now
    // owed a ContactExited. Carried-over contacts settle first, so the announced entries stay at the
    // head while the enter loop walks the new ones.
    private int _announcedCount;

    // True while this collider's own enter and exit handlers are running.
    private bool _dispatching;

    // The interned index of Layer in the world this collider is registered with.
    private int _layerIndex;

    // The bodies whose last move stopped on this collider, grown once and never shrunk. A slot empties
    // to null while this collider is carrying, and is compacted once the carry is done.
    private KinematicBody2D?[] _riders = [];
    private int _riderCount;
    private bool _ridersEmptied;

    // The colliders near this one's last move, allocated on its first shove. Each collider owns its
    // own, and _pushing keeps a nested move of this one from reusing it.
    private ColliderHandle[]? _near;
    private bool _pushing;

    /// <summary>The collider's starting shape, expressed relative to the entity's position.</summary>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/> with no points.</exception>
    protected Collider2D(in Shape2D shape)
    {
        RequireShape(shape);

        _shape = shape;
        _local = shape;
    }

    /// <summary>
    /// Raised for each thing this collider began touching since the previous step, in overlap-query
    /// order.
    /// </summary>
    /// <remarks>
    /// A handler may not reconfigure the collider. A handler that detaches it ends the dispatch,
    /// and the contacts the loop had not reached go unannounced.
    /// </remarks>
    public event Action<ColliderContact2D>? ContactEntered;

    /// <summary>
    /// Raised for each thing this collider stopped touching since the previous step, and for
    /// everything it had announced entering when it left its scene, was disabled, stopped reporting
    /// contacts, or was detached from its entity.
    /// </summary>
    /// <remarks>
    /// Exits come in <see cref="Touching"/> order. Each enter is paired with one exit, provided the
    /// handlers return normally.
    /// </remarks>
    public event Action<ColliderContact2D>? ContactExited;

    /// <summary>The shape in the collider's own space. <see cref="Offset"/> and the entity's position place it.</summary>
    public Shape2D Shape => _shape;

    // The shape at its offset. The world translates this by the entity's position.
    internal Shape2D Local => _local;

    // The shape at its current place in the world.
    private protected Shape2D WorldShape =>
        Entity is { } entity
            ? _local.Translated(entity.WorldPosition)
            : throw new InvalidOperationException("This Collider2D is attached to no entity. Attach it before asking where its shape sits.");

    // Null to use the channel's colour. A disabled collider draws in that colour at half alpha.
    private protected ColorRgba? DebugColor => _enabled ? null : DebugDraw.ColorOf(DebugDraw.Colliders) with { A = 128 };

    private protected Vector2 Motion => Entity!.WorldPosition - Entity.PreviousWorld.Position;

    private protected static Rect Edges(in Aabb2D box) => new(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y);

    /// <summary>Added to the entity's position to place the shape. Zero by default.</summary>
    /// <exception cref="ArgumentException">The shape cannot be placed at this offset.</exception>
    public Vector2 Offset
    {
        get => _offset;
        set
        {
            RequireNotDispatching();
            Guard.Finite(value, nameof(value));
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
    /// and <see cref="ContactExited"/>. Off by default.
    /// </summary>
    /// <remarks>
    /// Turning it off raises <see cref="ContactExited"/> for every announced contact before
    /// returning. Turning it on announces afresh on the next step.
    /// </remarks>
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

    /// <summary>
    /// Whether this collider lets a mover pass from below and blocks it from above, and from the sides too
    /// while <see cref="SolidSides"/> is set.
    /// </summary>
    /// <remarks>
    /// A one-way collider stops a sweep only on a surface that faces up, or with
    /// <see cref="SolidSides"/> on any surface that does not face down, and only when the mover started
    /// clear of it. A mover rising through it or already inside it passes, and
    /// <see cref="KinematicBody2D.DropThrough"/> passes it from above. It shoves and carries a body only
    /// through the surfaces that stop a sweep. Contacts and overlaps are reported as usual.
    /// </remarks>
    public bool OneWay
    {
        get => _oneWay;
        set
        {
            RequireNotDispatching();
            _oneWay = value;
            if (_world is { } world)
            {
                world.SetOneWay(_handle, value);
            }
        }
    }

    /// <summary>
    /// Whether this one-way collider also blocks a mover from the sides, passing it only from below.
    /// </summary>
    public bool SolidSides
    {
        get => _solidSides;
        set
        {
            RequireNotDispatching();
            _solidSides = value;
            if (_world is { } world)
            {
                world.SetSolidSides(_handle, value);
            }
        }
    }

    /// <summary>
    /// The collision world this collider is registered with, or null while it is disabled or in no
    /// scene. This is the world the scene exposes as <see cref="Scene.Collision"/>.
    /// </summary>
    public CollisionWorld2D? World => _world;

    /// <summary>
    /// This collider's identity in its scene's <see cref="Scene.Collision"/> world. Reads
    /// <see cref="ColliderHandle.None"/> while the collider is disabled or in no scene.
    /// </summary>
    public ColliderHandle Handle => _handle;

    /// <summary>
    /// The layers this collider's contact queries may detect, resolved against its scene's
    /// <see cref="Scene.Collision"/> world. The engine rebuilds it from the names given to
    /// <see cref="SetFilter"/> each time the collider registers with a world.
    /// </summary>
    /// <remarks>
    /// Reads <see cref="CollisionFilter.None"/> while the collider is disabled or in no scene.
    /// </remarks>
    public CollisionFilter Filter { get; private set; }

    /// <summary>The layer this collider is on.</summary>
    /// <remarks>
    /// Other queries' filters match against it. Defaults to
    /// <see cref="CollisionWorld2D.DefaultLayerName"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The world has no room left to intern the name.</exception>
    public string Layer
    {
        get => _layer;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            RequireNotDispatching();

            if (_scene?.Collision is { } world)
            {
                // Intern first. A world with no layer slots left throws here, while the collider
                // still holds its old layer.
                CollisionLayer layer = world.Layer(value);

                _layer = value;
                _layerIndex = layer.Index;
                if (_world is not null)
                {
                    world.SetLayer(_handle, layer);
                }

                return;
            }

            _layer = value;
        }
    }

    // The body that sweeps this collider while both are in a scene, or null.
    internal KinematicBody2D? Body { get; set; }

    /// <summary>Where the shape sits in the world right now.</summary>
    /// <exception cref="InvalidOperationException">The collider is attached to no entity.</exception>
    public Aabb2D Bounds => WorldShape.Bounds;

    /// <summary>
    /// Everything this collider was touching as of the last step while
    /// <see cref="ReportsContacts"/> is on, and empty otherwise. Carried-over contacts come first,
    /// then newly entered ones, each group in overlap-query order.
    /// </summary>
    /// <remarks>
    /// During a dispatch the span can already hold contacts whose <see cref="ContactEntered"/> has
    /// not been raised. The enter and exit pairing is a guarantee about the events, not about this
    /// span.
    /// </remarks>
    public ReadOnlySpan<ColliderContact2D> Touching => _touching.AsSpan(0, _touchingCount);

    /// <summary>Replaces the layers this collider's contact queries detect.</summary>
    /// <remarks>
    /// Detection does not block movement. <see cref="KinematicBody2D.BlocksOn"/> holds a separate
    /// filter for blocking.
    /// </remarks>
    /// <param name="names">The layer names to hit. An empty list hits nothing.</param>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name.</exception>
    public void SetFilter(params ReadOnlySpan<string> names)
    {
        RequireNotDispatching();

        // Resolve before touching the stored list. A bad name part way along leaves the old list
        // intact.
        CollisionFilter filter = ResolveFilter(_scene?.Collision, names);

        _detects.Clear();
        foreach (string name in names)
        {
            _detects.Add(name);
        }

        if (_world is not null)
        {
            Filter = filter;
        }
    }

    /// <summary>
    /// Writes into <paramref name="contacts"/> everything within
    /// <see cref="CollisionTolerance.ContactSkin"/> of this collider that matches
    /// <see cref="Filter"/>. The collider never reports itself.
    /// </summary>
    /// <returns>
    /// The total overlap count. A span shorter than that count is filled to capacity and the
    /// remaining overlaps are counted but not written.
    /// </returns>
    public int OverlapAll(Span<Contact2D> contacts) => RequireWorld().OverlapColliderAll(_handle, Filter, contacts);

    /// <summary>
    /// Reports whether this collider is within <see cref="CollisionTolerance.ContactSkin"/> of
    /// <paramref name="other"/>, ignoring both colliders' <see cref="Filter"/>. A collider never
    /// touches itself.
    /// </summary>
    /// <remarks>
    /// The test returns false when <paramref name="other"/> is disabled or in no scene.
    /// </remarks>
    /// <param name="other">The collider to test against.</param>
    /// <returns>Whether the two are touching.</returns>
    /// <exception cref="ArgumentException"><paramref name="other"/> is registered with another collision world.</exception>
    public bool Overlaps(Collider2D other) => Overlaps(other, out _);

    /// <summary>
    /// Writes where this collider touches <paramref name="other"/> to <paramref name="contact"/>.
    /// </summary>
    /// <remarks>
    /// All other rules of <see cref="Overlaps(Collider2D)"/> apply. The contact describes
    /// <paramref name="other"/>'s surface, matching what an overlap query over the same pair
    /// reports.
    /// </remarks>
    public bool Overlaps(Collider2D other, out Contact2D contact)
    {
        ArgumentNullException.ThrowIfNull(other);

        CollisionWorld2D world = RequireWorld();
        contact = default;

        // A collider outside a world touches nothing, as in every other query.
        if (ReferenceEquals(other, this) || other._world is not { } theirs)
        {
            return false;
        }

        if (!ReferenceEquals(theirs, world))
        {
            throw new ArgumentException(
                "The other collider belongs to a different collision world. Test colliders that share a scene.",
                nameof(other));
        }

        return world.OverlapPair(_handle, other._handle, CollisionFilter.Everything, out contact);
    }

    /// <summary>
    /// Casts a ray from the centre of this collider's <see cref="Bounds"/>, using
    /// <see cref="Filter"/> and never hitting this collider. Reports the nearest hit and breaks ties
    /// the way <see cref="CollisionWorld2D.Raycast"/> does.
    /// </summary>
    /// <param name="direction">Which way to look. Any non-zero length works.</param>
    /// <param name="distance">How far to look, in world units.</param>
    /// <param name="hit">The nearest hit, when there is one.</param>
    /// <returns>Whether the ray hit anything.</returns>
    public bool Raycast(Vector2 direction, float distance, out RayHit2D hit) =>
        Raycast(direction, distance, Filter, out hit);

    /// <summary>
    /// Casts a ray against <paramref name="filter"/> instead of <see cref="Filter"/>, for this call
    /// only.
    /// </summary>
    /// <remarks>
    /// <see cref="CollisionFilter.None"/> hits nothing, and this does not change
    /// <see cref="SetFilter"/>. All other rules of
    /// <see cref="Raycast(Vector2, float, out RayHit2D)"/> apply.
    /// </remarks>
    public bool Raycast(Vector2 direction, float distance, CollisionFilter filter, out RayHit2D hit)
    {
        // The world allows a zero distance, but a zero-length ray from a collider that ignores itself
        // always returns false. Reject it as a caller mistake.
        if (!float.IsFinite(distance) || distance <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "A collider's ray must reach a finite, positive distance.");
        }

        CollisionWorld2D world = RequireWorld();

        return world.Raycast(Bounds.Center, direction, distance, filter, out hit, _handle);
    }

    /// <summary>
    /// Sweeps this collider's shape from its current place along <paramref name="translation"/> and
    /// reports the first thing it hits, using <see cref="Filter"/> and never itself.
    /// </summary>
    /// <remarks>
    /// Nothing moves. A surface the collider already touches reports at fraction 0 when the sweep
    /// drives into it, and is ignored when the sweep runs along it or away from it.
    /// </remarks>
    /// <param name="translation">How far and which way to sweep, in world units.</param>
    /// <param name="hit">The nearest hit, when there is one.</param>
    /// <returns>Whether the sweep hit anything.</returns>
    public bool Cast(Vector2 translation, out ShapeCastHit2D hit) => Cast(translation, Filter, out hit);

    /// <summary>
    /// Sweeps this collider's shape against <paramref name="filter"/> instead of
    /// <see cref="Filter"/>, for this call only.
    /// </summary>
    /// <remarks>
    /// <see cref="CollisionFilter.None"/> hits nothing, and this does not change
    /// <see cref="SetFilter"/>. All other rules of <see cref="Cast(Vector2, out ShapeCastHit2D)"/>
    /// apply.
    /// </remarks>
    public bool Cast(Vector2 translation, CollisionFilter filter, out ShapeCastHit2D hit) =>
        RequireWorld().ShapeCast(_local, Entity!.WorldPosition, translation, filter, out hit, _handle);

    /// <summary>
    /// Replaces the collider's shape with <paramref name="shape"/>. Queries see the new shape as
    /// soon as this returns.
    /// </summary>
    /// <remarks>A throw leaves the collider unchanged.</remarks>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, or cannot be placed at the current offset.</exception>
    protected void SetShape(in Shape2D shape)
    {
        RequireNotDispatching();
        RequireShape(shape);
        RequirePlaceable(shape, _offset);

        _shape = shape;
        _local = shape.Translated(_offset);
        Resync();
    }

    internal sealed override TransformSupport Supports => TransformSupport.Position;

    // Re-attaching during a dispatch would put the collider back in the world, and whether it
    // settled again this step would depend on its position in the scene's list.
    internal override void OnAttachedTo(Entity entity)
    {
        if (_dispatching)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} cannot be attached to an entity while its own contacts are being dispatched.");
        }

        entity.TrackMovement(1);
    }

    // No dispatch guard here, because a handler may detach its own collider. By the time this runs,
    // Entity.Remove has already taken the collider out of the world through LeaveScene.
    internal override void OnDetachingFrom(Entity entity) => entity.TrackMovement(-1);

    // The rows every collider reports. A subclass adds its shape rows after calling this.
    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Offset", _offset);
        panel.Field("Layer", _layer);
        panel.Field("Touching", Touching.Length);
        panel.Toggle("Enabled", _enabled, on => Enabled = on);
        panel.Toggle("ReportsContacts", ReportsContacts, on => ReportsContacts = on);
    }

    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        _scene = Entity!.Scene;
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
        CollisionFilter filter = ResolveFilter(world, CollectionsMarshal.AsSpan(_detects));
        CollisionLayer layer = world.Layer(_layer);
        ColliderHandle handle = world.Add(_local, Entity!.WorldPosition, layer, this);
        world.SetOneWay(handle, _oneWay);
        world.SetSolidSides(handle, _solidSides);

        _layerIndex = layer.Index;

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
        ReleaseRiders();
        Body?.Unride();

        Filter = CollisionFilter.None;
        _world = null;
        _handle = ColliderHandle.None;

        EndAnnouncedContacts();
    }

    // Raises an exit for each announced contact and leaves the collider holding none. The counts are
    // cleared before the first handler runs, and a handler that detaches from in here finds nothing
    // owed and cannot exit the same contact twice.
    private void EndAnnouncedContacts()
    {
        ColliderContact2D[] announced = _touching;
        int announcedCount = _announcedCount;

        _touchingCount = 0;
        _wasTouchingCount = 0;
        _announcedCount = 0;

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
            _dispatching = false;
        }
    }

    // A collider no body is moved by only follows its entity. One that is carries its riders by the
    // same translation and then shoves the bodies it moved into.
    internal override void OnEntityMoved()
    {
        if (_world is not { } world)
        {
            return;
        }

        Vector2 position = Entity!.WorldPosition;
        bool moves = _riderCount > 0 || (_scene!.MovedByLayers & (1UL << _layerIndex)) != 0;

        // A move made from inside this collider's own carry, by a Crushed handler, only follows the
        // entity. Carrying again would reuse the buffer the outer carry is reading.
        if (!moves || _pushing)
        {
            world.SetPosition(_handle, position);
            return;
        }

        Vector2 from = world.PositionOf(_handle);
        Vector2 motion = position - from;
        world.SetPosition(_handle, position);
        if (motion == Vector2.Zero)
        {
            return;
        }

        _pushing = true;
        try
        {
            CarryRiders(motion);

            // A handler raised during the carry may have disabled or detached this collider.
            if (_world is not null)
            {
                ShoveBodies(world, from, motion);
            }
        }
        finally
        {
            _pushing = false;
            CompactRiders();
        }
    }

    internal void AddRider(KinematicBody2D body)
    {
        if (_riderCount == _riders.Length)
        {
            Array.Resize(ref _riders, Math.Max(4, _riders.Length * 2));
        }

        _riders[_riderCount++] = body;
    }

    internal void RemoveRider(KinematicBody2D body)
    {
        for (int index = 0; index < _riderCount; index++)
        {
            if (!ReferenceEquals(_riders[index], body))
            {
                continue;
            }

            if (_pushing)
            {
                _riders[index] = null;
                _ridersEmptied = true;
                return;
            }

            _riderCount--;
            _riders[index] = _riders[_riderCount];
            _riders[_riderCount] = null;
            return;
        }
    }

    // A carried rider's colliders move in turn, which carries whatever rides them. The count is read
    // once, so a body that lands here mid-carry waits for the next move.
    private void CarryRiders(Vector2 motion)
    {
        int count = _riderCount;
        for (int index = 0; index < count && _world is not null; index++)
        {
            if (_riders[index] is { } rider
                && rider.IsMovedBy(_layerIndex)
                && !MovesWith(rider.Entity))
            {
                rider.Carry(motion);
            }
        }
    }

    // Shoves each body this collider's move drove into by the travel left after meeting it, and checks
    // each rider a carry could not clear. The move is swept from where the collider was, so a body
    // thinner than the move is still met.
    private void ShoveBodies(CollisionWorld2D world, Vector2 from, Vector2 motion)
    {
        if ((_scene!.MovedByLayers & (1UL << _layerIndex)) == 0)
        {
            return;
        }

        Aabb2D reach = _local.Translated(from).Bounds.Union(world.WorldShapeOf(_handle).Bounds)
            .Expanded(CollisionTolerance.ContactSkin);

        _near ??= new ColliderHandle[8];
        int count = world.CollidersNear(reach, _handle, _near);
        if (count > _near.Length)
        {
            Array.Resize(ref _near, count);
            count = world.CollidersNear(reach, _handle, _near);
        }

        for (int index = 0; index < count && _world is not null; index++)
        {
            ColliderHandle near = _near[index];
            if (!world.Contains(near)
                || world.UserDataOf(near) is not Collider2D { Body: { } body }
                || !body.IsMovedBy(_layerIndex)
                || MovesWith(body.Entity)
                || !world.SweepPair(
                    _handle,
                    from,
                    motion,
                    near,
                    out float fraction,
                    out Vector2 normal,
                    out Vector2 point))
            {
                continue;
            }

            // The sweep reports the body's normal. The pusher's surface faces the other way.
            body.Shove(this, motion * (1f - fraction), -normal, point);
        }
    }

    // Whether an entity is this collider's own or one of its ancestors, which move with it already.
    private bool MovesWith(Entity? other)
    {
        for (Entity? entity = Entity; entity is not null; entity = entity.Parent)
        {
            if (ReferenceEquals(entity, other))
            {
                return true;
            }
        }

        return false;
    }

    private void CompactRiders()
    {
        if (!_ridersEmptied)
        {
            return;
        }

        _ridersEmptied = false;
        int kept = 0;
        for (int index = 0; index < _riderCount; index++)
        {
            if (_riders[index] is { } rider)
            {
                _riders[kept++] = rider;
            }
        }

        Array.Clear(_riders, kept, _riderCount - kept);
        _riderCount = kept;
    }

    // Lets go of every rider as this collider leaves the world.
    private void ReleaseRiders()
    {
        for (int index = 0; index < _riderCount; index++)
        {
            if (_riders[index] is { } rider)
            {
                _riders[index] = null;
                rider.ForgetFloor(this);
            }
        }

        _riderCount = 0;
        _ridersEmptied = false;
    }

    internal void SettleContacts()
    {
        if (_world is not { } world)
        {
            return;
        }

        // Grow the buffer to the reported count and query again, because a truncated gather would
        // silently drop a contact's enter and its later exit.
        int count = world.OverlapColliderAll(_handle, Filter, _found);
        if (count > _found.Length)
        {
            Array.Resize(ref _found, count);
            count = world.OverlapColliderAll(_handle, Filter, _found);
        }

        (_touching, _wasTouching) = (_wasTouching, _touching);
        _wasTouchingCount = _touchingCount;

        // Carried-over contacts were announced last step and are owed an exit from here on. The
        // contacts after them owe nothing until the enter loop announces each one.
        _touchingCount = SettleCarriedFirst(world, _found.AsSpan(0, count), out int carried);
        _announcedCount = carried;

        ColliderContact2D[] entered = _touching;
        int enteredCount = _touchingCount;
        ColliderContact2D[] left = _wasTouching;
        int leftCount = _wasTouchingCount;

        _dispatching = true;
        try
        {
            // Exits run before enters, and a handler reading Touching sees the settled set. This loop
            // finishes even if a handler detaches the collider. These contacts and the unregister
            // sweep's are disjoint, so nothing exits twice and nothing is dropped.
            for (int index = 0; index < leftCount; index++)
            {
                if (!Holds(entered, enteredCount, left[index].Target))
                {
                    ContactExited?.Invoke(left[index]);
                }
            }

            for (int index = carried; index < enteredCount; index++)
            {
                // A handler detached this collider, which removed it from the world and raised the
                // exits it owed. Nothing is left to enter.
                if (_world is null)
                {
                    break;
                }

                // Count the contact before the handler runs, and a handler that detaches from inside
                // it still sees this contact as owed an exit.
                _announcedCount = index + 1;
                ContactEntered?.Invoke(entered[index]);
            }
        }
        finally
        {
            _dispatching = false;
        }
    }

    // Writes the gather into _touching, putting contacts carried over from the previous step first and
    // keeping query order within each group, so the announced contacts stay at the head.
    private int SettleCarriedFirst(CollisionWorld2D world, ReadOnlySpan<Contact2D> found, out int carried)
    {
        if (_touching.Length < found.Length)
        {
            Array.Resize(ref _touching, found.Length);
        }

        int next = 0;
        for (int index = 0; index < found.Length; index++)
        {
            if (Holds(_wasTouching, _wasTouchingCount, found[index].Target))
            {
                _touching[next++] = Describe(world, found[index]);
            }
        }

        carried = next;
        for (int index = 0; index < found.Length; index++)
        {
            if (!Holds(_wasTouching, _wasTouchingCount, found[index].Target))
            {
                _touching[next++] = Describe(world, found[index]);
            }
        }

        return next;
    }

    // Builds the shape the way the world would hold it before anything is committed. An offset that
    // cannot be placed throws here instead of later, when the entity joins a scene.
    private void RequirePlaceable(in Shape2D shape, Vector2 offset)
    {
        Shape2D local = shape.Translated(offset);

        if (Entity is { } entity)
        {
            _ = local.Translated(entity.WorldPosition);
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
                "A default Shape2D holds no points. Build one with Shape2D.Box, Shape2D.Circle, Shape2D.Capsule or Shape2D.Polygon.",
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

    internal static ColliderContact2D Describe(CollisionWorld2D world, in Contact2D contact)
    {
        object? owner = world.UserDataOf(contact.Target.Collider);
        Collider2D? otherCollider = contact.Target.IsGridCell ? null : owner as Collider2D;

        // A grid no tile map owns reports no tile. The raw target still names its cell.
        TileContact2D? tile = contact.Target.IsGridCell && owner is TileMap map
            ? new TileContact2D(map, contact.Target.CellX, contact.Target.CellY)
            : null;

        return new ColliderContact2D(
            world,
            contact.Target,
            contact.Point,
            contact.Normal,
            contact.Depth,
            otherCollider,
            tile);
    }

    // Resolves layer names to a filter in this world, interning each name as it goes, because a
    // collider may name a layer no other collider has registered yet. A name the world has no room
    // for throws here. A null world resolves nothing, so the result is None.
    internal static CollisionFilter ResolveFilter(CollisionWorld2D? world, ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(names));
        }

        CollisionFilter filter = CollisionFilter.None;
        if (world is { } present)
        {
            foreach (string name in names)
            {
                filter = filter.With(present.Layer(name));
            }
        }

        return filter;
    }

    private void Resync()
    {
        if (_world is { } world)
        {
            world.SetShape(_handle, _local);
            world.SetPosition(_handle, Entity!.WorldPosition);
        }
    }

    private CollisionWorld2D RequireWorld() =>
        _world ?? throw new InvalidOperationException(
            "This Collider2D is disabled or in no scene, so it has no world to query. Enable it and add its entity to a scene.");
}
