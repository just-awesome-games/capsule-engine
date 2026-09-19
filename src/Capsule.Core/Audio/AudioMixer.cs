using System.Runtime.InteropServices;

namespace Capsule.Audio;

/// <summary>
/// The run's sound: buses, their volumes and pause state, and the voices playing on them. Reached
/// as <c>Run.Audio</c> and held for the run. A voice survives a scene transition until something
/// stops it. A call taking a voice that has ended does nothing. Each step rewrites
/// <see cref="Commands"/> for the host to apply afterwards.
/// <para>
/// Voice lifetimes are computed from the clip's duration, the voice's pitch and the step length.
/// Nothing is read back from a device. A headless run reaches the same state as a windowed one.
/// </para>
/// </summary>
public sealed class AudioMixer
{
    /// <summary>How many voices may sound at once before <see cref="Play(in AudioPlayback)"/> steals one.</summary>
    public const int MaxVoices = 64;

    // Float step lengths make a duration that is a whole multiple of the step divide to a hair over its
    // tick count. This relative tolerance keeps such a duration from spilling into one more tick.
    private const double TickTolerance = 1e-6;

    private const int MaxGeneration = 0xFFFFFF;

    private readonly List<AudioCommand> _commands = [];

    // Index 0 is the master bus. It sits in the table like any other bus, which keeps gain resolution to
    // two multiplications with no name comparison.
    private readonly List<Bus> _buses = [new Bus(string.Empty)];

    private readonly Slot[] _slots = new Slot[MaxVoices];

    private long _tick;

    private double _stepSeconds = 1.0 / StepContext.DefaultStepHertz;

    private float _unfocusedVolume;

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
    /// The commands the last step raised, in the order they were raised. The next mixer call
    /// invalidates the span. A command raised outside a step is appended to the last step's list.
    /// </summary>
    public ReadOnlySpan<AudioCommand> Commands => CollectionsMarshal.AsSpan(_commands);

    /// <summary>
    /// The linear amplitude the windowed host applies to the output while the game's window is
    /// inactive, in [0, 1]. Defaults to 0. This is run state outside the command stream, so setting
    /// it raises no command.
    /// </summary>
    public float UnfocusedVolume
    {
        get => _unfocusedVolume;
        set
        {
            Guard.InUnit(value, nameof(value));
            _unfocusedVolume = value;
        }
    }

    /// <summary>This bus's own linear amplitude in [0, 1]. An unchanged bus reads 1.</summary>
    public float GetVolume(AudioBus bus)
    {
        int index = Find(bus);

        return index < 0 ? 1f : _buses[index].Volume;
    }

