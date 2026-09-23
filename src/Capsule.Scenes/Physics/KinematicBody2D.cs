using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Physics;

/// <summary>
/// Sweeps one selected <see cref="Collider2D"/> through the scene's collision world, stopping and
/// sliding against an independently configured set of blocking layers, and reports which way it was
/// stopped. The caller owns velocity, acceleration and every gameplay response. This component
/// applies no force. One body per entity, because two would each write the entity's position.
/// <para>
/// <see cref="IsOnFloor"/>, <see cref="IsOnWall"/> and <see cref="IsOnCeiling"/> describe the last
/// <see cref="Move(Vector2)"/> only. A move that pressed into nothing clears them, and a zero
/// translation clears them too.
/// </para>
/// </summary>
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
    // Y-down, so up is negative Y. Make this a property when a consumer needs to flip gravity.
    private static readonly Vector2 Up = new(0f, -1f);

    // A normal within 45 degrees of up is a floor, within 45 degrees of down a ceiling, and anything
    // between is a wall. The axis-separated sweep resolves no motion along a slope, so there is no
    // floor-angle setting to expose.
    private const float FloorDot = 0.7071f;

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
    }

    /// <summary>
    /// The collider whose shape this body sweeps. Sweeping requires it to be enabled, registered in a
    /// scene, and still attached to this body's entity. Otherwise every move and test throws.
    /// </summary>
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

    /// <summary>Whether the last move was stopped by a floor, meaning a blocking contact whose normal points up.</summary>
    public bool IsOnFloor { get; private set; }

    /// <summary>Whether the last move was stopped by a wall, meaning a blocking contact whose normal is neither floor nor ceiling.</summary>
    public bool IsOnWall { get; private set; }

    /// <summary>Whether the last move was stopped by a ceiling, meaning a blocking contact whose normal points down.</summary>
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
    /// Attempts <paramref name="translation"/> and returns the part that was applied. One call adds the
    /// resolved translation to the entity's position, replaces <see cref="MoveContacts"/>, and sets
    /// <see cref="IsOnFloor"/>, <see cref="IsOnWall"/>, <see cref="IsOnCeiling"/>,
    /// <see cref="FloorNormal"/> and <see cref="WallNormal"/>. Velocity and force stay the caller's.
    /// </summary>
    public MoveResult2D Move(Vector2 translation) => MoveWith(translation, Filter);

    /// <summary>
    /// Attempts <paramref name="translation"/> against <paramref name="blocking"/> instead of
    /// <see cref="Filter"/>, for this call only. <see cref="CollisionFilter.None"/> stops on nothing.
    /// This does not change <see cref="BlocksOn"/>, so the next plain <see cref="Move(Vector2)"/> uses
    /// the stored filter again.
    /// </summary>
    public MoveResult2D Move(Vector2 translation, CollisionFilter blocking) => MoveWith(translation, blocking);

    /// <summary>
    /// Reports whether <see cref="Move(Vector2)"/> of <paramref name="translation"/> would be stopped
    /// short. It sweeps the body's own collider from its current place, using <see cref="Filter"/> and
    /// never hitting itself. Nothing moves and nothing is written, including <see cref="IsOnFloor"/>
    /// and its peers. The axes sweep independently, as in a real move, so the translation is blocked
    /// when either axis is blocked. An axis with no travel blocks nothing, and a surface reached exactly
    /// at the end of the translation does not count as a block.
    /// </summary>
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

        // Pass an empty contact span, because only the blocked flags matter here and this query does not
        // report what the sweep touched.
        MoveResult2D result = world.Move(
            world.ShapeOf(_collider.Handle),
            entity.WorldPosition + from,
            translation,
            Filter,
            default,
            _collider.Handle);

        return result.BlockedX || result.BlockedY;
    }

    private MoveResult2D MoveWith(Vector2 translation, CollisionFilter blocking)
    {
        CollisionWorld2D world = RequireSweepable(out Entity entity);
        Shape2D shape = world.ShapeOf(_collider.Handle);

        Vector2 origin = entity.WorldPosition;
        MoveResult2D result = world.Move(
            shape,
            origin,
            translation,
            blocking,
            _found,
            _collider.Handle);

        if (result.ContactCount > _found.Length)
        {
            Array.Resize(ref _found, result.ContactCount);
            result = world.Move(
                shape,
                origin,
                translation,
                blocking,
                _found,
                _collider.Handle);
        }

        // A world translation equals a local one here, because nothing above a body is turned or scaled.
        Displace(entity, result.Translation);
        _moveContactCount = Collider2D.Describe(
            world,
            _found.AsSpan(0, result.ContactCount),
            ref _moveContacts);

        Classify(result);

        return result;
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
        _moveContactCount = 0;
        IsOnFloor = false;
        IsOnWall = false;
        IsOnCeiling = false;
        FloorNormal = Vector2.Zero;
        WallNormal = Vector2.Zero;
    }

    // A hit landing at the end of a translation is recorded but stopped nothing, so it counts as
    // blocking only when its own sweep was blocked. The span holds the X sweep's contacts followed by
    // the Y sweep's, and each range is judged by its own axis flag.
    private void Classify(in MoveResult2D result)
    {
        Collider2D? ridden = null;
        IsOnFloor = false;
        IsOnWall = false;
        IsOnCeiling = false;
        FloorNormal = Vector2.Zero;
        WallNormal = Vector2.Zero;

        for (int index = 0; index < _moveContactCount; index++)
        {
            if (!(index < result.ContactsAlongX ? result.BlockedX : result.BlockedY))
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

                // Grid cells never carry, because a grid is anchored.
                if (ridden is null
                    && _moveContacts[index].OtherCollider is { } other
                    && MovedByFilter.Admits(_moveContacts[index].Layer))
                {
                    ridden = other;
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

        Ride(ridden);
    }
}
