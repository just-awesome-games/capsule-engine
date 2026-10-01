using System.Numerics;
using System.Runtime.CompilerServices;
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
public sealed class KinematicBody2D : Component
{
    // Y-down, so up is negative Y. A static readonly field would cost a class-initialisation check at
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
    /// <see cref="TestMove(Vector2)"/>, carries and shoves meet the swept pose exactly as they would with
    /// this off. A flat floor leaves the entity where it would be anyway.
    /// <para>
    /// At a ledge the body keeps its height until its whole box has left the ledge. A rise spends the
    /// sink before the box leaves its pose, and the translation a move returns includes any change in
    /// the sink. <see cref="FloorNormal"/> reports the floor under the center. The body never sinks over
    /// the edge of a ledge or a one-way floor, and a body that steps up onto a ledge stands on it at once.
    /// Setting the entity's position directly clears the sink, and the next grounded move finds it again.
    /// </para>
    /// <para>
    /// A grounded move casts one ray down from the bottom center. A move over a change of slope casts a
    /// second ray, and a move downhill casts the hanging half of the box ahead for walls. A move that
    /// lowers the entity further below the swept pose sweeps that half for walls first. A change of
    /// slope on a one-way floor casts up to three more rays down.
    /// </para>
    /// <para>
    /// A crest followed within half a width by a step, or a short rise between two flat floors, can
    /// leave the body a little high or low. It never ends inside a wall or below the floor under its
    /// center. A round collider walking downhill stops slightly short of a steep wall.
    /// </para>
    /// </remarks>
    public bool RestsOnCenter { get; set; }

    private readonly Collider2D _collider;
    private CollisionMask _blockedBy = CollisionMask.Empty;
    private CollisionMask _movedBy = CollisionMask.Empty;

    private Scene? _scene;

    // BlockedBy resolved in the current scene's world. Filter adds the MovedBy layers to it.
    private CollisionFilter _blockedByFilter;

    // The collider this body rides, as found by its last Move. Only a Move changes it.
    private Collider2D? _floor;

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

