using System.Runtime.InteropServices;

namespace Capsule.Audio;

/// <summary>
/// The run's sound: buses, their volumes and pause state, and the voices playing on them. Pure and
/// deterministic — every voice's lifetime is derived from its clip's measured duration, its pitch
/// and the step length, so a headless run reaches the same state a windowed one does and nothing is
/// ever read back from a device.
/// <para>
/// Each step it rewrites <see cref="Commands"/>, the instructions the host applies to its voice
/// table afterwards, the way <c>FrameView</c> is rewritten for the renderer. Every gain a command
/// carries is already resolved against the master and bus volumes.
/// </para>
/// <para>
/// Reached as <c>Scene.Audio</c>; the host owns one for the whole run, so a voice survives a scene
/// transition unless whatever started it stops it.
/// </para>
/// </summary>
public sealed class AudioMixer
{
    /// <summary>How many voices may sound at once before <see cref="Play(in AudioPlayback)"/> steals one.</summary>
    public const int MaxVoices = 64;

    // The step length is a float, so a duration that is an exact multiple of it divides to a hair
    // over or under its own tick count. A relative tolerance keeps such a duration on the boundary
    // instead of spilling into one more tick.
    private const double TickTolerance = 1e-6;

    private const int MaxGeneration = 0xFFFFFF;

    private readonly List<AudioCommand> _commands = [];

    // Index 0 is the master bus, which is registered rather than special-cased so that resolving a
    // gain is two multiplications and no branch on a name.
    private readonly List<Bus> _buses = [new Bus(string.Empty)];

    private readonly Slot[] _slots = new Slot[MaxVoices];

    private long _tick;

    // Held to the precision a step context carries, so a run at the default rate is not read as a
    // rate change on its first step.
    private double _stepSeconds = 1f / StepContext.DefaultStepHertz;

