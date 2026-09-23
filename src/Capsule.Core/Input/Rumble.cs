namespace Capsule.Input;

/// <summary>
/// The run's gamepad rumble: the pulses playing and the level they mix to. Reached as
/// <c>Run.Rumble</c> and held for the run.
/// </summary>
/// <remarks>
/// A pulse survives a scene transition until it ends or something stops it. A call taking a handle
/// to a pulse that has ended does nothing. After each step the host reads <see cref="Level"/> and
/// writes it to the pad.
/// <para>
/// Pulse lifetimes and envelopes are computed from the step's tick and the step length. Nothing is
/// read back from a pad. A headless run reaches the same state as a windowed one, and a driven run
/// steps identically whether or not a pad is connected.
/// </para>
/// </remarks>
public sealed class Rumble
{
    /// <summary>How many pulses may play at once before <see cref="Play(in RumblePulse)"/> evicts one.</summary>
    public const int MaxPulses = 8;

    // Float step lengths make a duration that is a whole multiple of the step multiply to a hair under
    // its own value. This relative tolerance keeps such a duration from lasting one more step.
    private const double DurationTolerance = 1e-6;

    private const int MaxGeneration = 0xFFFFFF;

    private readonly Slot[] _slots = new Slot[MaxPulses];

    private long _tick;

    private double _stepSeconds = 1.0 / StepContext.DefaultStepHertz;

    private float _volume = 1f;

