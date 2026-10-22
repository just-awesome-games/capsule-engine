using System.Numerics;
using System.Runtime.CompilerServices;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.Tiles;

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
/// _body = new KinematicBody2D(bodyCollider) { BlockedBy = CollisionLayers.Blocking };
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
public sealed partial class KinematicBody2D : Component
{
    // Y-down. Up is negative Y. A static readonly field would cost a class-initialisation check at
    // every use without tiered compilation. This form compiles to a constant.
    private static Vector2 Up => new(0f, -1f);

    // How far a normal's cosine to up may fall short of MaxFloorAngle's and still count. An exact
    // 45 degree edge then reads as a floor at the default.
    private const float AngleTolerance = 1e-4f;

    private const float DegreesToRadians = MathF.PI / 180f;

    // The least share of a walk's direction that runs across up. Only a MaxFloorAngle within a hair of
    // 90 admits a floor this steep, and the walk along it grows no longer.
    private const float MinAcross = 1e-3f;

    // The largest X a floor normal may have and still count as flat.
    private const float FlatX = 1e-4f;

    // How far below 1 the cosine between two floor normals may fall and the floors still count as one plane.
    private const float PlaneTolerance = 1e-5f;

    // How far to either side of where two floors' lines meet the join test looks for each floor. It stays
    // clear of the corner itself, where a ray may report either floor.
    private const float JoinProbe = 4f * CollisionTolerance.ContactSkin;

    private float _maxFloorAngle;

    // The cosine of MaxFloorAngle less AngleTolerance, and its tangent, taken once when it is set.
    private float _floorCos;
    private float _floorTan;

    // Set by DropThrough and consumed by the next Move.
    private bool _dropThrough;

    // Whether Move(Vector2) has run, which holds the empty-mask warning to the first move.
    private bool _hasMoved;

    private BodyMode _mode;

    private float _stepHeight;

    /// <summary>
    /// Whether a <see cref="BodyMode.Grounded"/> walk on a slope covers the move's whole X horizontally,
    /// true by default.
    /// </summary>
    /// <remarks>
    /// The slope sets the rise. When this is false, the walk covers the move's X along the slope's surface
    /// instead. A steeper slope then gives less horizontal headway.
    /// </remarks>
    public bool KeepsHorizontalSpeedOnSlopes { get; set; } = true;

    /// <summary>
    /// Whether a <see cref="BodyMode.Grounded"/> body on an uneven floor stands with its bottom center on
    /// the floor, where false, the default, stands it on the first part of its bottom to meet the floor.
    /// </summary>
    /// <remarks>
    /// The body still sweeps the pose it would hold with this off, and the entity stands up to half the
    /// collider's width times the tangent of <see cref="MaxFloorAngle"/> below that pose. Walls, ceilings,
    /// <see cref="TestMove(Vector2)"/>, carries and shoves meet the swept pose. <see cref="FloorNormal"/>
    /// reports the floor under the center, and the translation a move returns includes any change in the
    /// sink. Setting the entity's position directly clears the sink.
    /// <para>
    /// The entity never sinks over the edge of a ledge or a one-way floor. It keeps its height at a ledge
    /// until its whole box has left it, and stands on a ledge it steps up onto at once. A crest followed
    /// within half a width by a step, or a short rise between two flat floors, can leave it a little high
    /// or low, but never inside a wall or below the floor under its center. A round collider walking
    /// downhill stops slightly short of a steep wall.
    /// </para>
    /// </remarks>
    public bool RestsOnCenter { get; set; }

    private readonly Collider2D _collider;
    private CollisionMask _blockedBy = CollisionMask.Empty;
    private CollisionMask _movedBy = CollisionMask.Empty;

    private Scene? _scene;

    // The interned index of its collider's layer in the current scene's world, which the scene counts
    // among its body layers.
    private int _layerIndex;

    // BlockedBy resolved in the current scene's world. Filter adds the MovedBy layers to it.
    private CollisionFilter _blockedByFilter;

    // The collider this body rides, as found by its last Move. Only a Move changes it.
    private Collider2D? _floor;

    // The cell this body rides, as found by its last Move, and the tile it held then. Only a tile with a
    // surface velocity is ridden. The ride ends when the cell holds another tile.
    private TileContact2D? _floorCell;
    private TileType? _floorTile;

