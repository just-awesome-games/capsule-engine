using System.Numerics;
using Capsule.Animation;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Navigation;

/// <summary>Moves its entity along a <see cref="Path2D"/> at <see cref="Speed"/> by writing the entity's position each fixed step.</summary>
/// <remarks>
/// A collider on the entity carries what rides it, as it does for any write of the entity's position. The
/// path and <see cref="Speed"/> are in the space of the entity's <see cref="Entity.Position"/>, its parent's,
/// and the points are relative to that position as the follower starts. Starting places the entity at
/// <see cref="Offset"/> with no motion drawn between. The follower keeps its origin and <see cref="Offset"/> while
/// its entity is out of a scene, and carries on from them when it returns. Code writing the entity's position
/// elsewhere is overwritten on the follower's next move.
/// <para>
/// <see cref="LoopMode.Repeat"/> on a path that is not <see cref="Path2D.Closed"/> jumps from the last point to
/// the first. The jump is drawn with no motion between, and a collider on the entity drags its riders along it.
/// </para>
/// </remarks>
/// <example>
/// A block running out and back along a straight path:
/// <code>
/// Add(new PathFollower2D(new Path2D(Vector2.Zero, new Vector2(48f, 0f))) { Speed = 30f, Loop = LoopMode.PingPong });
/// </code>
/// An eased leg from one point to the next is a <see cref="Tween"/> writing the distance, with no speed:
/// <code>
/// _leg.Step();
/// _follower.Offset = DeterministicMath.Lerp(_follower.Path.Distances[_from], _follower.Path.Distances[_from + 1], _leg.Value);
/// </code>
/// </example>
public sealed class PathFollower2D : Component
{
    private float _offset;

    // Whether a PingPong is on its way back toward the first point.
    private bool _returning;

    // The entity's position the path is relative to, taken as the follower starts.
    private Vector2? _origin;

    /// <summary>The path the entity follows.</summary>
    public Path2D Path { get; }

    /// <summary>
    /// Units travelled along the path a second, on the fixed step. Zero by default, which leaves the entity
    /// where <see cref="Offset"/> places it, and a negative speed travels toward the first point.
    /// </summary>
    public float Speed
    {
        get;
        set => field = Guard.Finite(value, nameof(value));
    }

    /// <summary>What the follower does at the end of the path it travels toward. <see cref="LoopMode.Once"/> by default.</summary>
    public LoopMode Loop
    {
        get;
        set => field = Guard.RequireLoop(value, nameof(value));
    }

    /// <summary>Creates a follower of <paramref name="path"/>, standing at its first point.</summary>
    public PathFollower2D(Path2D path)
    {
        ArgumentNullException.ThrowIfNull(path);

        Path = path;
    }

    /// <summary>The distance along the path the entity stands at, from 0 to the path's <see cref="Path2D.Length"/>.</summary>
    /// <remarks>Writing it moves the entity there at once, and a distance outside that range is clamped into it.</remarks>
    public float Offset
    {
        get => _offset;
        set
        {
            _offset = Math.Clamp(Guard.Finite(value, nameof(value)), 0f, Path.Length);
            Place(teleport: false);
        }
    }

    /// <summary>Whether a <see cref="LoopMode.Once"/> follower stands at the end its <see cref="Speed"/> travels toward.</summary>
    /// <remarks>A looping follower, or one whose speed is zero, never finishes.</remarks>
    public bool IsFinished =>
        Loop == LoopMode.Once && ((Speed > 0f && _offset == Path.Length) || (Speed < 0f && _offset == 0f));

    /// <summary>
    /// Takes the entity's position, where its own scene hooks left it, as the origin and places the entity at
    /// <see cref="Offset"/>.
    /// </summary>
    /// <inheritdoc/>
    protected internal override void OnStart()
    {
        _origin = Entity!.Position;
        Place(teleport: true);
    }

    /// <inheritdoc/>
    protected internal override void OnStep(in StepContext context)
    {
        if (Speed == 0f)
        {
            return;
        }

        float length = Path.Length;
        float travel = Speed * context.DeltaSeconds;
        bool jumped = false;
        switch (Loop)
        {
            case LoopMode.Once:
                _offset = Math.Clamp(_offset + travel, 0f, length);
                break;
            case LoopMode.Repeat:
                float reached = _offset + travel;
                jumped = !Path.Closed && (reached >= length || reached < 0f);
                _offset = Wrap(reached, length);
                break;
            case LoopMode.PingPong:
                // Out and back is one run of twice the length, unfolded at the last point.
                float period = length + length;
                float unfolded = Wrap((_returning ? period - _offset : _offset) + travel, period);
                _returning = unfolded > length;
                _offset = _returning ? period - unfolded : unfolded;
                break;
        }

        Place(jumped);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Offset", _offset);
        panel.Field("Length", Path.Length);
        panel.Field("Speed", Speed);
        panel.Field("Loop", Loop.ToString());
    }

    // The remainder of value within 0 to period, for a value on either side of that range.
    private static float Wrap(float value, float period)
    {
        float wrapped = value % period;

        return wrapped < 0f ? wrapped + period : wrapped;
    }

    private void Place(bool teleport)
    {
        // Before the start, or once detached, the offset waits for the next placement.
        if (_origin is not { } origin || Entity is not { } entity)
        {
            return;
        }

        Vector2 position = origin + Path.PointAt(_offset);
        if (teleport)
        {
            entity.Teleport(position);
        }
        else
        {
            entity.Position = position;
        }
    }
}
