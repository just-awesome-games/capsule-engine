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
    /// The filter built from <see cref="BlocksOn"/> for the current scene's collision world. Reads
    /// <see cref="CollisionFilter.None"/> while this component is in no scene.
    /// </summary>
    public CollisionFilter Filter { get; private set; }

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
            Filter = filter;
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
        entity.Position += result.Translation;
        _moveContactCount = Collider2D.Describe(
            world,
            _found.AsSpan(0, result.ContactCount),
            ref _moveContacts);

        Classify(result);

        return result;
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

        Filter = Collider2D.ResolveFilter(Entity!.Scene.Collision, CollectionsMarshal.AsSpan(_blocksOn));
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

    // A hit landing at the end of a translation is recorded but stopped nothing, so it counts as
    // blocking only when its own sweep was blocked. The span holds the X sweep's contacts followed by
    // the Y sweep's, and each range is judged by its own axis flag.
    private void Classify(in MoveResult2D result)
    {
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