    // The map collider that lists this body while it rides one of the map's cells.
    private TileMapCollider2D? _floorGrid;

    // What the ridden floor carried this body by during the step Scene.StepsBegun counted as _carriedStep,
    // before any wall stopped it, and during the step before that one. A new floor clears both.
    private Vector2 _carriedMotion;
    private Vector2 _priorMotion;
    private int _carriedStep;

    // The tick of the step that last surface-carried this body in its current scene. A later surface in the
    // same step skips it.
    private long _surfaceTick = -1;

    // True while this body writes its own position. A collider that moves because of that write, such
    // as one on a child entity, cannot carry or shove the body again.
    private bool _moving;

    // Whether the last carry took the floor's whole motion. The floor's shove then passes the body by.
    // The body's own move clears it, and only that move finds a new floor.
    private bool _carriedWhole;

    private Contact2D[] _found = new Contact2D[16];

    // Whether the contact at the same index of _found belongs to a pass that stopped the move.
    private bool[] _stopped = new bool[16];
    private ColliderContact2D[] _moveContacts = new ColliderContact2D[16];
    private int _moveContactCount;

    // How far RestsOnCenter holds the entity below the pose the body sweeps, from 0 up to the allowance.
    private float _sink;

    // The side of the center, -1 left or 1 right, where the swept pose rests on its floor. The other half
    // of the entity's box hangs lower. Zero when neither side is known.
    private float _restSide;

    // The entity's local position as this body last wrote it. Any other position was set by the game.
    private Vector2 _written;

    // The swept pose's floor normal from the last Move, which the next walk follows. FloorNormal reports
    // the floor under the center instead while the body rests on it.
    private Vector2 _walkNormal;

    // The layers that stop this body, BlockedBy and MovedBy resolved in the current scene's world, or
    // None in no scene.
    internal CollisionFilter Filter { get; private set; }

    // The MovedBy layers resolved in the current scene's world, or None in no scene.
    internal CollisionFilter MovedByFilter { get; private set; }

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