    /// <summary>An idle mixer: master at volume 1, no other bus registered, nothing playing.</summary>
    public AudioMixer()
    {
        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].Generation = 1;
        }
    }

    /// <summary>
    /// The commands the last step raised, in the order they were raised. Rewritten every step and
    /// invalidated by the next mixer call, so a host applies them before stepping again. Commands
    /// raised before the first step, by the boot scene's start, stand until that step rewrites them:
    /// a host applies them once before stepping, and a test reads them before it steps. A scene
    /// entered by a transition starts inside the step that requested it, so its start rides that
    /// step's commands; a second scene started on the same mixer outside a step appends to what the
    /// last step raised.
    /// </summary>
    public ReadOnlySpan<AudioCommand> Commands => CollectionsMarshal.AsSpan(_commands);

    /// <summary>This bus's own linear amplitude in [0, 1]; 1 for a bus nothing has changed yet.</summary>
    public float GetVolume(AudioBus bus)
    {
        int index = Find(bus);

        return index < 0 ? 1f : _buses[index].Volume;
    }

    /// <summary>
    /// Sets this bus's linear amplitude, registering the bus if it is new, and raises
    /// <see cref="AudioCommandKind.SetGain"/> for every live voice on it — for every live voice at
    /// all when the bus is <see cref="AudioBus.Master"/>.
    /// <para>
    /// Bus volumes are the run's, not a scene's: what one scene sets stands for every scene after
    /// it until something sets it again, and a bus nothing has set reads 1. A game therefore levels
    /// its buses once — from its boot scene's start, or from a settings screen — rather than per
    /// scene. The mixer is installed on a scene after that scene is constructed, so a constructor
    /// has none to reach.
    /// </para>
    /// <para>
    /// A voice played before the volume it should carry goes out at the old product and is
    /// re-levelled by the <see cref="AudioCommandKind.SetGain"/> this raises for it. Ordering the
    /// two within one start changes what the host is told, never what it settles at.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="volume"/> is outside [0, 1] or is not a number.</exception>
    public void SetVolume(AudioBus bus, float volume)
    {
        RequireVolume(volume, nameof(volume));

        int index = Register(bus);
        CollectionsMarshal.AsSpan(_buses)[index].Volume = volume;

        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!Retired(ref slots[i]) && (index == 0 || slots[i].Bus == index))
            {
                Raise(AudioCommandKind.SetGain, i, in slots[i], Gain(in slots[i]), 0f);
            }
        }
    }

    /// <summary>Whether this bus is paused in its own right; a bus nothing has paused is not.</summary>
    public bool IsPaused(AudioBus bus)
    {
        int index = Find(bus);

        return index >= 0 && _buses[index].Paused;
    }

    /// <summary>
    /// Pauses this bus, registering it if it is new. Every voice whose effective state changes is
    /// held where it is and raises <see cref="AudioCommandKind.Pause"/>; a held one-shot resumes
    /// with the ticks it had left. Pausing <see cref="AudioBus.Master"/> pauses every voice.
    /// </summary>
    public void Pause(AudioBus bus) => SetBusPaused(bus, paused: true);

    /// <summary>
    /// Resumes this bus. A voice paused in its own right stays paused; every other one continues
    /// and raises <see cref="AudioCommandKind.Resume"/>.
    /// </summary>
    public void Resume(AudioBus bus) => SetBusPaused(bus, paused: false);

    /// <summary>Plays <paramref name="clip"/> once on <see cref="AudioBus.Master"/> at full volume and unit pitch.</summary>
    /// <returns>The voice started, or <see cref="Voice.None"/> when every voice is a live loop.</returns>
    public Voice Play(AudioClip clip) => Play(new AudioPlayback(clip));

    /// <summary>Plays <paramref name="clip"/> once on <paramref name="bus"/> at full volume and unit pitch.</summary>
    /// <returns>The voice started, or <see cref="Voice.None"/> when every voice is a live loop.</returns>
    public Voice Play(AudioClip clip, AudioBus bus) => Play(new AudioPlayback(clip) { Bus = bus });

    /// <summary>
    /// Starts one voice and raises <see cref="AudioCommandKind.Play"/> for it. A one-shot ends by
    /// itself after <c>ceil(DurationSeconds / Pitch / step)</c> steps; a loop never does. Played
    /// onto a paused bus, the voice starts held and raises <see cref="AudioCommandKind.Pause"/>
    /// straight after its play.
    /// <para>
    /// With no slot free, the oldest live one-shot is stolen and raises
    /// <see cref="AudioCommandKind.Stop"/> ahead of the new voice's play; where every live voice
    /// loops, nothing is stolen, nothing is raised, and the answer is <see cref="Voice.None"/>.
    /// </para>
    /// </summary>
    /// <returns>The voice started, or <see cref="Voice.None"/> when every voice is a live loop.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The playback's volume is outside [0, 1], its pitch is not positive and finite — which
    /// <c>default(AudioPlayback)</c> is not — its pan is outside [-1, 1], its start is negative,
    /// not finite, or at or past the clip's duration, or its clip's
    /// <see cref="AudioClip.LoopRegion"/> is neither <see cref="AudioLoopRegion.None"/> nor a
    /// region that starts at or after zero, ends after it starts, and ends no later than the clip.
    /// </exception>
    public Voice Play(in AudioPlayback playback)
    {
        RequireVolume(playback.Volume, nameof(playback));
        RequirePitch(playback.Pitch, nameof(playback));
        RequirePan(playback.Pan, nameof(playback));
        RequireStart(playback.StartSeconds, playback.Clip.DurationSeconds, nameof(playback));
        RequireRegion(playback.Clip, nameof(playback));

        int bus = Register(playback.Bus);
        int index = Allocate();
        if (index < 0)
        {
            return Voice.None;
        }

        double start = StartOf(in playback);

        ref Slot slot = ref _slots[index];
        slot.Clip = playback.Clip;
        slot.Bus = bus;
        slot.Volume = playback.Volume;
        slot.Pitch = playback.Pitch;
        slot.Pan = playback.Pan;
        slot.StartSeconds = start;
        slot.Loop = playback.Loop;
        slot.Live = true;
        slot.SelfPaused = false;
        slot.Paused = false;
        slot.StartTick = _tick;
        slot.RemainingSeconds = playback.Clip.DurationSeconds - start;
        slot.TimeAtClock = start;
        Arm(ref slot);

        Raise(AudioCommandKind.Play, index, in slot, Gain(in slot), slot.Pitch);

        if (EffectivelyPaused(in slot))
        {
            Hold(ref slot, index);
        }

        return Voice.Of(index, slot.Generation);
    }

    /// <summary>
    /// Ends <paramref name="voice"/>, raising <see cref="AudioCommandKind.Stop"/> and freeing its
    /// slot; the handle is stale from here on, so <see cref="IsLive"/>, <see cref="IsPlaying"/> and
    /// <see cref="IsPaused(Voice)"/> all read false for it. Does nothing for a voice that has already ended.
    /// </summary>
    public void Stop(Voice voice)
    {
        if (!TryResolve(voice, out int index))
        {
            return;
        }

        ref Slot slot = ref _slots[index];
        Raise(AudioCommandKind.Stop, index, in slot, 0f, 0f);
        Free(ref slot);
    }

    /// <summary>
    /// Holds <paramref name="voice"/> where it is, keeping the ticks it has left. Does nothing for
    /// a voice already paused in its own right, or one that has ended.
    /// </summary>
    public void Pause(Voice voice)
    {
        if (!TryResolve(voice, out int index) || _slots[index].SelfPaused)
        {
            return;
        }

        ref Slot slot = ref _slots[index];
        slot.SelfPaused = true;

        if (!slot.Paused)
        {
            Hold(ref slot, index);
        }
    }

    /// <summary>
    /// Continues <paramref name="voice"/> from where it was held. A voice whose bus is still paused
    /// stays held and raises nothing. Does nothing for a voice that has ended.
    /// </summary>
    public void Resume(Voice voice)
    {
        if (!TryResolve(voice, out int index) || !_slots[index].SelfPaused)
        {
            return;
        }

        ref Slot slot = ref _slots[index];
        slot.SelfPaused = false;

        if (slot.Paused && !EffectivelyPaused(in slot))
        {
            Release(ref slot, index);
        }
    }

    /// <summary>
    /// Sets this voice's own linear amplitude and raises <see cref="AudioCommandKind.SetGain"/>
    /// with the gain that resolves to. Does nothing for a voice that has ended.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="volume"/> is outside [0, 1] or is not a number.</exception>
    public void SetVolume(Voice voice, float volume)
    {
        RequireVolume(volume, nameof(volume));

        if (!TryResolve(voice, out int index))
        {
            return;
        }

        ref Slot slot = ref _slots[index];
        slot.Volume = volume;
        Raise(AudioCommandKind.SetGain, index, in slot, Gain(in slot), 0f);
    }

    /// <summary>
    /// Sets this voice's playback rate and raises <see cref="AudioCommandKind.SetPitch"/>. The clip
    /// time a one-shot has left is what carries across, so the tick it ends on is derived from that
    /// time again at the new rate. Does nothing for a voice that has ended.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pitch"/> is not positive and finite.</exception>
    public void SetPitch(Voice voice, float pitch)
    {
        RequirePitch(pitch, nameof(pitch));

        if (!TryResolve(voice, out int index))
        {
            return;
        }

        ref Slot slot = ref _slots[index];

        // Measured against the old rate, before it is replaced: rescaling the already-rounded tick
        // count instead would round up again and hand the voice time it never had.
        slot.TimeAtClock = ClipTimeAt(in slot);
        if (!slot.Loop)
        {
            slot.RemainingSeconds = ClipTimeLeft(in slot);
        }

        slot.Clock = _tick;
        slot.Pitch = pitch;

        if (!slot.Loop && !slot.Paused)
        {
            Arm(ref slot);
        }

        Raise(AudioCommandKind.SetPitch, index, in slot, 0f, pitch);
    }

    /// <summary>
    /// Sets where this voice sits between the speakers and raises
    /// <see cref="AudioCommandKind.SetPan"/>. Does nothing for a voice that has ended.
    /// </summary>
    /// <param name="voice">The voice to move.</param>
    /// <param name="pan">Stereo position in [-1, 1]: -1 hard left, 0 centred, 1 hard right.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pan"/> is outside [-1, 1] or is not a number.</exception>
    public void SetPan(Voice voice, float pan)
    {
        RequirePan(pan, nameof(pan));

        if (!TryResolve(voice, out int index))
        {
            return;
        }

        ref Slot slot = ref _slots[index];
        slot.Pan = pan;
        Raise(AudioCommandKind.SetPan, index, in slot, 0f, 0f);
    }

    /// <summary>
    /// The clip time this voice is at on the step being taken, in seconds from the clip's start; 0
    /// for a voice that is not live. Derived from the step tick rather than read back from a device,
    /// so it is the same in a headless run as in a windowed one, and it moves in whole steps rather
    /// than continuously.
    /// <para>
    /// It advances at the voice's pitch from the start it was played at, holds while the voice is
    /// held — by its own pause or its bus's — and, for a looping voice, wraps: one whose clip carries
    /// an <see cref="AudioClip.LoopRegion"/> runs past the region's end once and then reads inside
    /// the region, and one whose clip carries none wraps at the clip's duration.
    /// </para>
    /// <para>
    /// Unity's <c>AudioSource.time</c> reads this way.
    /// </para>
    /// </summary>
    public double GetTime(Voice voice)
    {
        if (!TryResolve(voice, out int index))
        {
            return 0.0;
        }

        ref Slot slot = ref _slots[index];
        double time = Math.Max(0.0, ClipTimeAt(in slot));

        if (!slot.Loop)
        {
            return Math.Min(time, slot.Clip.DurationSeconds);
        }

        bool region = slot.Clip.LoopRegion.HasRegion;
        double start = region ? slot.Clip.LoopRegion.StartSeconds : 0.0;
        double end = region ? slot.Clip.LoopRegion.EndSeconds : slot.Clip.DurationSeconds;

        if (time < end)
        {
            return time;
        }

        double cycle = end - start;

        return cycle > 0.0 ? start + ((time - end) % cycle) : start;
    }

    /// <summary>
    /// Whether this voice still owns its slot: sounding, or held by its own pause or its bus's.
    /// False once it has ended — stopped, expired or stolen — and for <see cref="Voice.None"/> or a
    /// handle to a voice that has ended.
    /// <para>
    /// Ownership, not audibility: this is the predicate that answers whether a sound is already
    /// going and so must not be started a second time. <see cref="IsPlaying"/> and
    /// <see cref="IsPaused(Voice)"/> partition it — exactly one of them holds while a voice is live, both
    /// are false once it is not — so reading <see cref="IsPlaying"/> alone stacks a duplicate voice
    /// over one its bus is holding.
    /// </para>
    /// <para>
    /// FMOD's <c>Channel::isPlaying</c> reads this way, true for a paused channel and failing for a
    /// dead handle; Unity's <c>AudioSource.isPlaying</c> reads the way <see cref="IsPlaying"/> does.
    /// </para>
    /// </summary>
    public bool IsLive(Voice voice) => TryResolve(voice, out _);

    /// <summary>
    /// Whether this voice is live and sounding: not held, and either looping or not yet finished.
    /// False while it is held, so it answers audibility rather than ownership —
    /// <see cref="IsLive"/> is the one to ask whether the voice still exists.
    /// </summary>
    public bool IsPlaying(Voice voice) => TryResolve(voice, out int index) && !_slots[index].Paused;

    /// <summary>
    /// Whether this voice is live and held, by its own pause or by its bus's: the other half of
    /// <see cref="IsLive"/>, and false for a voice that has ended.
    /// </summary>
    public bool IsPaused(Voice voice) => TryResolve(voice, out int index) && _slots[index].Paused;

    // Clears the commands the previous step raised and moves the mixer's clock onto this step, so
    // that a voice started during it expires against this step's tick and step length. A simulation
    // calls this before the scene's step; a mixer no simulation has stepped is at tick 0 and the
    // default step length, and a voice it started there is re-armed here against the run's own.
    internal void BeginStep(in StepContext context)
    {
        _commands.Clear();
        _tick = context.Tick;

        if (context.DeltaSeconds > 0f && context.DeltaSeconds != _stepSeconds)
        {
            Restep(context.DeltaSeconds);
        }
    }

    // A live one-shot was armed against the step length in force when it started — the default one
    // for anything played before the first step. Its clip time left is measured at that length and
    // its end tick derived from it again at the new one, so a voice lasts its clip's duration
    // whatever rate the run configures.
    private void Restep(double stepSeconds)
    {
        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (Retired(ref slots[i]))
            {
                continue;
            }

            // Both measured at the old length, before it is replaced. A loop has no time left to
            // rescale, but its clip position still has to be banked at the rate it was reached at.
            slots[i].TimeAtClock = ClipTimeAt(in slots[i]);
            if (!slots[i].Loop)
            {
                slots[i].RemainingSeconds = ClipTimeLeft(in slots[i]);
            }

            slots[i].Clock = _tick;
        }

        _stepSeconds = stepSeconds;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Live && !slots[i].Loop && !slots[i].Paused)
            {
                Arm(ref slots[i]);
            }
        }
    }

    private void SetBusPaused(AudioBus bus, bool paused)
    {
        int index = Register(bus);
        CollectionsMarshal.AsSpan(_buses)[index].Paused = paused;

        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (Retired(ref slots[i]))
            {
                continue;
            }

            bool held = EffectivelyPaused(in slots[i]);
            if (held == slots[i].Paused)
            {
                continue;
            }

            if (held)
            {
                Hold(ref slots[i], i);
            }
            else
            {
                Release(ref slots[i], i);
            }
        }
    }

    private void Hold(ref Slot slot, int index)
    {
        slot.TimeAtClock = ClipTimeAt(in slot);
        slot.RemainingSeconds = slot.Loop ? 0.0 : ClipTimeLeft(in slot);
        slot.Clock = _tick;
        slot.Paused = true;
        Raise(AudioCommandKind.Pause, index, in slot, 0f, 0f);
    }

    private void Release(ref Slot slot, int index)
    {
        slot.Paused = false;
        Arm(ref slot);
        Raise(AudioCommandKind.Resume, index, in slot, 0f, 0f);
    }

    // Where the voice is actually sounded from. A looping voice given a start at or past its loop
    // region's end is folded into the region: the host repeats the region from a start inside it
    // rather than playing on, so an unfolded start would displace every position read for the voice
    // from its first step onwards.
    private static double StartOf(in AudioPlayback playback)
    {
        AudioLoopRegion region = playback.Clip.LoopRegion;

        if (!playback.Loop || !region.HasRegion || playback.StartSeconds < region.EndSeconds)
        {
            return playback.StartSeconds;
        }

        return region.StartSeconds
            + ((playback.StartSeconds - region.StartSeconds) % (region.EndSeconds - region.StartSeconds));
    }

    // Fixes the tick a one-shot stops sounding on from the clip time it has left, which is the only
    // point that rounding happens: everything else moves the fractional time.
    private void Arm(ref Slot slot)
    {
        slot.Clock = _tick;
        slot.EndTick = slot.Loop ? long.MaxValue : _tick + TicksFor(slot.RemainingSeconds / slot.Pitch);
    }

    // The clip time this one-shot has left at the current tick. A held voice's clock does not run,
    // so what was measured when it was held is still what it has left.
    private double ClipTimeLeft(in Slot slot)
    {
        if (slot.Paused)
        {
            return slot.RemainingSeconds;
        }

        return Math.Max(0.0, slot.RemainingSeconds - ((_tick - slot.Clock) * _stepSeconds * slot.Pitch));
    }

    // Clip time this voice has consumed by the current tick, unwrapped: what a looping voice would
    // read if the clip ran on forever. A held voice's clock does not run, so the position banked when
    // it was held is where it still is.
    private double ClipTimeAt(in Slot slot)
    {
        if (slot.Paused)
        {
            return slot.TimeAtClock;
        }

        return slot.TimeAtClock + ((_tick - slot.Clock) * _stepSeconds * slot.Pitch);
    }

    private void Raise(AudioCommandKind kind, int index, in Slot slot, float gain, float pitch)
    {
        bool play = kind == AudioCommandKind.Play;

        _commands.Add(new AudioCommand(
            kind,
            Voice.Of(index, slot.Generation),
            play ? slot.Clip : default,
            play ? BusOf(slot.Bus) : default,
            gain,
            pitch,
            play || kind == AudioCommandKind.SetPan ? slot.Pan : 0f,
            play && slot.Loop,
            play ? slot.StartSeconds : 0.0));
    }

    // The master row is named by the empty string, which is not the name AudioBus.Master carries.
    private AudioBus BusOf(int index) => index == 0 ? AudioBus.Master : new AudioBus(_buses[index].Name);

    // Whether this slot holds nothing that can still be addressed, freeing it if it holds a one-shot
    // that has run out. An expired slot keeps Live until something reaches it, so every walk over the
    // table asks this rather than Live: left alone, an expired voice would answer a bus-wide change
    // and become resolvable again through its stale handle.
    private bool Retired(ref Slot slot)
    {
        if (!slot.Live)
        {
            return true;
        }

        if (!Expired(in slot))
        {
            return false;
        }

        Free(ref slot);

        return true;
    }

    // The first free or expired slot, else the oldest live one-shot, which is stopped to make room.
    private int Allocate()
    {
        Span<Slot> slots = _slots;

        for (int i = 0; i < slots.Length; i++)
        {
            if (Retired(ref slots[i]))
            {
                return i;
            }
        }

        int oldest = -1;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].Loop && (oldest < 0 || slots[i].StartTick < slots[oldest].StartTick))
            {
                oldest = i;
            }
        }

        if (oldest < 0)
        {
            return -1;
        }

        Raise(AudioCommandKind.Stop, oldest, in slots[oldest], 0f, 0f);
        Free(ref slots[oldest]);

        return oldest;
    }

    // Advancing the generation is what makes every handle to this slot stale. Wrapping it can only
    // collide with a handle held across 16 million reuses of one slot.
    private static void Free(ref Slot slot)
    {
        slot.Live = false;
        slot.Clip = default;
        slot.Generation = slot.Generation >= MaxGeneration ? 1 : slot.Generation + 1;
    }

    private bool TryResolve(Voice voice, out int index)
    {
        index = voice.Slot;
        if (voice.IsNone || index >= _slots.Length)
        {
            return false;
        }

        ref Slot slot = ref _slots[index];

        return slot.Live && slot.Generation == voice.Generation && !Expired(in slot);
    }

    private bool Expired(in Slot slot) => !slot.Loop && !slot.Paused && slot.EndTick <= _tick;

    private bool EffectivelyPaused(in Slot slot) =>
        slot.SelfPaused || _buses[0].Paused || (slot.Bus != 0 && _buses[slot.Bus].Paused);

    private float Gain(in Slot slot) =>
        _buses[0].Volume * (slot.Bus == 0 ? 1f : _buses[slot.Bus].Volume) * slot.Volume;

    private int Find(AudioBus bus)
    {
        string name = bus.Name ?? string.Empty;
        if (name.Length == 0)
        {
            return 0;
        }

        for (int i = 1; i < _buses.Count; i++)
        {
            if (string.Equals(_buses[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private int Register(AudioBus bus)
    {
        int index = Find(bus);
        if (index >= 0)
        {
            return index;
        }

        _buses.Add(new Bus(bus.Name));

        return _buses.Count - 1;
    }

    private long TicksFor(double seconds)
    {
        if (!(seconds > 0.0))
        {
            return 0;
        }

        double exact = seconds / _stepSeconds;

        return (long)Math.Ceiling(exact - (exact * TickTolerance));
    }

    internal static void RequireVolume(float volume, string parameterName)
    {
        if (!(volume >= 0f && volume <= 1f))
        {
            throw new ArgumentOutOfRangeException(parameterName, volume, "A volume is a linear amplitude in [0, 1].");
        }
    }

    internal static void RequirePitch(float pitch, string parameterName)
    {
        if (!(pitch > 0f) || float.IsInfinity(pitch))
        {
            throw new ArgumentOutOfRangeException(parameterName, pitch, "A pitch is a playback-rate multiplier, positive and finite.");
        }
    }

    internal static void RequirePan(float pan, string parameterName)
    {
        if (!(pan >= -1f && pan <= 1f))
        {
            throw new ArgumentOutOfRangeException(parameterName, pan, "A pan is a stereo position in [-1, 1], -1 hard left.");
        }
    }

    // Zero is legal whatever the clip, so a clip measured at no duration still plays from its start
    // and ends on the step it started.
    internal static void RequireStart(double startSeconds, double durationSeconds, string parameterName)
    {
        if (!(startSeconds >= 0.0) || double.IsInfinity(startSeconds) || (startSeconds > 0.0 && startSeconds >= durationSeconds))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                startSeconds,
                "A playback start is a clip time at or after zero and before the clip's duration.");
        }
    }

    // Checked whatever the playback loops, so a clip a game set a bad region on is refused at its
    // first play rather than its first looping one. AudioLoopRegion.HasRegion reads a malformed
    // region as no region at all, which is why the shape is tested here and not through it.
    internal static void RequireRegion(in AudioClip clip, string parameterName)
    {
        AudioLoopRegion region = clip.LoopRegion;

        if (region == AudioLoopRegion.None)
        {
            return;
        }

        if (!(region.StartSeconds >= 0.0)
            || !(region.EndSeconds > region.StartSeconds)
            || !(region.EndSeconds <= clip.DurationSeconds))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                region,
                "A loop region starts at or after zero, ends after it starts, and ends no later than the clip's duration.");
        }
    }

    private struct Bus(string name)
    {
        internal string Name = name;

        internal float Volume = 1f;

        internal bool Paused;
    }

    private struct Slot
    {
        internal AudioClip Clip;

        // Index into the bus table; 0 is master.
        internal int Bus;

        internal float Volume;

        internal float Pitch;

        internal float Pan;

        // Clip time the voice was played from, which is where its position starts.
        internal double StartSeconds;

        internal bool Loop;

        internal bool Live;

        // Paused in its own right, as against by its bus.
        internal bool SelfPaused;

        // Held, by either cause: the state the host has been told.
        internal bool Paused;

        internal int Generation;

        internal long StartTick;

        // The tick this one-shot stops sounding on; meaningless while held or looping.
        internal long EndTick;

        // The tick RemainingSeconds was last measured at.
        internal long Clock;

        // Clip seconds this one-shot had left at Clock, unrounded: rounding is the arming step's
        // alone, so pitch changes neither manufacture nor lose playback time. Meaningless looping.
        internal double RemainingSeconds;

        // Unwrapped clip time reached at Clock, for either kind of voice: the one piece of
        // bookkeeping a loop's position can be derived from, since RemainingSeconds is not kept for
        // one. Banked wherever Clock moves, so it is measured at the rate and pitch it was reached at.
        internal double TimeAtClock;
    }
}
