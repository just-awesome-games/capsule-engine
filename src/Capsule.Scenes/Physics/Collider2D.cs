using System.Numerics;
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
/// Position is what a collider follows. A <see cref="BoxCollider2D"/> also follows an axis-aligned
/// scale in the entity's ancestry. Every collider refuses a turn anywhere in that ancestry, and every
/// other collider refuses a scale too. The subclass defines the shape, and <see cref="Offset"/> places
/// it relative to the position. Every query throws while the collider is disabled or in no scene. A
/// filter built from another collision world's layers throws too.
/// <para>
/// A contact handler may change its own collider's <see cref="Enabled"/>, <see cref="Offset"/> and
/// shape, and the change applies at once. Disabling or detaching the collider removes it from the
/// world, raises the exits it owes, and cancels the remaining enters for this step. Enabling it again
/// in the same dispatch announces nothing more until the next step. <see cref="Layer"/>,
/// <see cref="Detects"/>, <see cref="ReportsContacts"/>, <see cref="OneWay"/>,
/// <see cref="SolidSides"/> and attaching the collider throw for the length of the dispatch.
/// </para>
/// </remarks>
public abstract class Collider2D : Component
{
    private Shape2D _shape;

    // The shape at its offset under the world scale. This is the form the world holds.
    private Shape2D _local;
    private Vector2 _offset;

    // The world scale _local was built for. Only a box sits under a scale other than one.
    private Vector2 _scale = Vector2.One;

    // Whether the scaled shape spans no more than the slop on an axis. The collider then stays out of
    // the world with Enabled unchanged, and _local keeps the last shape that spanned.
    private bool _collapsed;

    private string _layer = CollisionWorld2D.DefaultLayerName;
    private bool _enabled = true;
    private bool _reportsContacts;
    private bool _oneWay;
    private bool _solidSides;
    private CollisionMask _detects = CollisionMask.Empty;

    // Whether the warning for reporting contacts with an empty Detects has fired since this
    // registered collider entered that state.
    private bool _warnedReportingNothing;

    private CollisionWorld2D? _world;
    private Scene? _scene;
    private ColliderHandle _handle;

    // The contact buffers grow together, so swapping _touching and _wasTouching never resizes.
    private Contact2D[] _found = new Contact2D[16];
    private ColliderContact2D[] _touching = new ColliderContact2D[16];
    private ColliderContact2D[] _wasTouching = new ColliderContact2D[16];
    private int _touchingCount;

    // Whether each entry of _touching has been raised through ContactEntered and is owed a
    // ContactExited.
    private bool[] _announced = new bool[16];

    // True while this collider's own enter and exit handlers are running.
    private bool _dispatching;

    // Counts unregisters. A dispatch that sees it change stops announcing enters, even when a handler
    // has registered the collider again.
    private int _unregisters;

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
    /// A handler may change the collider's <see cref="Enabled"/>, <see cref="Offset"/> and shape, and
    /// sees the change at once. A handler that disables or detaches it ends the dispatch, and the
    /// contacts the loop had not reached go unannounced.
    /// </remarks>
    public event Action<ColliderContact2D>? ContactEntered;

    /// <summary>
    /// Raised for each thing this collider stopped touching since the previous step, and for
    /// everything it had announced entering when it left its scene, was disabled, stopped reporting
    /// contacts, or was detached from its entity.
    /// </summary>
    /// <remarks>
    /// Exits come in <see cref="Touching"/> order, before the step's enters. Each enter is paired with
    /// one exit, provided the handlers return normally. A handler may change the collider as a
    /// <see cref="ContactEntered"/> handler may.
    /// </remarks>
    public event Action<ColliderContact2D>? ContactExited;

    /// <summary>The shape in the collider's own space. <see cref="Offset"/> and the entity's position place it.</summary>
    public Shape2D Shape => _shape;

    // The shape at its offset under the world scale. The world translates this by the entity's position.
    internal Shape2D Local => _local;

    // The shape at its current place in the world.
    private protected Shape2D WorldShape =>
        Entity is { } entity
            ? _local.Translated(entity.WorldPosition)
            : throw new InvalidOperationException("This Collider2D is attached to no entity. Attach it before asking where its shape sits.");