    /// <summary>How a move meets what it hits. <see cref="BodyMode.Grounded"/> by default.</summary>
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
            _floorCos = DeterministicMath.Cos(value * DegreesToRadians) - AngleTolerance;
            _floorTan = DeterministicMath.Tan(value * DegreesToRadians);
        }
    }

    /// <summary>
    /// How tall a lip a <see cref="BodyMode.Grounded"/> body on a floor steps up onto and how deep a drop it
    /// walks down without leaving the floor, in world units, where 0, the default, turns stepping off.
    /// </summary>
    /// <remarks>
    /// A body whose last move ended on a floor and whose walk meets a wall rises, walks on and settles
    /// onto a floor no lower than where it started. Without headroom or such a floor, the wall stops it
    /// as usual. A walk that leaves its floor stays on one up to this far below where the walk ended. A
    /// move that rises or follows <see cref="DropThrough"/> never steps. A body steps onto a one-way
    /// surface only where that surface blocks it.
    /// </remarks>
    public float StepHeight
    {
        get => _stepHeight;
        set
        {
            if (!(value >= 0f && float.IsFinite(value)))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "StepHeight must be finite and at least 0. Use 0 to turn stepping off.");
            }

            _stepHeight = value;
        }
    }

    /// <summary>The collider whose shape this body sweeps.</summary>
    /// <remarks>
    /// Sweeping requires it to be enabled, registered in a scene, and still attached to this body's
    /// entity. Otherwise every move and test throws.
    /// </remarks>
    public Collider2D Collider => _collider;

    /// <summary>The surfaces the most recent <see cref="Move(Vector2)"/> reached.</summary>
    /// <remarks>
    /// Each sweep of the move reaches the nearest surface and every other within a ten-thousandth of the
    /// sweep's length of it.
    /// </remarks>
    public ReadOnlySpan<ColliderContact2D> MoveContacts => _moveContacts.AsSpan(0, _moveContactCount);

    /// <summary>
    /// The velocity, in units per second, at which the floor this body rides carried it over the last finished
    /// step, or zero when it rides none.
    /// </summary>
    /// <remarks>
    /// It counts every carry of that step, the floor's own motion and its surface velocity, whether or not a
    /// wall stopped it. A read during a step sees the step before, whichever of the floor and the body steps
    /// first. A floor that held still through that step reads zero. Only a floor on a <see cref="MovedBy"/>
    /// layer is ridden. The move that leaves the floor ends the ride. A controller reads this before that move
    /// and keeps what it chooses. The body never applies it.
    /// </remarks>
    public Vector2 PlatformVelocity
    {
        get
        {
            if ((_floor is null && _floorGrid is null) || _scene is not { StepSeconds: > 0f } scene)
            {
                return Vector2.Zero;
            }

            // Between steps the last one has finished. During a step only the one before it has.
            int finished = scene.SteppingTick is null ? scene.StepsBegun : unchecked(scene.StepsBegun - 1);
            Vector2 motion = finished == _carriedStep ? _carriedMotion
                : finished == unchecked(_carriedStep - 1) ? _priorMotion
                : Vector2.Zero;

            return motion / scene.StepSeconds;
        }
    }

    /// <summary>
    /// The layers that stop this body besides its <see cref="MovedBy"/> layers, where the empty default adds none.
    /// </summary>
    /// <remarks>
    /// Blocking is separate from what the body's collider <see cref="Collider2D.Detects"/>. A
    /// collider can report an overlap that does not change movement.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public CollisionMask BlockedBy
    {
        get => _blockedBy;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            // Resolve before storing. A world with no layer slots left throws here, while the body
            // still blocks on what it did.
            CollisionFilter filter = Collider2D.ResolveFilter(_scene?.Physics.World, value);
            _blockedBy = value;
            if (InScene)
            {
                _blockedByFilter = filter;
                Filter = filter | MovedByFilter;
            }
        }
    }

    /// <summary>
    /// The layers whose moving colliders move this body, where the empty default means nothing carries or
    /// shoves it.
    /// </summary>
    /// <remarks>
    /// A body standing on such a collider rides it, and one moving into the body shoves it. A layer
    /// that moves the body also blocks it. The body rides the first such floor its last
    /// <see cref="Move(Vector2)"/> stopped on, and is carried exactly whether that floor steps before
    /// or after it. A wall or ceiling stops a carry or a shove. A collider moving into several bodies
    /// shoves them one at a time, in the slot order of their colliders.
    /// <para>
    /// A floor with a <see cref="Collider2D.SurfaceVelocity"/> or <see cref="TileType.SurfaceVelocity"/>
    /// carries the body by that velocity on every step, whether or not the body moves itself. The carry runs
    /// in the floor's own step, as a moving floor's does, and only the first such floor in tree order to
    /// carry the body moves it that step. A <see cref="BodyMode.Grounded"/> body walks its floor as its own move does. The carry never
    /// changes the caller's velocity, and a floor on a held entity carries nothing. After each carry the
    /// body rides the carrying floor it then stands on, or nothing, and <see cref="IsOnFloor"/>,
    /// <see cref="MoveContacts"/> and their peers keep what the last move set.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public CollisionMask MovedBy
    {
        get => _movedBy;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            CollisionFilter filter = Collider2D.ResolveFilter(_scene?.Physics.World, value);
            _movedBy = value;
            if (InScene)
            {
                SetMovedBy(filter);
                Filter = _blockedByFilter | filter;
            }
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
    public MoveResult2D Move(Vector2 translation)
    {
        MoveResult2D result = MoveWith(translation, Filter);
        if (!_hasMoved)
        {
            WarnIfBlockedByNothing();
        }

        return result;
    }

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
    /// <see cref="BlockedBy"/> and <see cref="MovedBy"/>, for this call only.
    /// </summary>
    /// <remarks>
    /// <see cref="CollisionFilter.None"/> stops on nothing.
    /// </remarks>
    public MoveResult2D Move(Vector2 translation, CollisionFilter blocking) => MoveWith(translation, blocking);

    /// <summary>
    /// Reports whether <see cref="Move(Vector2)"/> of <paramref name="translation"/> would be
    /// stopped short. It sweeps the body's own collider from its current place, stopped by
    /// <see cref="BlockedBy"/> and <see cref="MovedBy"/> and never hitting itself.
    /// </summary>
    /// <remarks>
    /// Nothing moves, and <see cref="IsOnFloor"/>, <see cref="MoveContacts"/> and their peers keep what
    /// the last move set. The test slides as a <see cref="BodyMode.Floating"/> move does, and the
    /// translation is blocked when any part of it is stopped short. A surface reached exactly at the end
    /// of the translation does not count as a block.
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
        MoveResult2D result = world.Move(
            world.ShapeOf(_collider.Handle),
            Swept(entity) + from,
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
        Guard.Finite(entity.WorldPosition + translation, nameof(translation));

        bool through = _dropThrough;
        _dropThrough = false;
        _carriedWhole = false;

        MoveResult2D result = Travel(world, entity, translation, blocking, through, out Vector2 centerNormal);
        _moveContactCount = result.ContactCount;
        Collider2D.Grow(ref _moveContacts, _moveContactCount);
        for (int index = 0; index < _moveContactCount; index++)
        {
            _moveContacts[index] = Collider2D.Describe(world, _found[index]);
        }

        Classify();
        _walkNormal = FloorNormal;
        if (IsOnFloor && centerNormal != Vector2.Zero)
        {
            FloorNormal = centerNormal;
        }

        return result;
    }

    // Sweeps the body, settles one that rests on its center, and writes the entity. The contacts are left
    // in _found. Returns the translation the entity moved and the center's floor normal, or zero.
    private MoveResult2D Travel(CollisionWorld2D world, Entity entity, Vector2 translation, CollisionFilter blocking, bool through, out Vector2 centerNormal)
    {
        Shape2D shape = world.ShapeOf(_collider.Handle);
        Vector2 origin = Swept(entity);
        float sunk = _sink;
        bool rests = RestsOnCenter && _mode == BodyMode.Grounded;
        bool rising = Vector2.Dot(translation, Up) > 0f;
        Vector2 swept = translation;
        if (sunk != 0f && rests)
        {
            swept = Spend(translation);
        }
        else
        {
            _sink = 0f;
        }

        MoveResult2D result = Sweep(world, shape, origin, swept, blocking, through, rising);
        centerNormal = Vector2.Zero;
        if (rests)
        {
            float cleared = 0f;
            if (_sink > 2f * CollisionTolerance.LinearSlop && IsOnFloor && !rising && !through && swept.X != 0f && MathF.Sign(swept.X) == -_restSide)
            {
                result = KeepClear(world, shape, origin, swept, blocking, result);
                cleared = _sink;
            }

            centerNormal = Settle(world, shape, origin + result.Translation, blocking, origin.Y + sunk + shape.Bounds.Max.Y, result, cleared);
            _scene!.Physics.NoteSink(_sink);
        }

        // A world translation equals a local one here, because nothing above a body is turned or scaled.
        Vector2 moved = result.Translation + new Vector2(0f, _sink - sunk);
        Displace(entity, moved);

        return result with { Translation = moved };
    }

    // Resolves the move, and runs it again with room for every contact when the span overflowed.
    private MoveResult2D Sweep(CollisionWorld2D world, in Shape2D shape, Vector2 origin, Vector2 translation, CollisionFilter blocking, bool through, bool rising)
    {
        MoveResult2D result = Resolve(world, shape, origin, translation, blocking, through, rising);
        if (result.ContactCount > _found.Length)
        {
            Collider2D.Grow(ref _found, result.ContactCount);
            Collider2D.Grow(ref _stopped, result.ContactCount);
            result = Resolve(world, shape, origin, translation, blocking, through, rising);
        }

        return result;
    }

    // Runs the whole move against the world without writing the body. It is pure. An overflowing
    // contact span can be grown and the move run again. Kept out of line, because each inlined copy
    // would add a sweep that MoveWith's frame zeroes on every move.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private MoveResult2D Resolve(CollisionWorld2D world, in Shape2D shape, Vector2 origin, Vector2 translation, CollisionFilter blocking, bool through, bool rising)
    {
        MoveSweep sweep = new(world, shape, origin, blocking, _collider.Handle, through, _found, _stopped);

        if (_mode == BodyMode.Floating)
        {
            sweep.Slide(translation);
        }
        else
        {
            Walk(ref sweep, translation, through, rising);
        }

        return sweep.Result;
    }

    // The grounded move. The part across up walks along the floor, the part along up falls onto floors
    // and rises into ceilings, and a body that stood on a floor follows it down. A rising move neither
    // steps nor follows the floor down, even when the sink took its whole rise.
    private void Walk(ref MoveSweep sweep, Vector2 translation, bool through, bool rising)
    {
        float rise = Vector2.Dot(translation, Up);

        // Lateral is horizontal while up is -Y. Its length and direction need no square root.
        float length = MathF.Abs(translation.X);

        if (length > 0f)
        {
            Vector2 direction = new(MathF.Sign(translation.X), 0f);
            // A lateral direction already runs along a flat floor.
            if (IsOnFloor && _walkNormal != Up)
            {
                direction = Tangent(direction, _walkNormal);
            }

            bool steps = _stepHeight > 0f && IsOnFloor && !rising && !through;
            Across(ref sweep, direction, length, steps);
        }

        float walked = sweep.At.Y;

        if (rise != 0f)
        {
            Vector2 remaining = Up * rise;
            for (int pass = 0; pass < MoveSweep.MaxPasses && remaining != Vector2.Zero; pass++)
            {
                MovePass step = sweep.Pass(remaining);

                // A floor stops a fall outright. A body resting on a slope never slides down it.
                if (!step.Blocked || (rise < 0f ? IsFloor(step.Normal) : IsCeiling(step.Normal)))
                {
                    break;
                }

                remaining = MoveSweep.AlongSurface(remaining - step.Moved, step.Normal);
            }
        }

        // The farthest a walk can leave a floor it followed is the distance it covers across up times the
        // steepest floor's slope, over a crest or off a step. Twice that covers a crest into a descent.
        // Neither setting covers more than `length` across.
        if (IsOnFloor && !rising && !through && !StoppedOnFloor(sweep))
        {
            float reach = (2f * length * _floorTan) + CollisionTolerance.ContactSkin;
            if (!sweep.Snap(-Up * reach, _floorCos) && _stepHeight > 0f)
            {
                // A step down reaches StepHeight below where the walk ended, less what the fall covered.
                float down = walked + _stepHeight + CollisionTolerance.ContactSkin - sweep.At.Y;
                if (down > reach)
                {
                    sweep.Snap(-Up * down, _floorCos);
                }
            }
        }
    }

    // Walks `left` on from `direction`, turning along each walkable surface it meets. The walk spends
    // `left` across up, or along the floor when KeepsHorizontalSpeedOnSlopes is off. A wall ends it, or
    // is stepped over when `steps` allows.
    private void Across(ref MoveSweep sweep, Vector2 direction, float left, bool steps)
    {
        // A floor turns the walk along it from the heading, not from the direction that met it. A
        // direction running straight into the far face of a valley would otherwise leave none.
        Vector2 heading = new(MathF.Sign(direction.X), 0f);
        int turnedFrom = 0;
        int turnedTo = 0;
        for (int pass = 0; pass < MoveSweep.MaxPasses && left > 0f && direction != Vector2.Zero; pass++)
        {
            // Across up, a unit direction along a floor covers the X share of its length.
            float across = KeepsHorizontalSpeedOnSlopes ? MathF.Max(MathF.Abs(direction.X), MinAcross) : 1f;
            MovePass step = sweep.Pass(direction * (left / across));

            // A floor the walk turned along and then moved on from no longer holds the body. A crest's
            // vertex is met flat and walked past, and the floor beyond it is left for the snap to find.
            if (step.Moved != Vector2.Zero)
            {
                _stopped.AsSpan(turnedFrom, turnedTo - turnedFrom).Clear();
            }

            if (!step.Blocked)
            {
                break;
            }

            // A walkable surface turns the rest of the walk along it. Anything steeper stops the walk.
            left -= KeepsHorizontalSpeedOnSlopes ? MathF.Abs(step.Moved.X) : step.Moved.Length();
            if (IsFloor(step.Normal))
            {
                direction = Tangent(heading, step.Normal);
                turnedFrom = sweep.Written - step.Written;
                turnedTo = sweep.Written;
                continue;
            }

            if (steps && left > CollisionTolerance.LinearSlop)
            {
                StepUp(ref sweep, step, left, heading.X);
            }

            break;
        }
    }

    // Climbs the wall that stopped `wall`. The body rises by up to StepHeight, walks on with what is left
    // and settles onto a floor no lower than where the rise began. A climb that fails any of these puts
    // the move back where the wall stopped it. Every leg is a sweep. The body then never ends inside
    // anything it blocks on. It is kept out of line to keep the saved sweep out of every walk's frame.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StepUp(ref MoveSweep sweep, in MovePass wall, float left, float way)
    {
        // A face turned down is an overhang. A wall met only above the step's reach is taller than the
        // step. Neither needs a sweep to rule out.
        int from = sweep.Written - wall.Written;
        if (IsCeiling(wall.Normal) || (wall.Written > 0 && LowestPoint(from, sweep.Written) < sweep.Bottom - _stepHeight - CollisionTolerance.LinearSlop))
        {
            return;
        }

        MoveSweep blocked = sweep;
        float lifted = -sweep.Pass(Up * _stepHeight).Moved.Y;
        int risen = sweep.Written;
        float start = sweep.At.X;

        // The walk after the rise only climbs. Settling by the rise then lands no lower than the start.
        if (lifted > CollisionTolerance.LinearSlop)
        {
            Across(ref sweep, new Vector2(way, 0f), left, false);
            int landed = sweep.Written;
            if (MathF.Abs(sweep.At.X - start) > CollisionTolerance.LinearSlop
                && sweep.Snap(-Up * (lifted + CollisionTolerance.ContactSkin), _floorCos))
            {
                Supersede(from, risen, landed);
                return;
            }
        }

        sweep = blocked;
    }

    // Leaves the landing as the only floor that stopped a step. The wall and anything the rise touched
    // stopped nothing in the end, and neither did a floor walked before the landing.
    private void Supersede(int from, int risen, int landed)
    {
        for (int index = 0; index < landed; index++)
        {
            if ((index >= from && index < risen) || IsFloor(_found[index].Normal))
            {
                _stopped[index] = false;
            }
        }
    }

    // The largest Y, the lowest point, among the contacts found from `from` up to `to`.
    private float LowestPoint(int from, int to)
    {
        float lowest = float.NegativeInfinity;
        for (int index = from; index < to; index++)
        {
            lowest = MathF.Max(lowest, _found[index].Point.Y);
        }

        return lowest;
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

    /// <summary>
    /// What this body calls a surface with <paramref name="normal"/>, by the same rule that sets
    /// <see cref="IsOnFloor"/>, <see cref="IsOnWall"/> and <see cref="IsOnCeiling"/>.
    /// </summary>
    /// <remarks>
    /// The rule reads <see cref="MaxFloorAngle"/> as it is now. A surface exactly at that angle is a
    /// floor, or a ceiling seen from below.
    /// </remarks>
    /// <param name="normal">A unit surface normal, pointing out of the surface.</param>
    /// <example>
    /// A shape cast finds a surface the body has not touched yet, and the body judges it the way a move would:
    /// <code>
    /// if (_collider.Cast(new Vector2(reach, 0f), out ShapeCastHit2D hit)
    ///     &amp;&amp; _body.ClassifyNormal(hit.Normal) == SurfaceKind.Wall)
    /// {
    ///     // A wall is within reach. A slope is not one.
    /// }
    /// </code>
    /// </example>
    public SurfaceKind ClassifyNormal(Vector2 normal)
    {
        if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(normal), normal, "The normal must be finite. Pass a hit's or contact's Normal.");
        }

        return KindOf(normal);
    }

    private SurfaceKind KindOf(Vector2 normal) =>
        IsFloor(normal) ? SurfaceKind.Floor : IsCeiling(normal) ? SurfaceKind.Ceiling : SurfaceKind.Wall;

    private bool IsFloor(Vector2 normal) => Vector2.Dot(normal, Up) >= _floorCos;

    private bool IsCeiling(Vector2 normal) => -Vector2.Dot(normal, Up) >= _floorCos;

    // The unit direction along a surface that keeps to the way `direction` was heading, or zero when
    // the direction runs straight into it.
    private static Vector2 Tangent(Vector2 direction, Vector2 normal)
    {
        Vector2 along = MoveSweep.AlongSurface(direction, normal);
        float length = along.Length();

        return length > 1e-6f ? along / length : Vector2.Zero;
    }

    // Whether this body is moved by the layer with this interned index.
    internal bool IsMovedBy(int layerIndex) => (MovedByFilter.Bits & (1UL << layerIndex)) != 0;

    // Recounts this body under its collider's layer, interned in scene. A collider moved to another
    // scene changes nothing here.
    internal void FollowLayer(Scene scene, int layerIndex)
    {
        if (!ReferenceEquals(scene, _scene) || layerIndex == _layerIndex)
        {
            return;
        }

        scene.Physics.CountBodyLayer(_layerIndex, -1);
        _layerIndex = layerIndex;
        scene.Physics.CountBodyLayer(_layerIndex, 1);
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
            _written = entity.Position;
        }
        finally
        {
            _moving = false;
        }
    }

    // Masks are often set after the body joins its scene. The first move is the earliest honest check.
    private void WarnIfBlockedByNothing()
    {
        _hasMoved = true;
        if (_blockedBy.Names.IsEmpty && _movedBy.Names.IsEmpty)
        {
            Log.Warning(
                $"The KinematicBody2D on a {Entity!.GetType().Name} has an empty BlockedBy, and nothing stops it. "
                + "Set BlockedBy to the layers that should");
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
                "A KinematicBody2D sweeps only once its collider is enabled and its entity has joined a started scene. Sweep from OnStart onward, not from a constructor.");
    }

    internal override bool Steps => false;

    // The sweep moves the entity along the world axes. It supports position only.
    internal override TransformSupport Supports => TransformSupport.Position;

    // One body owns the entity's position write. Attaching a second body throws.
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
        panel.Field("PlatformVelocity", PlatformVelocity);
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
        _blockedByFilter = Collider2D.ResolveFilter(_scene.Physics.World, _blockedBy);
        MovedByFilter = Collider2D.ResolveFilter(_scene.Physics.World, _movedBy);
        Filter = _blockedByFilter | MovedByFilter;
        _scene.Physics.CountMovedBy(MovedByFilter, 1);

        // Interned by name, as the collider may join the scene after this body.
        _layerIndex = _scene.Physics.World.Layer(_collider.Layer).Index;
        _scene.Physics.CountBodyLayer(_layerIndex, 1);
        _collider.Body = this;
    }

    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        // Classifying no contacts clears the flags and normals and ends the ride.
        _moveContactCount = 0;
        Classify();
        _scene?.Physics.CountMovedBy(MovedByFilter, -1);
        _scene?.Physics.CountBodyLayer(_layerIndex, -1);
        _scene = null;

        // A new scene's host may count its ticks from zero again.
        _surfaceTick = -1;
        if (ReferenceEquals(_collider.Body, this))
        {
            _collider.Body = null;
        }

        Filter = CollisionFilter.None;
        MovedByFilter = CollisionFilter.None;
        _blockedByFilter = CollisionFilter.None;
        _dropThrough = false;
        _sink = 0f;
        _restSide = 0f;
        _walkNormal = Vector2.Zero;
    }

    // A hit landing at the end of a translation is recorded but stopped nothing. Only a contact of a
    // pass that was stopped classifies.
    private void Classify()
    {
        Collider2D? ridden = null;
        TileContact2D? riddenCell = null;
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
            SurfaceKind kind = KindOf(normal);

            if (kind == SurfaceKind.Floor)
            {
                if (!IsOnFloor)
                {
                    IsOnFloor = true;
                    FloorNormal = normal;
                }

                // A grid is anchored. A cell carries only by its tile's surface velocity.
                ref readonly ColliderContact2D contact = ref _moveContacts[index];
                if (ridden is null && riddenCell is null && MovedByFilter.Admits(contact.Layer) && Carries(contact))
                {
                    ridden = contact.OtherCollider;
                    riddenCell = contact.Tile;
                }
            }
            else if (kind == SurfaceKind.Ceiling)
            {
                IsOnCeiling = true;
            }
            else if (!IsOnWall)
            {
                IsOnWall = true;
                WallNormal = normal;
            }
        }

        Ride(ridden, riddenCell);
    }

    // Whether a floor contact can carry this body: any collider, or a cell whose tile has a surface velocity.
    private static bool Carries(in ColliderContact2D contact) =>
        contact.OtherCollider is not null || (contact.Tile is { } cell && cell.Type.SurfaceVelocity != Vector2.Zero);
}