    // An idle mixer: volume 1, nothing playing.
    internal Rumble()
    {
        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].Generation = 1;
        }
    }

    /// <summary>
    /// The master scale applied to <see cref="Level"/>, in [0, 1] and 1 by default. Zero silences
    /// the pad for players who turn rumble off.
    /// </summary>
    /// <remarks>Every pulse still runs its course.</remarks>
    public float Volume
    {
        get => _volume;
        set
        {
            Guard.InUnit(value, nameof(value));
            _volume = value;
        }
    }

    /// <summary>
    /// The settled output for the current step: per motor, the loudest live pulse after its fade
    /// envelope, then scaled by <see cref="Volume"/>. Computed on read and allocation free.
    /// </summary>
    public RumbleLevel Level
    {
        get
        {
            float low = 0f;
            float high = 0f;
            float leftTrigger = 0f;
            float rightTrigger = 0f;

            ReadOnlySpan<Slot> slots = _slots;
            for (int i = 0; i < slots.Length; i++)
            {
                ref readonly Slot slot = ref slots[i];
                if (!slot.Live || Expired(in slot))
                {
                    continue;
                }

                float envelope = Envelope(in slot);
                low = MathF.Max(low, slot.Low * envelope);
                high = MathF.Max(high, slot.High * envelope);
                leftTrigger = MathF.Max(leftTrigger, slot.LeftTrigger * envelope);
                rightTrigger = MathF.Max(rightTrigger, slot.RightTrigger * envelope);
            }

            return new RumbleLevel(low * _volume, high * _volume, leftTrigger * _volume, rightTrigger * _volume);
        }
    }

    /// <summary>
    /// Plays a timed pulse on the two main motors that decays linearly over
    /// <paramref name="seconds"/>. The pulse reads at full amplitude on the step it is played.
    /// </summary>
    /// <param name="low">The low-frequency motor's amplitude in [0, 1].</param>
    /// <param name="high">The high-frequency motor's amplitude in [0, 1].</param>
    /// <param name="seconds">How long the pulse lasts, greater than zero and finite.</param>
    /// <returns>The pulse started. It is never <see cref="RumbleHandle.None"/>.</returns>
    public RumbleHandle Play(float low, float high, float seconds)
    {
        Guard.InUnit(low, nameof(low));
        Guard.InUnit(high, nameof(high));
        Guard.Positive(seconds, nameof(seconds));

        return Start(low, high, 0f, 0f, seconds, RumbleFade.Decay, held: false);
    }

    /// <summary>
    /// Plays a timed pulse. It ends after its seconds have elapsed in whole steps, and reads at
    /// full amplitude on the step it is played.
    /// </summary>
    /// <remarks>
    /// With no slot free, the live pulse with the lowest current peak is evicted and its slot
    /// reused. A quiet tail never keeps a new hit off the pad.
    /// </remarks>
    /// <returns>The pulse started. It is never <see cref="RumbleHandle.None"/>.</returns>
    public RumbleHandle Play(in RumblePulse pulse)
    {
        Guard.InUnit(pulse.Low, nameof(pulse));
        Guard.InUnit(pulse.High, nameof(pulse));
        Guard.InUnit(pulse.LeftTrigger, nameof(pulse));
        Guard.InUnit(pulse.RightTrigger, nameof(pulse));
        Guard.Positive(pulse.Seconds, nameof(pulse));
        RequireFade(pulse.Fade, nameof(pulse));

        return Start(pulse.Low, pulse.High, pulse.LeftTrigger, pulse.RightTrigger, pulse.Seconds, pulse.Fade, held: false);
    }

    /// <summary>Holds the two main motors at a level until <see cref="Stop(RumbleHandle)"/>. No fade.</summary>
    /// <param name="low">The low-frequency motor's amplitude in [0, 1].</param>
    /// <param name="high">The high-frequency motor's amplitude in [0, 1].</param>
    /// <returns>The pulse started. It is never <see cref="RumbleHandle.None"/>.</returns>
    public RumbleHandle Hold(float low, float high)
    {
        Guard.InUnit(low, nameof(low));
        Guard.InUnit(high, nameof(high));

        return Start(low, high, 0f, 0f, 0f, RumbleFade.None, held: true);
    }

    /// <summary>
    /// Holds every motor at <paramref name="level"/> until <see cref="Stop(RumbleHandle)"/>.
    /// </summary>
    /// <remarks>
    /// No fade. A held pulse is evicted like any other when it has the lowest peak of a full table.
    /// </remarks>
    /// <returns>The pulse started. It is never <see cref="RumbleHandle.None"/>.</returns>
    public RumbleHandle Hold(RumbleLevel level)
    {
        RequireLevel(level, nameof(level));

        return Start(level.Low, level.High, level.LeftTrigger, level.RightTrigger, 0f, RumbleFade.None, held: true);
    }

    /// <summary>
    /// Retunes a live pulse's two main motors in place and rests its triggers. A timed pulse keeps
    /// its clock and its fade.
    /// </summary>
    /// <param name="handle">The pulse to retune.</param>
    /// <param name="low">The low-frequency motor's amplitude in [0, 1].</param>
    /// <param name="high">The high-frequency motor's amplitude in [0, 1].</param>
    public void Set(RumbleHandle handle, float low, float high)
    {
        Guard.InUnit(low, nameof(low));
        Guard.InUnit(high, nameof(high));

        Retune(handle, low, high, 0f, 0f);
    }

    /// <summary>Retunes a live pulse's four motors in place. A timed pulse keeps its clock and its fade.</summary>
    /// <param name="handle">The pulse to retune.</param>
    /// <param name="level">The amplitudes to play from now on.</param>
    public void Set(RumbleHandle handle, RumbleLevel level)
    {
        RequireLevel(level, nameof(level));

        Retune(handle, level.Low, level.High, level.LeftTrigger, level.RightTrigger);
    }

    /// <summary>Ends <paramref name="handle"/> and frees its slot.</summary>
    public void Stop(RumbleHandle handle)
    {
        if (TryResolve(handle, out int index))
        {
            Free(ref _slots[index]);
        }
    }

    /// <summary>Ends every pulse, held and timed. A pause menu calls this when it opens.</summary>
    public void Stop()
    {
        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Live)
            {
                Free(ref slots[i]);
            }
        }
    }

    /// <summary>Whether this pulse still owns its slot: held, or timed and not yet run out.</summary>
    public bool IsLive(RumbleHandle handle) => TryResolve(handle, out _);

    // Moves the mixer's clock onto this step and frees the pulses that have run out. A pulse played
    // during the step counts its elapsed time from this step's tick, so it reads at full amplitude
    // when the host reads Level after the step.
    internal void BeginStep(in StepContext context)
    {
        _tick = context.Tick;

        if (context.StepSeconds > 0.0 && context.StepSeconds != _stepSeconds)
        {
            ChangeStepLength(context.StepSeconds);
        }

        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Live && Expired(in slots[i]))
            {
                Free(ref slots[i]);
            }
        }
    }

    // A live pulse measured its elapsed time at the step length in force when it started. Bank that
    // time at the old length and count on from here at the new one, so the pulse still lasts its
    // seconds.
    private void ChangeStepLength(double stepSeconds)
    {
        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Live)
            {
                Rebase(ref slots[i]);
            }
        }

        _stepSeconds = stepSeconds;
    }

    // Banks the elapsed time this pulse has reached, measured at the step length that produced it.
    private void Rebase(ref Slot slot)
    {
        slot.ElapsedAtClock = ElapsedSeconds(in slot);
        slot.Clock = _tick;
    }

    private RumbleHandle Start(float low, float high, float leftTrigger, float rightTrigger, float seconds, RumbleFade fade, bool held)
    {
        int index = Allocate();

        ref Slot slot = ref _slots[index];
        slot.Live = true;
        slot.Held = held;
        slot.Low = low;
        slot.High = high;
        slot.LeftTrigger = leftTrigger;
        slot.RightTrigger = rightTrigger;
        slot.Seconds = seconds;
        slot.Fade = fade;
        slot.Clock = _tick;
        slot.ElapsedAtClock = 0.0;

        return RumbleHandle.Of(index, slot.Generation);
    }

    private void Retune(RumbleHandle handle, float low, float high, float leftTrigger, float rightTrigger)
    {
        if (!TryResolve(handle, out int index))
        {
            return;
        }

        ref Slot slot = ref _slots[index];
        slot.Low = low;
        slot.High = high;
        slot.LeftTrigger = leftTrigger;
        slot.RightTrigger = rightTrigger;
    }

    // The first free or expired slot. Failing that, the live pulse with the lowest current peak,
    // which is the one the pad would miss least.
    private int Allocate()
    {
        Span<Slot> slots = _slots;

        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].Live)
            {
                return i;
            }

            if (Expired(in slots[i]))
            {
                Free(ref slots[i]);

                return i;
            }
        }

        int quietest = 0;
        float lowestPeak = float.MaxValue;
        for (int i = 0; i < slots.Length; i++)
        {
            float peak = Peak(in slots[i]);
            if (peak < lowestPeak)
            {
                lowestPeak = peak;
                quietest = i;
            }
        }

        Free(ref slots[quietest]);

        return quietest;
    }

    // Advancing the generation makes existing handles to this slot stale. The wrap collides only with a
    // handle held across 16 million reuses of the same slot.
    private static void Free(ref Slot slot)
    {
        slot.Live = false;
        slot.Generation = slot.Generation >= MaxGeneration ? 1 : slot.Generation + 1;
    }

    private bool TryResolve(RumbleHandle handle, out int index)
    {
        index = handle.Slot;
        if (handle.IsNone || index >= _slots.Length)
        {
            return false;
        }

        ref Slot slot = ref _slots[index];

        return slot.Live && slot.Generation == handle.Generation && !Expired(in slot);
    }

    private double ElapsedSeconds(in Slot slot) => slot.ElapsedAtClock + ((_tick - slot.Clock) * _stepSeconds);

    private bool Expired(in Slot slot) =>
        !slot.Held && ElapsedSeconds(in slot) >= slot.Seconds * (1.0 - DurationTolerance);

    private float Envelope(in Slot slot)
    {
        if (slot.Held || slot.Fade == RumbleFade.None)
        {
            return 1f;
        }

        double remaining = 1.0 - (ElapsedSeconds(in slot) / slot.Seconds);

        return remaining <= 0.0 ? 0f : (float)remaining;
    }

    private float Peak(in Slot slot) =>
        MathF.Max(MathF.Max(slot.Low, slot.High), MathF.Max(slot.LeftTrigger, slot.RightTrigger)) * Envelope(in slot);

    private static void RequireLevel(RumbleLevel level, string parameterName)
    {
        Guard.InUnit(level.Low, parameterName);
        Guard.InUnit(level.High, parameterName);
        Guard.InUnit(level.LeftTrigger, parameterName);
        Guard.InUnit(level.RightTrigger, parameterName);
    }

    private static void RequireFade(RumbleFade fade, string parameterName)
    {
        if (fade is not (RumbleFade.Decay or RumbleFade.None))
        {
            throw new ArgumentOutOfRangeException(parameterName, fade, "Expected a RumbleFade member. Use Decay or None.");
        }
    }

    private struct Slot
    {
        internal bool Live;

        // Held until stopped. Seconds and Fade are meaningless while true.
        internal bool Held;

        internal float Low;

        internal float High;

        internal float LeftTrigger;

        internal float RightTrigger;

        internal float Seconds;

        internal RumbleFade Fade;

        internal int Generation;

        // The tick ElapsedAtClock was last measured at.
        internal long Clock;

        // Seconds elapsed at Clock, kept unrounded so a step-length change neither adds nor loses time.
        internal double ElapsedAtClock;
    }
}