    // Null to use the channel's colour. A disabled or collapsed collider draws in that colour at half alpha.
    private protected ColorRgba? DebugColor => _enabled && !_collapsed ? null : DebugDraw.ColorOf(DebugDraw.Colliders) with { A = 128 };

    private protected Vector2 Motion => Entity!.WorldPosition - Entity.PreviousWorld.Position;

    /// <summary>Added to the entity's position to place the shape, in the collider's own units. Zero by default.</summary>
    /// <exception cref="ArgumentException">The shape cannot be placed at this offset.</exception>
    public Vector2 Offset
    {
        get => _offset;
        set
        {
            Guard.Finite(value, nameof(value));
            Reshape(_shape, value, _scale);
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
            if (_enabled != value)
            {
                SetEnabled(value);
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

            SyncContactFilter();
            WarnIfReportingNothing();
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
    /// The layers this collider's contacts and its own queries find, where the empty default finds nothing.
    /// </summary>
    /// <remarks>
    /// Detection is one-way. Other colliders and queries find this one by its <see cref="Layer"/>,
    /// whatever it detects. Detecting a layer does not block on it. A body's
    /// <see cref="KinematicBody2D.BlockedBy"/> decides what stops it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public CollisionMask Detects
    {
        get => _detects;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            RequireNotDispatching();

            // Resolve before storing. A world with no layer slots left throws here, while the
            // collider still detects what it did.
            CollisionFilter filter = ResolveFilter(_scene?.Collision, value);
            _detects = value;
            if (_world is not null)
            {
                Filter = filter;
                SyncContactFilter();
                WarnIfReportingNothing();
            }
        }
    }

    // Detects resolved in the world this collider is registered with, or None outside one.
    internal CollisionFilter Filter { get; private set; }

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

            if (_scene is { } scene)
            {
                // Intern first. A world with no layer slots left throws here, while the collider
                // still holds its old layer.
                CollisionLayer layer = scene.Collision.Layer(value);
                _layerIndex = layer.Index;
                _world?.SetLayer(_handle, layer);
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
    /// The <see cref="ColliderContact2D"/> contacts this collider held when contacts last settled, in
    /// overlap-query order, and empty while <see cref="ReportsContacts"/> is off. Contacts settle once a step,
    /// after every entity's step and before any late step.
    /// </summary>
    /// <remarks>
    /// A move made after the settle shows on the next one. A collider out of the world at the settle holds an
    /// empty list until the next step's settle, even when enabled again before then. A collider on a held
    /// entity keeps the list from its last settle. Disabling or detaching the collider, or turning
    /// <see cref="ReportsContacts"/> off, empties the list at once. A scene removal made during a step empties
    /// it at the step's end, and the removed entity reads <see cref="Scenes.Entity.IsRemovalPending"/> until then. A
    /// span read before the list empties keeps its contents. A loop that can disable the collider checks
    /// <see cref="Enabled"/> before each contact.
    /// <para>
    /// During a dispatch the span can already hold contacts whose <see cref="ContactEntered"/> has not been
    /// raised. The enter and exit pairing is a guarantee about the events, not about this span.
    /// </para>
    /// </remarks>
    public ReadOnlySpan<ColliderContact2D> Touching => _touching.AsSpan(0, _touchingCount);

    /// <summary>
    /// Writes into <paramref name="contacts"/> everything within
    /// <see cref="CollisionTolerance.ContactSkin"/> of this collider that
    /// <see cref="Detects"/> matches. The collider never reports itself.
    /// </summary>
    /// <returns>
    /// The total overlap count. A span shorter than that count is filled to capacity and the
    /// remaining overlaps are counted but not written.
    /// </returns>
    /// <remarks><see cref="Scene.ColliderOf"/> finds the collider and entity behind each contact.</remarks>
    public int OverlapAll(Span<Contact2D> contacts) => RequireWorld().OverlapColliderAll(_handle, Filter, contacts);

    /// <summary>
    /// Writes into <paramref name="contacts"/> everything within
    /// <see cref="CollisionTolerance.ContactSkin"/> of this collider that <paramref name="mask"/>
    /// matches, instead of <see cref="Detects"/>, for this call only.
    /// </summary>
    /// <remarks>
    /// All other rules of
    /// <see cref="OverlapAll(Span{Contact2D})"/> apply.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public int OverlapAll(CollisionMask mask, Span<Contact2D> contacts) =>
        RequireWorld().OverlapColliderAll(_handle, mask, contacts);

    /// <summary>
    /// Reports whether this collider is within <see cref="CollisionTolerance.ContactSkin"/> of
    /// <paramref name="other"/>, ignoring what either collider <see cref="Detects"/>. A collider never
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
    /// Casts a ray from the centre of this collider's <see cref="Bounds"/>, matching
    /// <see cref="Detects"/> and never hitting this collider. Reports the nearest hit and breaks ties
    /// the way
    /// <see cref="CollisionWorld2D.Raycast(Vector2, Vector2, float, CollisionFilter, out RayHit2D, ColliderHandle)"/>
    /// does.
    /// </summary>
    /// <param name="direction">Which way to look. Any non-zero length works.</param>
    /// <param name="distance">How far to look, in world units.</param>
    /// <param name="hit">The nearest hit, when there is one.</param>
    /// <returns>Whether the ray hit anything.</returns>
    public bool Raycast(Vector2 direction, float distance, out RayHit2D hit) =>
        Raycast(direction, distance, Filter, out hit);

    /// <summary>
    /// Casts a ray against <paramref name="filter"/> instead of <see cref="Detects"/>, for this call
    /// only.
    /// </summary>
    /// <remarks>
    /// <see cref="CollisionFilter.None"/> hits nothing. All other rules of
    /// <see cref="Raycast(Vector2, float, out RayHit2D)"/> apply.
    /// </remarks>
    public bool Raycast(Vector2 direction, float distance, CollisionFilter filter, out RayHit2D hit)
    {
        RequireRayDistance(distance);

        return RequireWorld().Raycast(Bounds.Center, direction, distance, filter, out hit, _handle);
    }

    /// <summary>
    /// Casts a ray against <paramref name="mask"/> instead of <see cref="Detects"/>, for this call
    /// only.
    /// </summary>
    /// <remarks>
    /// All other rules of
    /// <see cref="Raycast(Vector2, float, out RayHit2D)"/> apply.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public bool Raycast(Vector2 direction, float distance, CollisionMask mask, out RayHit2D hit)
    {
        RequireRayDistance(distance);

        return RequireWorld().Raycast(Bounds.Center, direction, distance, mask, out hit, _handle);
    }

    /// <summary>
    /// Sweeps this collider's shape from its current place along <paramref name="translation"/> and
    /// reports the first thing it hits, matching <see cref="Detects"/> and never itself.
    /// </summary>
    /// <remarks>
    /// Nothing moves. Something the collider starts more than
    /// <see cref="CollisionTolerance.ContactSkin"/> inside reports at fraction 0 whichever way the
    /// sweep moves, unless it is one-way. A surface the collider merely touches reports at fraction 0
    /// when the sweep drives into it, and is ignored when the sweep runs along it or away from it.
    /// <see cref="CollisionWorld2D.ShapeCast(in Shape2D, Vector2, Vector2, CollisionFilter, out ShapeCastHit2D, ColliderHandle)"/>
    /// gives the point and normal of each.
    /// </remarks>
    /// <param name="translation">How far and which way to sweep, in world units.</param>
    /// <param name="hit">The nearest hit, when there is one.</param>
    /// <returns>Whether the sweep hit anything.</returns>
    public bool Cast(Vector2 translation, out ShapeCastHit2D hit) => Cast(translation, Filter, out hit);

    /// <summary>
    /// Sweeps this collider's shape against <paramref name="filter"/> instead of
    /// <see cref="Detects"/>, for this call only.
    /// </summary>
    /// <remarks>
    /// <see cref="CollisionFilter.None"/> hits nothing. All other rules of
    /// <see cref="Cast(Vector2, out ShapeCastHit2D)"/> apply.
    /// </remarks>
    public bool Cast(Vector2 translation, CollisionFilter filter, out ShapeCastHit2D hit) =>
        RequireWorld().ShapeCast(_local, Entity!.WorldPosition, translation, filter, out hit, _handle);

    /// <summary>
    /// Sweeps this collider's shape against <paramref name="mask"/> instead of
    /// <see cref="Detects"/>, for this call only.
    /// </summary>
    /// <remarks>
    /// All other rules of <see cref="Cast(Vector2, out ShapeCastHit2D)"/> apply.
    /// </remarks>
    /// <example>
    /// A wall probe that sweeps the player's collider against climbable layers only:
    /// <code>
    /// private static readonly CollisionMask Climbable = new(CollisionLayers.Climbable);
    ///
    /// if (_collider.Cast(new Vector2(reach, 0f), Climbable, out ShapeCastHit2D hit))
    /// {
    ///     // Kick off hit.Normal.
    /// }
    /// </code>
    /// </example>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public bool Cast(Vector2 translation, CollisionMask mask, out ShapeCastHit2D hit) =>
        RequireWorld().ShapeCast(_local, Entity!.WorldPosition, translation, mask, out hit, _handle);

    /// <summary>
    /// Replaces the collider's shape with <paramref name="shape"/>. Queries see the new shape as
    /// soon as this returns.
    /// </summary>
    /// <remarks>A throw leaves the collider unchanged.</remarks>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, or cannot be placed at the current offset.</exception>
    protected void SetShape(in Shape2D shape)
    {
        RequireShape(shape);
        Reshape(shape, _offset, _scale);
    }

    internal override TransformSupport Supports => TransformSupport.Position;

    // Builds the form the world holds from the shape, its offset and the world scale, or returns false
    // when that form spans no more than the slop on an axis. Only a collider whose Supports includes
    // Resize ever sees a scale other than one.
    private protected virtual bool TryPlace(in Shape2D shape, Vector2 offset, Vector2 scale, out Shape2D placed)
    {
        placed = shape.Translated(offset);
        return true;
    }

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
        // Parenting outside a scene notifies nothing, so the scale catches up before registering.
        FollowScale();

        _scene = Entity!.Scene;
        if (_enabled && !_collapsed)
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

    private void SetEnabled(bool value)
    {
        _enabled = value;
        if (_scene is null || _collapsed)
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

    private void Register()
    {
        Scene scene = _scene!;
        CollisionWorld2D world = scene.Collision;
        CollisionFilter filter = ResolveFilter(world, _detects);
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
            SyncContactFilter();
            scene.TrackContacts(this);
        }

        WarnIfReportingNothing();
    }

    private void Unregister()
    {
        if (_world is not { } world)
        {
            return;
        }

        _unregisters++;
        _scene?.UntrackContacts(this);
        world.Remove(_handle);
        ReleaseRiders();
        Body?.Unride();

        Filter = CollisionFilter.None;
        _world = null;
        _handle = ColliderHandle.None;
        _warnedReportingNothing = false;

        EndAnnouncedContacts();
    }

    // Raises an exit for each announced contact, in Touching order, and leaves the collider holding
    // none. The count is cleared before the first handler runs, and a handler that detaches from in
    // here finds nothing owed and cannot exit the same contact twice.
    private void EndAnnouncedContacts()
    {
        ColliderContact2D[] touching = _touching;
        bool[] announced = _announced;
        int count = _touchingCount;

        _touchingCount = 0;

        // This can run inside a dispatch, from a handler that disabled or detached the collider. The
        // outer dispatch stays guarded after it.
        bool dispatching = _dispatching;
        _dispatching = true;
        try
        {
            for (int index = 0; index < count; index++)
            {
                if (announced[index])
                {
                    ContactExited?.Invoke(touching[index]);
                }
            }
        }
        finally
        {
            _dispatching = dispatching;
        }
    }

    // A collider no body is moved by only follows its entity. One that is carries its riders by the
    // same translation and then shoves the bodies it moved into.
    internal override void OnEntityMoved()
    {
        FollowScale();

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

    // Shoves each body this collider's move drove into by the travel left after meeting it, in handle
    // order. A rider whose carry took the whole motion is passed by, and one a carry could not clear is
    // checked. The move is swept from where the collider was, so a body thinner than the move is still met.
    private void ShoveBodies(CollisionWorld2D world, Vector2 from, Vector2 motion)
    {
        if ((_scene!.MovedByLayers & (1UL << _layerIndex)) == 0)
        {
            return;
        }

        // A body resting on its center stands below the pose it sweeps. The reach extends down to it.
        Aabb2D path = _local.Translated(from).Bounds.Union(world.WorldShapeOf(_handle).Bounds)
            .Expanded(CollisionTolerance.ContactSkin);
        Aabb2D reach = new(path.Min, path.Max + new Vector2(0f, _scene.DeepestSink));

        _near ??= new ColliderHandle[8];
        int count = world.CollidersNear(reach, _handle, _near);
        if (count > _near.Length)
        {
            Grow(ref _near, count);
            count = world.CollidersNear(reach, _handle, _near);
        }

        for (int index = 0; index < count && _world is not null; index++)
        {
            ColliderHandle near = _near[index];
            if (!world.Contains(near)
                || world.UserDataOf(near) is not Collider2D { Body: { } body }
                || !body.IsMovedBy(_layerIndex)
                || MovesWith(body.Entity)
                || body.CarriedWholeBy(this)
                || !world.SweepPair(
                    _handle,
                    from,
                    motion,
                    near,
                    body.SweptOffset(),
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

        // Grow the buffers to the reported count and gather again, because a truncated gather would
        // silently drop a contact's enter and its later exit.
        int count = world.ContactsOf(_handle, _found);
        if (count > _found.Length)
        {
            Grow(ref _found, count);
            Grow(ref _touching, count);
            Grow(ref _wasTouching, count);
            Grow(ref _announced, count);
            count = world.ContactsOf(_handle, _found);
        }

        // Nothing touched before or now, and nothing is owed or raised.
        if (count == 0 && _touchingCount == 0)
        {
            return;
        }

        (_touching, _wasTouching) = (_wasTouching, _touching);
        int leftCount = Diff(world, count, _touchingCount);
        _touchingCount = count;

        ColliderContact2D[] touching = _touching;
        ColliderContact2D[] left = _wasTouching;
        bool[] announced = _announced;

        int unregisters = _unregisters;
        _dispatching = true;
        try
        {
            // Exits run before enters, and a handler reading Touching sees the settled set. This loop
            // finishes even if a handler detaches the collider. These contacts and the unregister
            // sweep's are disjoint, so nothing exits twice and nothing is dropped.
            for (int index = 0; index < leftCount; index++)
            {
                ContactExited?.Invoke(left[index]);
            }

            // A handler that detaches this collider removes it from the world and raises the exits
            // it owes. Nothing is left to enter.
            for (int index = 0; index < count && _unregisters == unregisters; index++)
            {
                if (announced[index])
                {
                    continue;
                }

                // Marked before the handler runs, so a handler that detaches from inside it still
                // owes this contact an exit.
                announced[index] = true;
                ContactEntered?.Invoke(touching[index]);
            }
        }
        finally
        {
            _dispatching = false;
        }
    }

    // Writes the gather into _touching and marks the contacts carried over from the previous step as
    // announced. Both lists are in overlap-query order, so one merge finds them. The previous
    // contacts not found again move to the head of _wasTouching in their order, and the return value
    // says how many there are.
    private int Diff(CollisionWorld2D world, int count, int wasCount)
    {
        int was = 0;
        int leftCount = 0;
        for (int index = 0; index < count; index++)
        {
            ref readonly Contact2D found = ref _found[index];
            while (was < wasCount && world.OverlapOrder(_wasTouching[was].Target, found.Target) < 0)
            {
                _wasTouching[leftCount++] = _wasTouching[was++];
            }

            bool carried = was < wasCount && _wasTouching[was].Target == found.Target;
            if (carried)
            {
                was++;
            }

            _touching[index] = Describe(world, found);
            _announced[index] = carried;
        }

        while (was < wasCount)
        {
            _wasTouching[leftCount++] = _wasTouching[was++];
        }

        return leftCount;
    }

    // A pure translation leaves the world scale as it was and does no shape work.
    private void FollowScale()
    {
        Vector2 scale = Entity!.WorldTransform.Scale;
        if (scale != _scale)
        {
            Reshape(_shape, _offset, scale);
        }
    }

    // Builds the form the world holds and commits it with its inputs. A form that cannot be placed
    // throws before anything changes, here instead of later when the entity joins a scene. A form that
    // collapses takes the collider out of the world as disabling does, and one that spans again puts
    // it back.
    private void Reshape(in Shape2D shape, Vector2 offset, Vector2 scale)
    {
        bool spans = TryPlace(shape, offset, scale, out Shape2D local);
        if (spans && Entity is { } entity)
        {
            _ = local.Translated(entity.WorldPosition);
        }

        bool wasCollapsed = _collapsed;
        _shape = shape;
        _offset = offset;
        _scale = scale;
        _collapsed = !spans;
        if (spans)
        {
            _local = local;
        }

        if (_scene is null || !_enabled)
        {
            return;
        }

        if (!spans)
        {
            Unregister();
        }
        else if (wasCollapsed)
        {
            Register();
        }
        else if (_world is { } world)
        {
            world.SetShape(_handle, _local);
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

    private static void RequireShape(in Shape2D shape)
    {
        if (shape.PointCount == 0)
        {
            throw new ArgumentException(
                "A default Shape2D holds no points. Build one with Shape2D.Box, Shape2D.Circle, Shape2D.Capsule or Shape2D.Polygon.",
                nameof(shape));
        }
    }

    // Grows a contact buffer to the power of two that holds count, and leaves one that holds it already.
    // A count rising one a step then reallocates a handful of times instead of on every step.
    internal static void Grow<T>(ref T[] buffer, int count)
    {
        if (buffer.Length < count)
        {
            Array.Resize(ref buffer, (int)BitOperations.RoundUpToPowerOf2((uint)count));
        }
    }

    internal static ColliderContact2D Describe(CollisionWorld2D world, in Contact2D contact)
    {
        object? owner = world.UserDataOf(contact.Target.Collider);
        Collider2D? otherCollider = contact.Target.IsGridCell ? null : owner as Collider2D;

        // A grid no tile-map collider owns reports no tile. The raw target still names its cell.
        TileContact2D? tile = contact.Target.IsGridCell && owner is TileMapCollider2D { Entity: TileMap map }
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

    // Resolves a mask to a filter in this world, interning each name as it goes, because a collider
    // may name a layer no other collider has registered yet. A null world resolves to None. This
    // skips the world's mask table, which a mask built per entity would grow without bound.
    internal static CollisionFilter ResolveFilter(CollisionWorld2D? world, CollisionMask mask) =>
        world?.Intern(mask.Names) ?? CollisionFilter.None;

    // Warns once each time a registered collider starts reporting contacts while detecting no layer.
    // Initializers run before registration, so setting ReportsContacts before Detects never warns.
    private void WarnIfReportingNothing()
    {
        bool reportsNothing = _reportsContacts && _detects.Names.IsEmpty;
        if (reportsNothing && !_warnedReportingNothing)
        {
            Log.Warning(
                $"A {GetType().Name} on a {Entity!.GetType().Name} reports contacts but its Detects is empty, "
                + "and it will report none. Set Detects to the layers it should report");
        }

        _warnedReportingNothing = reportsNothing;
    }

    // The world keeps candidates only for a collider that reports contacts.
    private void SyncContactFilter() =>
        _world!.SetContactFilter(_handle, _reportsContacts ? Filter : CollisionFilter.None);

    // The world allows a zero distance, but a zero-length ray from a collider that ignores itself
    // always returns false. Reject it as a caller mistake.
    private static void RequireRayDistance(float distance)
    {
        if (!float.IsFinite(distance) || distance <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "A collider's ray must reach a finite, positive distance.");
        }
    }

    private CollisionWorld2D RequireWorld() =>
        _world ?? throw new InvalidOperationException(
            "This Collider2D is disabled or in no scene, so it has no world to query. Enable it and add its entity to a scene.");
}
