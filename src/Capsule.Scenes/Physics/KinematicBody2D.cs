using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Physics;

/// <summary>
/// Sweeps one selected <see cref="Collider2D"/> through the scene's collision world, stopping and
/// sliding against an independently configured set of blocking layers, and reports which way it was
/// stopped. The caller owns velocity, acceleration and every gameplay response.
/// </summary>
/// <remarks>
/// This component applies no force. An entity holds at most one body, and attaching a second
/// throws.
/// <para>
/// <see cref="IsOnFloor"/>, <see cref="IsOnWall"/> and <see cref="IsOnCeiling"/> describe the last
/// <see cref="Move(Vector2)"/> only. A move that pressed into nothing clears them, and so does a
/// zero translation, except that a grounded body standing on a floor snaps to it and stays on it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// BoxCollider2D bodyCollider = new(new Vector2(8f, 8f));
/// Add(bodyCollider);
///
/// _body = new KinematicBody2D(bodyCollider);
/// _body.BlocksOn(CollisionLayers.Blocking);
/// Add(_body);
///
/// // Each step, from the entity's own OnStep:
/// _velocity.Y += _tuning.Gravity * context.DeltaSeconds;
/// _body.Move(_velocity * context.DeltaSeconds);
/// if (_body.IsOnFloor)
/// {
///     _velocity.Y = 0f;
/// }
/// </code>
/// </example>
public sealed class KinematicBody2D : Component
{
    // Y-down, so up is negative Y. Make this an instance property when a consumer needs to flip gravity.
    // A static readonly field would cost a class-initialisation check at every use without tiered
    // compilation. This form compiles to a constant.
    private static Vector2 Up => new(0f, -1f);

    // How far a normal's cosine to up may fall short of MaxFloorAngle's and still count. An exact
    // 45 degree edge then reads as a floor at the default.
    private const float AngleTolerance = 1e-4f;

    private const float DegreesToRadians = MathF.PI / 180f;

    private float _maxFloorAngle;

    // The cosine and tangent of MaxFloorAngle, taken once when it is set.
    private float _floorCos;
    private float _floorTan;

    // Set by DropThrough and consumed by the next Move.
    private bool _dropThrough;

    private BodyMode _mode;

    private readonly Collider2D _collider;
    private readonly List<string> _blocksOn = [];
    private readonly List<string> _movedBy = [];

    private Scene? _scene;

    // BlocksOn's names resolved in the current scene's world. Filter adds the MovedBy layers to it.
    private CollisionFilter _blocksOnFilter;

    // The collider this body rides, as found by its last Move. Only a Move changes it.
    private Collider2D? _floor;

    // True while this body writes its own position. A collider that moves because of that write, such
    // as one on a child entity, cannot carry or shove the body again.
    private bool _moving;

    private Contact2D[] _found = new Contact2D[16];

    // Whether the contact at the same index of _found belongs to a pass that stopped the move.
    private bool[] _stopped = new bool[16];
    private ColliderContact2D[] _moveContacts = new ColliderContact2D[16];
    private int _moveContactCount;

    /// <param name="collider">
    /// The collider whose shape this body sweeps. It must be attached to the same entity as the body by
    /// the time that entity joins a scene. The two may be attached in either order.
    /// </param>
    public KinematicBody2D(Collider2D collider)
    {
        ArgumentNullException.ThrowIfNull(collider);
        _collider = collider;
        MaxFloorAngle = 45f;
    }

