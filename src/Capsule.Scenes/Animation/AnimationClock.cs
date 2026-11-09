using System.Runtime.CompilerServices;

namespace Capsule.Animation;

// The tick clock an animator runs its clip on: the speed, the part of a tick it has carried, and the
// hold on the step a Play claims. An animator keeps one in a field, initialised with new(), and never
// copies it.
internal struct AnimationClock
{
    private float _speed = 1f;

    // The fraction of a tick Speed has advanced and no step has spent yet.
    private float _carry;

    private bool _pendingStart;
    private long? _startedOnTick;

    public AnimationClock()
    {
    }

    public float Speed
    {
        readonly get => _speed;

        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Speed is finite and not negative. Play a reversed clip to run an animation backwards.");
            }

            _speed = value;
        }
    }

    // Holds the next step for a Play made during tick startedOn, or made out of a scene when it is null.
    public void Start(long? startedOn)
    {
        _pendingStart = true;
        _startedOnTick = startedOn;
        _carry = 0f;
    }

    // Counts a start made out of a scene from the tick the entity joins one.
    public void Join(long? tick)
    {
        if (_pendingStart && _startedOnTick is null)
        {
            _startedOnTick = tick;
        }
    }

    // A clip swapped in by a step begins with no part of a tick carried.
    public void DropCarry() => _carry = 0f;

    // Whether this step moves the clip at all, and the whole ticks it spends when it does. Inlined: every
    // animator calls it every step.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdvance(long tick, bool paused, out float ticks)
    {
        ticks = 0f;

        // A played frame shows for the whole pause. A paused step spends the step Play holds back.
        if (paused)
        {
            _pendingStart = false;
            return false;
        }

        // The tick Play ran in spends no step on its frame. A component stepped after the animator plays
        // during an earlier tick than this step's. A Play outside a step belongs to the next tick.
        if (_pendingStart)
        {
            _pendingStart = false;
            if (_startedOnTick is not { } startedOn || startedOn == tick)
            {
                return false;
            }
        }

        float advance = _carry + _speed;
        ticks = MathF.Floor(advance);
        _carry = advance - ticks;
        return true;
    }

    // The cursor steps that spend ticks, more than one, of a clip duration long. Whole passes of a loop
    // land where they began, and lapped says some were skipped. Skipping them bounds the work to one pass
    // whatever the speed.
    public static long Steps(float ticks, int duration, bool loop, out bool lapped)
    {
        lapped = loop && ticks >= duration;

        return lapped ? (long)((double)ticks % duration)
            : ticks >= duration ? duration : (long)ticks;
    }
}