    /// <summary>The surfaces the most recent <see cref="Move(Vector2)"/> reached.</summary>
    /// <remarks>
    /// Each sweep of the move reaches the nearest surface and every other within a ten-thousandth of the
    /// sweep's length of it.
    /// </remarks>
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
            CollisionFilter filter = Collider2D.ResolveFilter(Entity?.SceneOrNull?.Collision, value);
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
    /// that moves the body also blocks it. The body rides the floor its last
    /// <see cref="Move(Vector2)"/> stopped on, and is carried exactly whether that floor steps before
    /// or after it. A wall or ceiling stops a carry or a shove. A collider moving into several bodies
    /// shoves them one at a time, in the order of their colliders' handles.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public CollisionMask MovedBy
    {
        get => _movedBy;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            CollisionFilter filter = Collider2D.ResolveFilter(Entity?.SceneOrNull?.Collision, value);
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
        Shape2D shape = world.ShapeOf(_collider.Handle);
        Vector2 origin = Swept(entity);
        Guard.Finite(entity.WorldPosition + translation, nameof(translation));

        bool through = _dropThrough;
        _dropThrough = false;
        _carriedWhole = false;

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
        Vector2 centerNormal = Vector2.Zero;
        if (rests)
        {
            float cleared = 0f;
            if (_sink > 2f * CollisionTolerance.LinearSlop && IsOnFloor && !rising && !through && swept.X != 0f && MathF.Sign(swept.X) == -_restSide)
            {
                result = KeepClear(world, shape, origin, swept, blocking, result);
                cleared = _sink;
            }

            centerNormal = Settle(world, shape, origin + result.Translation, blocking, origin.Y + sunk + shape.Bounds.Max.Y, result, cleared);
            _scene!.NoteSink(_sink);
        }

        // A world translation equals a local one here, because nothing above a body is turned or scaled.
        Vector2 moved = result.Translation + new Vector2(0f, _sink - sunk);
        Displace(entity, moved);
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

    // Runs the whole move against the world without writing the body. It is pure, so an overflowing
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

        // Lateral is horizontal while up is -Y, so its length and direction need no square root.
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

                // A floor stops a fall outright, so a body resting on a slope never slides down it.
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

    // Where the body's sweeps start, the entity's place raised by the sink. A position the game set
    // itself clears the sink.
    private Vector2 Swept(Entity entity)
    {
        if (_sink == 0f)
        {
            return entity.WorldPosition;
        }

        if (entity.Position != _written)
        {
            _sink = 0f;
            return entity.WorldPosition;
        }

        return entity.WorldPosition - new Vector2(0f, _sink);
    }

    // Takes the sink out of a move that leaves the floor. A rise lifts the entity through the sink
    // before the swept pose moves. A fall that started in the air lowers the swept pose through it
    // while the entity falls as asked.
    private Vector2 Spend(Vector2 translation)
    {
        if (translation.Y < 0f)
        {
            float spent = MathF.Min(-translation.Y, _sink);
            _sink -= spent;
            translation.Y += spent;
        }
        else if (translation.Y > 0f && !IsOnFloor)
        {
            translation.Y += _sink;
            _sink = 0f;
        }

        return translation;
    }

    // Casts the entity's hanging half, the strip below the swept pose on its downhill side, along the
    // walk. A wall it meets cuts the walk short and stops the move as that wall would stop the box.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private MoveResult2D KeepClear(CollisionWorld2D world, in Shape2D shape, Vector2 origin, Vector2 swept, CollisionFilter blocking, MoveResult2D result)
    {
        Aabb2D bounds = shape.Bounds;
        float center = origin.X + ((bounds.Min.X + bounds.Max.X) * 0.5f);
        float top = origin.Y + bounds.Max.Y;
        float bottom = origin.Y + bounds.Max.Y + _sink;
        Aabb2D strip = _restSide < 0f
            ? new Aabb2D(new Vector2(center + CollisionTolerance.LinearSlop, top), new Vector2(origin.X + bounds.Max.X, bottom))
            : new Aabb2D(new Vector2(origin.X + bounds.Min.X, top), new Vector2(center - CollisionTolerance.LinearSlop, bottom));

        if (!world.ShapeCast(Shape2D.Box(strip), Vector2.Zero, result.Translation, blocking, out ShapeCastHit2D wall, _collider.Handle)
            || IsFloor(wall.Normal))
        {
            return result;
        }

        float reach = MathF.Max(0f, (MathF.Abs(result.Translation.X) * wall.Fraction) - CollisionTolerance.LinearSlop);
        result = Sweep(world, shape, origin, new Vector2(MathF.Sign(swept.X) * reach, swept.Y), blocking, false, false);

        int count = result.ContactCount;
        if (count == _found.Length)
        {
            Collider2D.Grow(ref _found, count + 1);
            Collider2D.Grow(ref _stopped, count + 1);
        }

        _found[count] = new Contact2D(wall.Target, wall.Point, wall.Normal, 0f);
        _stopped[count] = true;

        return new MoveResult2D(result.Translation, true, count + 1);
    }

    // Sets the sink after a move that ended on a floor with the swept pose at `pose`. The entity's bottom
    // center goes onto the floor under the center when that floor joins the one the pose rests on and
    // the hanging half meets no wall. Otherwise the entity keeps its `previous` bottom, held within the
    // allowance and the depth the move has already kept clear. KeepClear kept `cleared` clear along the
    // move. Returns the center's floor normal, or zero when the body holds its height.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vector2 Settle(CollisionWorld2D world, in Shape2D shape, Vector2 pose, CollisionFilter blocking, float previous, in MoveResult2D move, float cleared)
    {
        int support = SupportIndex(move.ContactCount);
        if (support < 0)
        {
            return Vector2.Zero;
        }

        Aabb2D bounds = shape.Bounds;
        float half = (bounds.Max.X - bounds.Min.X) * 0.5f;
        float center = pose.X + bounds.Min.X + half;
        float bottom = pose.Y + bounds.Max.Y;
        float allowance = half * _floorTan;
        float reach = (2f * allowance) + CollisionTolerance.ContactSkin;
        float held = Math.Clamp(previous - bottom, 0f, allowance);
        float side = _restSide;
        _sink = MathF.Min(held, Kept(move.Translation.X, side, side, held, cleared));

        // The ray reaches twice the allowance. A pose a walk left hanging over a crest still finds the
        // floor below and holds the entity as low as the allowance lets it.
        if (!world.Raycast(new Vector2(center, bottom), -Up, reach, blocking, out RayHit2D floor, _collider.Handle)
            || !IsFloor(floor.Normal))
        {
            return Vector2.Zero;
        }

        // The swept pose stands a slop clear of its floor. On a flat floor the center is that close too.
        float depth = floor.Point.Y - bottom - CollisionTolerance.LinearSlop;
        if (depth < CollisionTolerance.LinearSlop)
        {
            _sink = 0f;
            _restSide = 0f;
            return floor.Normal;
        }

        // The pose rests on the side where the floor climbs toward it. Two flat floors leave only the
        // contact to say which side that is.
        Vector2 rest = _found[support].Normal;
        _restSide = MathF.Abs(floor.Normal.X) > FlatX ? -MathF.Sign(floor.Normal.X)
            : MathF.Abs(rest.X) > FlatX ? -MathF.Sign(rest.X)
            : MathF.Sign(_found[support].Point.X - center);

        float kept = Kept(move.Translation.X, _restSide, side, held, cleared);
        float hold = MathF.Min(held, kept);

        // A floor that runs on as one plane from the center to the resting corner needs no ledge test.
        bool plane = Vector2.Dot(floor.Normal, rest) >= 1f - PlaneTolerance
            && MathF.Abs(depth - (half * MathF.Abs(floor.Normal.X / floor.Normal.Y))) <= CollisionTolerance.ContactSkin;
        if (!plane && _restSide != 0f)
        {
            Vector2 from = new(center, floor.Point.Y - CollisionTolerance.LinearSlop);
            Vector2 chord = new Vector2(_restSide < 0f ? pose.X + bounds.Min.X : pose.X + bounds.Max.X, bottom) - from;
            if (world.Raycast(from, chord, chord.Length(), blocking, out RayHit2D wall, _collider.Handle) && !IsFloor(wall.Normal))
            {
                // A wall between the two floors is a ledge. An entity that stood below the wall has just
                // stepped up onto the ledge and stands on it.
                _sink = previous > wall.Point.Y ? 0f : hold;
                return Vector2.Zero;
            }

            // No ray meets the side of a floor that blocks only from above. The floors themselves show
            // whether one runs on into the other.
            if (RestsOnTopOnly(world, move.ContactCount) && !Joins(world, blocking, center, bottom, half, reach, floor))
            {
                _sink = hold;
                return Vector2.Zero;
            }
        }

        float sink = MathF.Min(depth, allowance);
        if (sink > kept + CollisionTolerance.LinearSlop && (_restSide == 0f || Hangs(world, blocking, center, bottom, half, sink)))
        {
            _sink = hold;
            return Vector2.Zero;
        }

        _sink = sink;

        return floor.Normal;
    }

    // How deep below the swept pose the move has already kept the hanging half clear, for a pose resting
    // on side `side` after a walk of `across` from one resting on `before`. A pose that moved toward its
    // resting side, or not across at all, hangs within the box the entity filled before, down to `held`.
    // One that moved the other way is clear as far as KeepClear swept it.
    private static float Kept(float across, float side, float before, float held, float cleared) =>
        across == 0f || MathF.Sign(across) == side ? held
        : side == before ? cleared
        : 0f;

    // Whether a wall stands in the hanging half of an entity `sink` below the swept pose. A sliver at the
    // center sweeps out through that half, below the pose.
    private bool Hangs(CollisionWorld2D world, CollisionFilter blocking, float center, float bottom, float half, float sink)
    {
        Aabb2D sliver = new(new Vector2(center - CollisionTolerance.LinearSlop, bottom), new Vector2(center + CollisionTolerance.LinearSlop, bottom + sink));
        Vector2 across = new(-_restSide * (half - (2f * CollisionTolerance.LinearSlop)), 0f);

        return world.ShapeCast(Shape2D.Box(sliver), Vector2.Zero, across, blocking, out ShapeCastHit2D wall, _collider.Handle)
            && !IsFloor(wall.Normal);
    }

    // Whether a floor the move stopped on blocks only from above.
    private bool RestsOnTopOnly(CollisionWorld2D world, int count)
    {
        for (int index = 0; index < count; index++)
        {
            if (_stopped[index] && IsFloor(_found[index].Normal) && world.IsTopOnly(_found[index].Target))
            {
                return true;
            }
        }

        return false;
    }

    // Whether the floor under the resting corner runs on into `floor`, the floor under the center, with
    // no drop between. The two floors' lines must meet under the box, and the floor just to either side
    // of that point must lie on the matching line.
    private bool Joins(CollisionWorld2D world, CollisionFilter blocking, float center, float bottom, float half, float reach, in RayHit2D floor)
    {
        float corner = center + (_restSide * (half - CollisionTolerance.LinearSlop));
        if (!world.Raycast(new Vector2(corner, bottom), -Up, reach, blocking, out RayHit2D rest, _collider.Handle)
            || !IsFloor(rest.Normal))
        {
            return false;
        }

        Vector2 restNormal = rest.Normal;
        Vector2 centerNormal = floor.Normal;
        float restLine = Vector2.Dot(restNormal, rest.Point);
        float centerLine = Vector2.Dot(centerNormal, floor.Point);
        float cross = (restNormal.X * centerNormal.Y) - (restNormal.Y * centerNormal.X);
        if (MathF.Abs(cross) <= 1e-4f)
        {
            return MathF.Abs(Vector2.Dot(centerNormal, rest.Point) - centerLine) <= CollisionTolerance.ContactSkin;
        }

        float meet = ((restLine * centerNormal.Y) - (centerLine * restNormal.Y)) / cross;
        float along = (meet - center) * _restSide;
        if (along < -CollisionTolerance.ContactSkin || along > half + CollisionTolerance.ContactSkin)
        {
            return false;
        }

        return (along <= JoinProbe || Lies(world, blocking, meet - (_restSide * JoinProbe), bottom, reach, centerNormal, centerLine))
            && (along >= half - JoinProbe || Lies(world, blocking, meet + (_restSide * JoinProbe), bottom, reach, restNormal, restLine));
    }

    // Whether the floor straight below `x` has `normal` and lies on the line where that normal's dot
    // product with a point is `line`.
    private bool Lies(CollisionWorld2D world, CollisionFilter blocking, float x, float bottom, float reach, Vector2 normal, float line) =>
        world.Raycast(new Vector2(x, bottom), -Up, reach, blocking, out RayHit2D hit, _collider.Handle)
        && Vector2.Dot(hit.Normal, normal) >= 1f - PlaneTolerance
        && MathF.Abs(Vector2.Dot(normal, hit.Point) - line) <= CollisionTolerance.ContactSkin;

    // The index of the first contact that stopped a move on a floor, or -1.
    private int SupportIndex(int count)
    {
        for (int index = 0; index < count; index++)
        {
            if (_stopped[index] && IsFloor(_found[index].Normal))
            {
                return index;
            }
        }

        return -1;
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

    // Sweeps a riding body along its floor's motion with its own blocking filter. A body writing its
    // own position is never moved again. A rider is in a scene with its collider registered, because
    // every path that breaks either ends the ride first.
    internal void Carry(Vector2 motion)
    {
        _carriedWhole = false;
        if (_moving)
        {
            return;
        }

        CollisionWorld2D world = _collider.World!;
        Entity entity = Entity!;
        MoveResult2D result = world.Move(
            world.ShapeOf(_collider.Handle),
            Swept(entity),
            motion,
            Filter,
            default,
            _collider.Handle);

        Displace(entity, result.Translation);
        _carriedWhole = result.Translation == motion;
    }

    // Whether this body rides `floor` and its carry took the floor's whole motion. The floor's path then
    // cannot reach the body.
    internal bool CarriedWholeBy(Collider2D floor) => _carriedWhole && ReferenceEquals(_floor, floor);

    // Shoves the body by what is left of the pusher's move after meeting it, or leaves a rider where
    // its carry put it. A shove that falls short by more than the mover's slop raises Crushed with the
    // pusher's surface where the two met, and the shortfall along its normal as the depth.
    // A collider moved to another entity still names this body, which then has nothing to shove.
    internal void Shove(Collider2D pusher, Vector2 remainder, Vector2 normal, Vector2 point)
    {
        if (_moving || Entity is not { } entity || !ReferenceEquals(_collider.Entity, entity))
        {
            return;
        }

        Vector2 applied = Vector2.Zero;
        if (!ReferenceEquals(_floor, pusher))
        {
            CollisionWorld2D sweeping = _collider.World!;
            MoveResult2D result = sweeping.MovePast(
                sweeping.ShapeOf(_collider.Handle),
                Swept(entity),
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

    // How far the pose this body sweeps stands from its registered collider. A pusher meets that pose.
    internal Vector2 SweptOffset() =>
        _sink != 0f && Entity is { } entity ? Swept(entity) - entity.WorldPosition : Vector2.Zero;

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
            _written = entity.Position;
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

    internal override bool Steps => false;

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
        _blockedByFilter = Collider2D.ResolveFilter(_scene.Collision, _blockedBy);
        MovedByFilter = Collider2D.ResolveFilter(_scene.Collision, _movedBy);
        Filter = _blockedByFilter | MovedByFilter;
        _scene.CountMovedBy(MovedByFilter, 1);
        _collider.Body = this;
    }

    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        // Classifying no contacts clears the flags and normals and ends the ride.
        _moveContactCount = 0;
        Classify();
        _scene?.CountMovedBy(MovedByFilter, -1);
        _scene = null;
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
            SurfaceKind kind = KindOf(normal);

            if (kind == SurfaceKind.Floor)
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

        Ride(ridden);
    }
}