    /// <summary>How a move meets what it hits. <see cref="BodyMode.Floating"/> by default.</summary>
    public BodyMode Mode
    {
        get => _mode;
        set
        {
            if (value is not (BodyMode.Floating or BodyMode.Grounded))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Mode is not a BodyMode. Use Floating or Grounded.");
            }

            _mode = value;
        }
    }

    /// <summary>
    /// The steepest surface, in degrees from flat, that counts as a floor. 45 by default.
    /// </summary>
    /// <remarks>
    /// A surface this steep or flatter is a floor, one as steep seen from below is a ceiling, and
    /// anything between is a wall. A grounded body walks up a floor and stops at a wall.
    /// </remarks>
    public float MaxFloorAngle
    {
        get => _maxFloorAngle;
        set
        {
            if (!(value >= 0f && value < 90f))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxFloorAngle must be at least 0 and under 90 degrees.");
            }

            _maxFloorAngle = value;
            _floorCos = DeterministicMath.Cos(value * DegreesToRadians);
            _floorTan = DeterministicMath.Tan(value * DegreesToRadians);
        }
    }

    /// <summary>The collider whose shape this body sweeps.</summary>
    /// <remarks>
    /// Sweeping requires it to be enabled, registered in a scene, and still attached to this body's
    /// entity. Otherwise every move and test throws.
    /// </remarks>
    public Collider2D Collider => _collider;

    /// <summary>
    /// The layers that stop this body, its <see cref="BlocksOn"/> and <see cref="MovedBy"/> layers, built
    /// for the current scene's collision world. Reads <see cref="CollisionFilter.None"/> while this
    /// component is in no scene.
    /// </summary>
    public CollisionFilter Filter { get; private set; }

    /// <summary>
    /// Raised when a collider on a <see cref="MovedBy"/> layer moves into this body and the body cannot
    /// get out of the way.
    /// </summary>
    /// <remarks>
    /// The contact describes the pusher's surface, and <c>Normal * Depth</c> leads out of it. The
    /// pusher is never stopped, and the body stays where the shove left it. The event is raised on every
    /// move that pins the body. A handler may call <see cref="Move(Vector2)"/> and
    /// <see cref="TestMove(Vector2)"/>.
    /// </remarks>
    public event Action<ColliderContact2D>? Crushed;

    /// <summary>The surfaces the most recent <see cref="Move(Vector2)"/> reached.</summary>
    public ReadOnlySpan<ColliderContact2D> MoveContacts => _moveContacts.AsSpan(0, _moveContactCount);

    /// <summary>
    /// Whether the last move was stopped by a floor, a blocking contact whose normal is within
    /// <see cref="MaxFloorAngle"/> of up (negative Y).
    /// </summary>
    public bool IsOnFloor { get; private set; }

    /// <summary>Whether the last move was stopped by a wall, a blocking contact whose normal is neither floor nor ceiling.</summary>
    public bool IsOnWall { get; private set; }

    /// <summary>
    /// Whether the last move was stopped by a ceiling, a blocking contact whose normal is within
    /// <see cref="MaxFloorAngle"/> of down.
    /// </summary>
    public bool IsOnCeiling { get; private set; }

    /// <summary>The floor contact's normal, or zero when <see cref="IsOnFloor"/> is false.</summary>
    public Vector2 FloorNormal { get; private set; }

    /// <summary>
    /// The wall contact's normal, or zero when <see cref="IsOnWall"/> is false. The sign of X says which
    /// side the wall is on.
    /// </summary>
    public Vector2 WallNormal { get; private set; }

    /// <summary>
    /// Replaces the layers that stop this body. This filter is separate from
    /// <see cref="Collider2D.SetFilter"/>, which lets a collider report an overlap that does not
    /// change movement.
    /// </summary>
    /// <param name="names">The layer names that block movement. An empty list blocks on nothing.</param>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name.</exception>
    public void BlocksOn(params ReadOnlySpan<string> names)
    {
        // Resolve before touching the stored list. A bad name part way along leaves the old list
        // intact.
        CollisionFilter filter = Collider2D.ResolveFilter(Entity?.SceneOrNull?.Collision, names);

        _blocksOn.Clear();
        foreach (string name in names)
        {
            _blocksOn.Add(name);
        }

        if (InScene)
        {
            _blocksOnFilter = filter;
            Filter = filter | MovedByFilter;
        }
    }

    /// <summary>
    /// Replaces the layers whose moving colliders move this body. A body standing on one rides it, and
    /// one moving into the body shoves it.
    /// </summary>
    /// <remarks>
    /// A layer that moves the body also blocks it. The body rides the floor its last
    /// <see cref="Move(Vector2)"/> stopped on, and is carried exactly whether that floor steps before
    /// or after it. A wall or ceiling stops a carry or a shove.
    /// </remarks>
    /// <param name="names">
    /// The layer names that move this body. An empty list, the default, moves it by nothing.
    /// </param>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name.</exception>
    public void MovedBy(params ReadOnlySpan<string> names)
    {
        CollisionFilter filter = Collider2D.ResolveFilter(Entity?.SceneOrNull?.Collision, names);

        _movedBy.Clear();
        foreach (string name in names)
        {
            _movedBy.Add(name);
        }

        if (InScene)
        {
            SetMovedBy(filter);
            Filter = _blocksOnFilter | filter;
        }
    }

    /// <summary>
    /// Attempts <paramref name="translation"/>, in world units, and returns the part that was
    /// applied.
    /// </summary>
    /// <remarks>
    /// One call adds the resolved translation to the entity's position, replaces
    /// <see cref="MoveContacts"/>, and sets <see cref="IsOnFloor"/>, <see cref="IsOnWall"/>,
    /// <see cref="IsOnCeiling"/>, <see cref="FloorNormal"/> and <see cref="WallNormal"/>. Velocity
    /// and force stay the caller's. <see cref="Mode"/> decides how the move meets what it hits.
    /// </remarks>
    public MoveResult2D Move(Vector2 translation) => MoveWith(translation, Filter);

    /// <summary>
    /// Lets the next <see cref="Move(Vector2)"/> pass through one-way surfaces, tiles and colliders
    /// alike.
    /// </summary>
    /// <remarks>
    /// That move consumes it whether or not it meets one, and does not snap to the ground. Leaving the
    /// scene clears it. A body already past a one-way surface keeps falling through it on later moves.
    /// </remarks>
    public void DropThrough() => _dropThrough = true;

    /// <summary>
    /// Attempts <paramref name="translation"/> against <paramref name="blocking"/> instead of
    /// <see cref="Filter"/>, for this call only.
    /// </summary>
    /// <remarks>
    /// <see cref="CollisionFilter.None"/> stops on nothing. The stored <see cref="BlocksOn"/>
    /// layers are unchanged, and the next plain <see cref="Move(Vector2)"/> uses them again.
    /// </remarks>
    public MoveResult2D Move(Vector2 translation, CollisionFilter blocking) => MoveWith(translation, blocking);

    /// <summary>
    /// Reports whether <see cref="Move(Vector2)"/> of <paramref name="translation"/> would be
    /// stopped short. It sweeps the body's own collider from its current place, using
    /// <see cref="Filter"/> and never hitting itself.
    /// </summary>
    /// <remarks>
    /// Nothing moves and nothing is written, including <see cref="IsOnFloor"/> and its peers. The
    /// test slides as a <see cref="BodyMode.Floating"/> move does, and the translation is blocked when
    /// any part of it is stopped short. A surface reached exactly at the end of the translation does
    /// not count as a block.
    /// </remarks>
    /// <param name="translation">The move to test, in world units.</param>
    /// <returns>Whether something would stop the move short.</returns>
    public bool TestMove(Vector2 translation) => TestMove(translation, Vector2.Zero);

    /// <summary>
    /// Reports whether <paramref name="translation"/> would be stopped short if the body stood
    /// <paramref name="from"/> away from its current place. All other rules of
    /// <see cref="TestMove(Vector2)"/> apply, and nothing moves to the offset.
    /// </summary>
    public bool TestMove(Vector2 translation, Vector2 from)
    {
        CollisionWorld2D world = RequireSweepable(out Entity entity);

        // Pass an empty contact span, because only the blocked flag matters here and this query does not
        // report what the sweep touched.
        MoveResult2D result = world.Move(
            world.ShapeOf(_collider.Handle),
            entity.WorldPosition + from,
            translation,
            Filter,
            default,
            _collider.Handle);

        return result.Blocked;
    }

    private MoveResult2D MoveWith(Vector2 translation, CollisionFilter blocking)
    {
        CollisionWorld2D world = RequireSweepable(out Entity entity);
        Guard.Finite(translation, nameof(translation));
        Shape2D shape = world.ShapeOf(_collider.Handle);
        Vector2 origin = entity.WorldPosition;
        Guard.Finite(origin + translation, nameof(translation));

        bool through = _dropThrough;
        _dropThrough = false;

        MoveResult2D result = Resolve(world, shape, origin, translation, blocking, through);
        if (result.ContactCount > _found.Length)
        {
            Array.Resize(ref _found, result.ContactCount);
            Array.Resize(ref _stopped, result.ContactCount);
            result = Resolve(world, shape, origin, translation, blocking, through);
        }

        // A world translation equals a local one here, because nothing above a body is turned or scaled.
        Displace(entity, result.Translation);
        _moveContactCount = Collider2D.Describe(
            world,
            _found.AsSpan(0, result.ContactCount),
            ref _moveContacts);

        Classify();

        return result;
    }

    // Runs the whole move against the world without writing the body. It is pure, so an overflowing
    // contact span can be grown and the move run again. Kept out of line, because each inlined copy
    // would add a sweep that MoveWith's frame zeroes on every move.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private MoveResult2D Resolve(CollisionWorld2D world, in Shape2D shape, Vector2 origin, Vector2 translation, CollisionFilter blocking, bool through)
    {
        MoveSweep sweep = new(world, shape, origin, blocking, _collider.Handle, through, _found, _stopped);

        if (_mode == BodyMode.Floating)
        {
            sweep.Slide(translation);
        }
        else
        {
            Walk(ref sweep, translation, through);
        }

        return sweep.Result;
    }

    // The grounded move. The part across up walks along the floor at its own length, the part along
    // up falls onto floors and rises into ceilings, and a body that stood on a floor follows it down.
    private void Walk(ref MoveSweep sweep, Vector2 translation, bool through)
    {
        float rise = Vector2.Dot(translation, Up);

        // Lateral is horizontal while up is -Y, so its length and direction need no square root.
        float length = MathF.Abs(translation.X);

        if (length > 0f)
        {
            Vector2 direction = new(MathF.Sign(translation.X), 0f);
            // A lateral direction already runs along a flat floor.
            if (IsOnFloor && FloorNormal != Up)
            {
                direction = Tangent(direction, FloorNormal);
            }

            float left = length;
            for (int pass = 0; pass < MoveSweep.MaxPasses && left > 0f && direction != Vector2.Zero; pass++)
            {
                MovePass step = sweep.Pass(direction * left);
                if (!step.Blocked)
                {
                    break;
                }

                // A walkable surface turns the rest of the walk along it at the same length. Anything
                // steeper stops the walk.
                left -= step.Moved.Length();
                direction = IsFloor(step.Normal) ? Tangent(direction, step.Normal) : Vector2.Zero;
            }
        }

        if (rise != 0f)
        {
            Vector2 remaining = Up * rise;
            for (int pass = 0; pass < MoveSweep.MaxPasses && remaining != Vector2.Zero; pass++)
            {
                MovePass step = sweep.Pass(remaining);

                // A floor stops a fall outright, so a body resting on a slope never slides down it.
                if (!step.Blocked || (rise < 0f ? IsFloor(step.Normal) : IsCeiling(step.Normal)))
                {
                    break;
                }

                remaining = MoveSweep.AlongSurface(remaining - step.Moved, step.Normal);
            }
        }

        // The farthest a walk of this length can leave a floor it followed is its length times the
        // steepest floor's slope, over a crest or off a step. Twice that covers a crest into a descent.
        if (IsOnFloor && rise <= 0f && !through && !StoppedOnFloor(sweep))
        {
            float reach = (2f * length * _floorTan) + CollisionTolerance.ContactSkin;
            sweep.Snap(-Up * reach, _floorCos - AngleTolerance);
        }
    }

    private bool StoppedOnFloor(in MoveSweep sweep)
    {
        for (int index = 0; index < sweep.Written; index++)
        {
            if (_stopped[index] && IsFloor(_found[index].Normal))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsFloor(Vector2 normal) => Vector2.Dot(normal, Up) >= _floorCos - AngleTolerance;

    private bool IsCeiling(Vector2 normal) => -Vector2.Dot(normal, Up) >= _floorCos - AngleTolerance;

    // The unit direction along a surface that keeps to the way `direction` was heading, or zero when
    // the direction runs straight into it.
    private static Vector2 Tangent(Vector2 direction, Vector2 normal)
    {
        Vector2 along = MoveSweep.AlongSurface(direction, normal);
        float length = along.Length();

        return length > 1e-6f ? along / length : Vector2.Zero;
    }

    // The MovedBy layers resolved in the current scene's world, or None in no scene.
    internal CollisionFilter MovedByFilter { get; private set; }

    // Whether this body is moved by the layer with this interned index.
    internal bool IsMovedBy(int layerIndex) => (MovedByFilter.Bits & (1UL << layerIndex)) != 0;

    // Sweeps a riding body along its floor's motion with its own blocking filter. A body writing its
    // own position is never moved again.
    internal void Carry(Vector2 motion)
    {
        if (_moving
            || _collider.World is not { } world
            || Entity is not { } entity
            || !ReferenceEquals(_collider.Entity, entity))
        {
            return;
        }

        MoveResult2D result = world.Move(
            world.ShapeOf(_collider.Handle),
            entity.WorldPosition,
            motion,
            Filter,
            default,
            _collider.Handle);

        Displace(entity, result.Translation);
    }

    // Shoves the body by what is left of the pusher's move after meeting it, or leaves a rider where
    // its carry put it. A shove that falls short by more than the mover's slop raises Crushed with the
    // pusher's surface where the two met, and the shortfall along its normal as the depth.
    internal void Shove(Collider2D pusher, Vector2 remainder, Vector2 normal, Vector2 point)
    {
        if (_moving)
        {
            return;
        }

        Vector2 applied = Vector2.Zero;
        if (!ReferenceEquals(_floor, pusher)
            && _collider.World is { } sweeping
            && Entity is { } entity
            && ReferenceEquals(_collider.Entity, entity))
        {
            MoveResult2D result = sweeping.MovePast(
                sweeping.ShapeOf(_collider.Handle),
                entity.WorldPosition,
                remainder,
                Filter,
                _collider.Handle,
                pusher.Handle);

            applied = result.Translation;
            Displace(entity, applied);
        }

        float shortfall = Vector2.Dot(remainder - applied, normal);
        if (shortfall > CollisionTolerance.LinearSlop && pusher.World is { } world)
        {
            Contact2D contact = new(
                CollisionTarget.ForCollider(pusher.Handle, world.LayerOf(pusher.Handle)),
                point,
                normal,
                shortfall);
            Crushed?.Invoke(Collider2D.Describe(world, contact));
        }
    }

    // Called by a collider this body rides as it leaves the world. The collider has already let go.
    internal void ForgetFloor(Collider2D floor)
    {
        if (ReferenceEquals(_floor, floor))
        {
            _floor = null;
        }
    }

    // Replaces the effective moved-by layers, keeping the scene's union current. A changed set drops
    // the riding link, which the next Move finds again.
    private void SetMovedBy(CollisionFilter effective)
    {
        if (effective == MovedByFilter)
        {
            return;
        }

        _scene!.CountMovedBy(MovedByFilter, -1);
        MovedByFilter = effective;
        _scene.CountMovedBy(effective, 1);
        Unride();
    }

    private void Displace(Entity entity, Vector2 translation)
    {
        if (translation == Vector2.Zero)
        {
            return;
        }

        _moving = true;
        try
        {
            entity.Position += translation;
        }
        finally
        {
            _moving = false;
        }
    }

    private void Ride(Collider2D? floor)
    {
        if (ReferenceEquals(_floor, floor))
        {
            return;
        }

        Unride();
        if (floor is not null)
        {
            floor.AddRider(this);
            _floor = floor;
        }
    }

    // Clears the riding link both ways.
    internal void Unride()
    {
        if (_floor is { } floor)
        {
            _floor = null;
            floor.RemoveRider(this);
        }
    }

    private CollisionWorld2D RequireSweepable(out Entity entity)
    {
        entity = Entity
            ?? throw new InvalidOperationException("A KinematicBody2D attached to no entity has nothing to sweep.");

        if (!ReferenceEquals(_collider.Entity, entity))
        {
            throw new InvalidOperationException(
                "A KinematicBody2D cannot sweep after its collider has left the body's entity.");
        }

        return _collider.World
            ?? throw new InvalidOperationException(
                "A KinematicBody2D needs its collider enabled and registered in a scene before it can sweep.");
    }

    // The sweep moves the entity along the world axes, so it supports position only.
    internal override TransformSupport Supports => TransformSupport.Position;

    // One body owns the entity's position write, so attaching a second body throws.
    internal override void OnAttachedTo(Entity entity)
    {
        foreach (Component held in entity.Components)
        {
            if (!ReferenceEquals(held, this) && held is KinematicBody2D)
            {
                throw new InvalidOperationException(
                    $"A {entity.GetType().Name} already holds a KinematicBody2D. Keep one body per entity, since each body writes the position from its own sweep.");
            }
        }
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Mode", _mode);
        panel.Field("IsOnFloor", IsOnFloor);
        panel.Field("IsOnWall", IsOnWall);
        panel.Field("IsOnCeiling", IsOnCeiling);
        panel.Field("FloorNormal", FloorNormal);
        panel.Field("WallNormal", WallNormal);
        panel.Field("MoveContacts", MoveContacts.Length);
    }

    // Checked when the entity joins the scene. A constructor may add the body before the collider it
    // sweeps.
    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        if (!ReferenceEquals(_collider.Entity, Entity))
        {
            throw new InvalidOperationException(
                "A KinematicBody2D's collider must be attached to the same entity before that entity joins a scene.");
        }

        _scene = Entity!.Scene;
        _blocksOnFilter = Collider2D.ResolveFilter(_scene.Collision, CollectionsMarshal.AsSpan(_blocksOn));
        MovedByFilter = Collider2D.ResolveFilter(_scene.Collision, CollectionsMarshal.AsSpan(_movedBy));
        Filter = _blocksOnFilter | MovedByFilter;
        _scene.CountMovedBy(MovedByFilter, 1);
        _collider.Body = this;
    }

    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        Unride();
        _scene?.CountMovedBy(MovedByFilter, -1);
        _scene = null;
        if (ReferenceEquals(_collider.Body, this))
        {
            _collider.Body = null;
        }

        Filter = CollisionFilter.None;
        MovedByFilter = CollisionFilter.None;
        _blocksOnFilter = CollisionFilter.None;
        _dropThrough = false;
        _moveContactCount = 0;
        IsOnFloor = false;
        IsOnWall = false;
        IsOnCeiling = false;
        FloorNormal = Vector2.Zero;
        WallNormal = Vector2.Zero;
    }

    // A hit landing at the end of a translation is recorded but stopped nothing, so only a contact of a
    // pass that was stopped classifies.
    private void Classify()
    {
        Collider2D? ridden = null;
        IsOnFloor = false;
        IsOnWall = false;
        IsOnCeiling = false;
        FloorNormal = Vector2.Zero;
        WallNormal = Vector2.Zero;

        for (int index = 0; index < _moveContactCount; index++)
        {
            if (!_stopped[index])
            {
                continue;
            }

            Vector2 normal = _moveContacts[index].Normal;

            if (IsFloor(normal))
            {
                if (!IsOnFloor)
                {
                    IsOnFloor = true;
                    FloorNormal = normal;
                }

                // Grid cells never carry, because a grid is anchored.
                if (ridden is null
                    && _moveContacts[index].OtherCollider is { } other
                    && MovedByFilter.Admits(_moveContacts[index].Layer))
                {
                    ridden = other;
                }
            }
            else if (IsCeiling(normal))
            {
                IsOnCeiling = true;
            }
            else if (!IsOnWall)
            {
                IsOnWall = true;
                WallNormal = normal;
            }
        }

        Ride(ridden);
    }
}