    /// <summary>
    /// Sets this bus's linear amplitude in [0, 1], registering the bus if it is new, and raises
    /// <see cref="AudioCommandKind.SetGain"/> for every live voice on it.
    /// <see cref="AudioBus.Master"/> covers every live voice. Bus volumes belong to the run and
    /// stand until they are set again.
    /// </summary>
    public void SetVolume(AudioBus bus, float volume)
    {
        Guard.InUnit(volume, nameof(volume));

        int index = Register(bus);
        CollectionsMarshal.AsSpan(_buses)[index].Volume = volume;

        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!ReclaimIfExpired(ref slots[i]) && (index == 0 || slots[i].Bus == index))
            {
                Raise(AudioCommandKind.SetGain, i, in slots[i], Gain(in slots[i]), 0f);
            }
        }
    }

    /// <summary>Whether this bus is paused in its own right. An untouched bus is not.</summary>
    public bool IsPaused(AudioBus bus)
    {
        int index = Find(bus);

        return index >= 0 && _buses[index].Paused;
    }

    /// <summary>
    /// Pauses this bus, registering it if it is new. Every voice whose effective state changes is
    /// held where it is and raises <see cref="AudioCommandKind.Pause"/>. Pausing
    /// <see cref="AudioBus.Master"/> pauses every voice.
    /// </summary>
    public void Pause(AudioBus bus) => SetBusPaused(bus, paused: true);

    /// <summary>
    /// Resumes this bus. A voice paused in its own right stays paused. Every other voice continues
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
    /// Starts one voice and raises <see cref="AudioCommandKind.Play"/> for it. A one-shot ends
    /// itself after <c>ceil(DurationSeconds / Pitch / step)</c> steps and a loop plays until it is
    /// stopped. A voice played onto a paused bus starts held.
    /// <para>
    /// With no slot free the oldest live one-shot is stolen and raises
    /// <see cref="AudioCommandKind.Stop"/> ahead of the new voice's play. If every live voice loops,
    /// nothing is stolen.
    /// </para>
    /// </summary>
    /// <returns>The voice started, or <see cref="Voice.None"/> when every voice is a live loop.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The playback's volume, pitch, pan or start is outside its range, or the clip carries a loop
    /// region that does not fit it.
    /// </exception>
    public Voice Play(in AudioPlayback playback)
    {
        Guard.InUnit(playback.Volume, nameof(playback));
        Guard.Positive(playback.Pitch, nameof(playback));
        Guard.InRange(playback.Pan, -1f, 1f, nameof(playback));
        RequireStart(playback.StartSeconds, playback.Clip.DurationSeconds, nameof(playback));
        RequireRegion(playback.Clip, nameof(playback));

        int index = Allocate();
        if (index < 0)
        {
            return Voice.None;
        }

        int bus = Register(playback.Bus);
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
        slot.TimeAtClock = start;
        Arm(ref slot);

        Raise(AudioCommandKind.Play, index, in slot, Gain(in slot), slot.Pitch);

        if (EffectivelyPaused(in slot))
        {
            Hold(ref slot, index);
        }

        return Voice.Of(index, slot.Generation);
    }

    /// <summary>Ends <paramref name="voice"/>, raising <see cref="AudioCommandKind.Stop"/> and freeing its slot.</summary>
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
    /// Holds <paramref name="voice"/> where it is, keeping the ticks it has left. A voice already
    /// paused in its own right is unchanged.
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

    /// <summary>Continues <paramref name="voice"/> from where it was held. A voice whose bus is still paused stays held and raises nothing.</summary>
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
    /// Sets this voice's own linear amplitude in [0, 1] and raises
    /// <see cref="AudioCommandKind.SetGain"/> with the gain that resolves to.
    /// </summary>
    public void SetVolume(Voice voice, float volume)
    {
        Guard.InUnit(volume, nameof(volume));

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
    /// time a one-shot has left carries across, and its end tick is recomputed at the new rate.
    /// </summary>
    public void SetPitch(Voice voice, float pitch)
    {
        Guard.Positive(pitch, nameof(pitch));

        if (!TryResolve(voice, out int index))
        {
            return;
        }

        ref Slot slot = ref _slots[index];

        // Bank the position at the old rate. Rescaling the rounded tick count would round up twice.
        Rebase(ref slot);
        slot.Pitch = pitch;

        if (!slot.Loop && !slot.Paused)
        {
            Arm(ref slot);
        }

        Raise(AudioCommandKind.SetPitch, index, in slot, 0f, pitch);
    }

    /// <summary>Sets where this voice sits between the speakers and raises <see cref="AudioCommandKind.SetPan"/>.</summary>
    /// <param name="pan">Stereo position in [-1, 1]: -1 hard left, 0 centred, 1 hard right.</param>
    /// <param name="voice">The voice to move.</param>
    public void SetPan(Voice voice, float pan)
    {
        Guard.InRange(pan, -1f, 1f, nameof(pan));

        if (!TryResolve(voice, out int index))
        {
            return;
        }

        ref Slot slot = ref _slots[index];
        slot.Pan = pan;
        Raise(AudioCommandKind.SetPan, index, in slot, 0f, 0f);
    }

    /// <summary>
    /// The clip time this voice is at on the step being taken, in seconds from the clip's start. A
    /// voice that is not live reads 0. The time advances in whole steps at the voice's pitch, holds
    /// while the voice is held, and wraps within a looping clip's region.
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
    /// Whether this voice still owns its slot: sounding, or held by its own pause or its bus's. This
    /// is ownership, not audibility, so ask it before restarting a sound that may already be going.
    /// <see cref="IsPlaying"/> and <see cref="IsPaused(Voice)"/> partition it.
    /// </summary>
    public bool IsLive(Voice voice) => TryResolve(voice, out _);

    /// <summary>Whether this voice is live and sounding: not held, and either looping or not yet finished.</summary>
    public bool IsPlaying(Voice voice) => TryResolve(voice, out int index) && !_slots[index].Paused;

    /// <summary>Whether this voice is live and held, by its own pause or by its bus's.</summary>
    public bool IsPaused(Voice voice) => TryResolve(voice, out int index) && _slots[index].Paused;

    // Clears the previous step's commands and moves the mixer's clock onto this step. A voice started
    // during the step expires against this step's tick and step length.
    internal void BeginStep(in StepContext context)
    {
        _commands.Clear();
        _tick = context.Tick;

        if (context.StepSeconds > 0.0 && context.StepSeconds != _stepSeconds)
        {
            ChangeStepLength(context.StepSeconds);
        }
    }

    // A live one-shot was armed against the step length in force when it started. Bank its remaining clip
    // time at the old length and re-arm at the new one, so the voice still lasts its clip's duration.
    private void ChangeStepLength(double stepSeconds)
    {
        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!ReclaimIfExpired(ref slots[i]))
            {
                Rebase(ref slots[i]);
            }
        }

        _stepSeconds = stepSeconds;

        for (int i = 0; i < slots.Length; i++)
        {
            if (!ReclaimIfExpired(ref slots[i]) && !slots[i].Loop && !slots[i].Paused)
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
            if (ReclaimIfExpired(ref slots[i]))
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
        Rebase(ref slot);
        slot.Paused = true;
        Raise(AudioCommandKind.Pause, index, in slot, 0f, 0f);
    }

    private void Release(ref Slot slot, int index)
    {
        // Bank the position while the voice is still held, or the held ticks count as played.
        Rebase(ref slot);
        slot.Paused = false;
        Arm(ref slot);
        Raise(AudioCommandKind.Resume, index, in slot, 0f, 0f);
    }

    // Where the voice is sounded from. A looping voice given a start at or past its loop region's end is
    // folded into the region, because the host repeats the region from a start inside it.
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

    // Banks the clip position this voice has reached, measured at the rate and step length that produced
    // it. A later change to either applies from this point on.
    private void Rebase(ref Slot slot)
    {
        slot.TimeAtClock = ClipTimeAt(in slot);
        slot.Clock = _tick;
    }

    // Fixes the tick a one-shot stops sounding on from the clip time it has left. This is the mixer's only
    // rounding. Every other path moves fractional time.
    private void Arm(ref Slot slot)
    {
        slot.Clock = _tick;
        slot.EndTick = slot.Loop ? long.MaxValue : _tick + TicksFor(ClipTimeLeft(in slot) / slot.Pitch);
    }

    // The clip time this one-shot has left at the current tick.
    private double ClipTimeLeft(in Slot slot) => Math.Max(0.0, slot.Clip.DurationSeconds - ClipTimeAt(in slot));

    // Clip time this voice has consumed by the current tick, unwrapped: what a looping voice would read if
    // the clip ran forever. A held voice's clock does not run.
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

    // The master row is keyed by the empty string, not by the name AudioBus.Master carries.
    private AudioBus BusOf(int index) => index == 0 ? AudioBus.Master : new AudioBus(_buses[index].Name);

    // Whether this slot holds nothing addressable, freeing it if it holds a one-shot that has run out. An
    // expired slot keeps Live until something reaches it, so walks over the table ask this instead of Live.
    // An expired voice left in place would respond to a bus-wide change.
    private bool ReclaimIfExpired(ref Slot slot)
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

    // The first free or expired slot. Failing that, the oldest live one-shot, stopped to make room.
    private int Allocate()
    {
        Span<Slot> slots = _slots;

        for (int i = 0; i < slots.Length; i++)
        {
            if (ReclaimIfExpired(ref slots[i]))
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

    // Advancing the generation makes existing handles to this slot stale. The wrap collides only with a
    // handle held across 16 million reuses of the same slot.
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

    // Zero is legal for any clip. A clip of no duration plays from its start and ends immediately.
    private static void RequireStart(double startSeconds, double durationSeconds, string parameterName)
    {
        if (!(startSeconds >= 0.0) || double.IsInfinity(startSeconds) || (startSeconds > 0.0 && startSeconds >= durationSeconds))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                startSeconds,
                "Expected a clip time at or after zero and before the clip's duration.");
        }
    }

    // Checked even for a playback that does not loop. A bad region is refused at the clip's first play.
    // HasRegion reads a malformed region as no region, so the shape is tested directly here.
    private static void RequireRegion(in AudioClip clip, string parameterName)
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
                "Expected a loop region at or after zero, ending after its start and no later than the clip.");
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

        // Index into the bus table. Index 0 is master.
        internal int Bus;

        internal float Volume;

        internal float Pitch;

        internal float Pan;

        // Clip time the voice was played from.
        internal double StartSeconds;

        internal bool Loop;

        internal bool Live;

        // Paused in its own right, not by its bus.
        internal bool SelfPaused;

        // Held, by either cause: the state the host has been told.
        internal bool Paused;

        internal int Generation;

        internal long StartTick;

        // The tick this one-shot stops sounding on. Meaningless while held or looping.
        internal long EndTick;

        // The tick TimeAtClock was last measured at.
        internal long Clock;

        // Unwrapped clip time reached at Clock, kept unrounded so pitch changes neither add nor lose
        // playback time.
        internal double TimeAtClock;
    }
}
