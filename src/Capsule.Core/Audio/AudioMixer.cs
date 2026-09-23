using System.Runtime.InteropServices;
using Capsule.Animation;

namespace Capsule.Audio;

/// <summary>
/// The run's sound: buses, their volumes and pause state, and the voices playing on them. Reached
/// as <c>Run.Audio</c> and held for the run.
/// </summary>
/// <remarks>
/// A voice survives a scene transition until something stops it. A call taking a voice that has
/// ended does nothing. The host applies what a step changed after that step.
/// <para>
/// Voice lifetimes are computed from the clip's duration, the voice's pitch and the step length.
/// Nothing is read back from a device. A headless run reaches the same state as a windowed one.
/// </para>
/// </remarks>
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

    // Whether the last step's input read unfocused.
    private bool _unfocused;

    // An idle mixer: master at volume 1, no other bus registered, nothing playing.
    internal AudioMixer()
    {
        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].Generation = 1;
        }
    }

    // The commands the last step raised, in the order they were raised. The next mixer call
    // invalidates the span. A command raised outside a step is appended to the last step's list.
    internal ReadOnlySpan<AudioCommand> Commands => CollectionsMarshal.AsSpan(_commands);

    /// <summary>
    /// The linear amplitude every voice is scaled by while the run's input reads unfocused, in [0, 1].
    /// Defaults to 0, which silences an unfocused game.
    /// </summary>
    /// <remarks>
    /// It follows <see cref="Input.InputState.HasWindowFocus"/>, and a headless run reaches the same gains. To
    /// pause music instead of ducking it, set this to 1 and pause the bus on
    /// <see cref="Input.InputState.WindowFocusLost"/>.
    /// </remarks>
    public float UnfocusedVolume
    {
        get => _unfocusedVolume;
        set
        {
            Guard.InUnit(value, nameof(value));
            _unfocusedVolume = value;
            if (_unfocused)
            {
                RaiseGains(0);
            }
        }
    }

    /// <summary>This bus's own linear amplitude in [0, 1]. An unchanged bus reads 1.</summary>
    public float GetVolume(AudioBus bus)
    {
        int index = Find(bus);

        return index < 0 ? 1f : _buses[index].Volume;
    }

    /// <summary>
    /// Sets this bus's linear amplitude in [0, 1], registering the bus if it is new.
    /// </summary>
    /// <remarks>
    /// <see cref="AudioBus.Master"/> scales every live voice. This cancels any ramp
    /// <see cref="FadeVolume(AudioBus, float, float, Ease)"/> started on the bus.
    /// </remarks>
    public void SetVolume(AudioBus bus, float volume)
    {
        Guard.InUnit(volume, nameof(volume));

        int index = Register(bus);
        ref Bus bus0 = ref CollectionsMarshal.AsSpan(_buses)[index];
        bus0.Volume = volume;
        bus0.Ramp.Active = false;

        RaiseGains(index);
    }

    /// <summary>
    /// Ramps this bus's own linear amplitude from where it is to <paramref name="volume"/> over
    /// <paramref name="seconds"/> on <paramref name="ease"/>, registering the bus if it is new. A
    /// duration of zero sets the volume at once, exactly as <see cref="SetVolume(AudioBus, float)"/> does.
    /// </summary>
    /// <remarks>
    /// A ramp does not move on this call. Its first move lands on the next step.
    /// </remarks>
    public void FadeVolume(AudioBus bus, float volume, float seconds, Ease ease = Ease.Linear)
    {
        Guard.InUnit(volume, nameof(volume));
        Guard.RequireSeconds(seconds, nameof(seconds));
        Guard.RequireEase(ease, nameof(ease));

        int index = Register(bus);
        ref Bus bus0 = ref CollectionsMarshal.AsSpan(_buses)[index];
        StartRamp(ref bus0.Ramp, bus0.Volume, volume, seconds, ease);

        if (bus0.Ramp.Active)
        {
            return;
        }

        bus0.Volume = volume;

        RaiseGains(index);
    }

    /// <summary>Whether this bus is paused in its own right. An untouched bus is not.</summary>
    public bool IsPaused(AudioBus bus)
    {
        int index = Find(bus);

        return index >= 0 && _buses[index].Paused;
    }

    /// <summary>Pauses this bus, registering it if it is new.</summary>
    /// <remarks>
    /// Every voice on the bus is held where it is. Pausing <see cref="AudioBus.Master"/> pauses
    /// every voice.
    /// </remarks>
    public void Pause(AudioBus bus) => SetBusPaused(bus, paused: true);

    /// <summary>
    /// Resumes this bus, registering it if it is new. A voice still held by its own pause, its bus
    /// or the master stays paused.
    /// </summary>
    /// <remarks>Every other voice the bus held continues from where it was held.</remarks>
    public void Resume(AudioBus bus) => SetBusPaused(bus, paused: false);

    /// <summary>Plays <paramref name="clip"/> once on <see cref="AudioBus.Master"/> at full volume and unit pitch.</summary>
    /// <returns>The voice started, or <see cref="Voice.None"/> when every voice is a live loop.</returns>
    public Voice Play(AudioClip clip) => Play(new AudioPlayback(clip));

    /// <summary>Plays <paramref name="clip"/> once on <paramref name="bus"/> at full volume and unit pitch.</summary>
    /// <returns>The voice started, or <see cref="Voice.None"/> when every voice is a live loop.</returns>
    public Voice Play(AudioClip clip, AudioBus bus) => Play(new AudioPlayback(clip) { Bus = bus });

    /// <summary>
    /// Starts one voice. A one-shot ends itself after <c>ceil((DurationSeconds - StartSeconds) /
    /// Pitch / step)</c> steps and a loop plays until it is stopped.
    /// </summary>
    /// <remarks>
    /// A voice played onto a paused bus starts held.
    /// <para>
    /// With no slot free the oldest live one-shot is stopped and its slot reused. If every live
    /// voice loops, nothing is stolen.
    /// </para>
    /// </remarks>
    /// <returns>The voice started, or <see cref="Voice.None"/> when every voice is a live loop.</returns>
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

    /// <summary>
    /// Plays <paramref name="to"/> with its own volume forced to 0, ramps it up to
    /// <see cref="AudioPlayback.Volume"/> over <paramref name="seconds"/> on <see cref="Ease.OutSine"/>,
    /// and fades <paramref name="from"/> out over the same span on <see cref="Ease.InSine"/>. A
    /// <paramref name="from"/> that is <see cref="Voice.None"/> or already ended is a plain fade-in.
    /// </summary>
    /// <remarks>The two hold equal power throughout.</remarks>
    /// <returns>The voice <paramref name="to"/> started, or <see cref="Voice.None"/> when the mixer had none to give, leaving <paramref name="from"/> untouched.</returns>
    public Voice CrossFade(Voice from, in AudioPlayback to, float seconds)
    {
        Guard.InUnit(to.Volume, nameof(to));
        Guard.RequireSeconds(seconds, nameof(seconds));

        Voice started = Play(to with { Volume = 0f });
        if (started.IsNone)
        {
            return Voice.None;
        }

        FadeVolume(started, to.Volume, seconds, Ease.OutSine);

        if (TryResolve(from, out int fromIndex))
        {
            FadeStop(fromIndex, seconds, Ease.InSine);
        }

        return started;
    }

    /// <summary>Ends <paramref name="voice"/> and frees its slot.</summary>
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
    /// Ramps this voice's own amplitude to 0 on <see cref="Ease.Linear"/> over <paramref name="seconds"/>,
    /// then stops the voice and frees its slot on the landing tick. A
    /// duration of zero stops the voice at once, as <see cref="Stop(Voice)"/> does.
    /// </summary>
    /// <remarks>
    /// <see cref="IsLive(Voice)"/> reads true until the landing tick. <see cref="Stop(Voice)"/> during
    /// the fade stops at once instead.
    /// </remarks>
    public void Stop(Voice voice, float seconds)
    {
        Guard.RequireSeconds(seconds, nameof(seconds));

        if (TryResolve(voice, out int index))
        {
            FadeStop(index, seconds, Ease.Linear);
        }
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
    /// Sets this voice's own linear amplitude in [0, 1]. This cancels any ramp
    /// <see cref="FadeVolume(Voice, float, float, Ease)"/> or <see cref="Stop(Voice, float)"/> started
    /// on the voice, including a pending fade-stop.
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
        slot.Ramp.Active = false;
        slot.FadeStops = false;
        Raise(AudioCommandKind.SetGain, index, in slot, Gain(in slot), 0f);
    }

    /// <summary>
    /// Ramps this voice's own linear amplitude from where it is to <paramref name="volume"/> over
    /// <paramref name="seconds"/> on <paramref name="ease"/>, replacing any ramp already on the voice,
    /// including a pending fade-stop. A voice that resolves to nothing does nothing, and a duration of
    /// zero sets the volume at once, exactly as <see cref="SetVolume(Voice, float)"/> does.
    /// </summary>
    /// <remarks>
    /// A ramp does not move on this call. Its first move lands on the next step.
    /// </remarks>
    public void FadeVolume(Voice voice, float volume, float seconds, Ease ease = Ease.Linear)
    {
        Guard.InUnit(volume, nameof(volume));
        Guard.RequireSeconds(seconds, nameof(seconds));
        Guard.RequireEase(ease, nameof(ease));

        if (!TryResolve(voice, out int index))
        {
            return;
        }

        ref Slot slot = ref _slots[index];
        slot.FadeStops = false;
        StartRamp(ref slot.Ramp, slot.Volume, volume, seconds, ease);

        if (!slot.Ramp.Active)
        {
            slot.Volume = volume;
            Raise(AudioCommandKind.SetGain, index, in slot, Gain(in slot), 0f);
        }
    }

    /// <summary>
    /// Sets this voice's playback rate. The clip
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

    /// <summary>Sets where this voice sits between the speakers.</summary>
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
    /// voice that is not live reads 0.
    /// </summary>
    /// <remarks>
    /// The time advances in whole steps at the voice's pitch, holds while the voice is held, and
    /// wraps within a looping clip's region.
    /// </remarks>
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
    /// Whether this voice still owns its slot: sounding, or held by its own pause or its bus's. It
    /// reports ownership, not audibility.
    /// </summary>
    /// <remarks>
    /// Ask it before restarting a sound that may already be going. <see cref="IsPlaying"/> and
    /// <see cref="IsPaused(Voice)"/> partition it.
    /// </remarks>
    public bool IsLive(Voice voice) => TryResolve(voice, out _);

    /// <summary>Whether this voice is live and sounding: not held, and either looping or not yet finished.</summary>
    public bool IsPlaying(Voice voice) => TryResolve(voice, out int index) && !_slots[index].Paused;

    /// <summary>Whether this voice is live and held, by its own pause or by its bus's.</summary>
    public bool IsPaused(Voice voice) => TryResolve(voice, out int index) && _slots[index].Paused;

    // Clears the previous step's commands and moves the mixer's clock onto this step. A voice started
    // during the step expires against this step's tick and step length.
    // The mixer used to be lazy: BeginStep cleared the commands and moved the clock, and everything
    // else was derived when something asked. It now does one allocation-free walk over the bus table
    // and the slots every step, advancing whatever ramp is active and raising the gain a moved one
    // lands on.
    internal void BeginStep(in StepContext context)
    {
        _commands.Clear();
        _tick = context.Tick;

        if (context.StepSeconds > 0.0 && context.StepSeconds != _stepSeconds)
        {
            ChangeStepLength(context.StepSeconds);
        }

        Span<Bus> buses = CollectionsMarshal.AsSpan(_buses);
        for (int i = 0; i < buses.Length; i++)
        {
            buses[i].Moved = AdvanceRamp(ref buses[i].Ramp, ref buses[i].Volume, _tick);
        }

        bool focusMoved = _unfocused == context.Input.HasWindowFocus;
        _unfocused = !context.Input.HasWindowFocus;
        bool masterMoved = buses[0].Moved || focusMoved;

        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (ReclaimIfExpired(ref slots[i]))
            {
                continue;
            }

            ref Slot slot = ref slots[i];
            bool slotMoved = AdvanceRamp(ref slot.Ramp, ref slot.Volume, _tick);
            bool landed = slotMoved && !slot.Ramp.Active;

            if (slot.FadeStops && landed)
            {
                Raise(AudioCommandKind.Stop, i, in slot, 0f, 0f);
                Free(ref slot);
                continue;
            }

            bool busMoved = masterMoved || (slot.Bus != 0 && buses[slot.Bus].Moved);
            if (slotMoved || busMoved)
            {
                Raise(AudioCommandKind.SetGain, i, in slot, Gain(in slot), 0f);
            }
        }
    }

    // A live one-shot was armed against the step length in force when it started. Bank its remaining clip
    // time at the old length and re-arm at the new one, so the voice still lasts its clip's duration. A
    // ramp is rebased the same way, to elapsed and remaining wall-clock seconds at the old length and
    // back to ticks at the new one. This preserves the eased fraction it has reached.
    private void ChangeStepLength(double stepSeconds)
    {
        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!ReclaimIfExpired(ref slots[i]))
            {
                Rebase(ref slots[i]);
                RebaseRamp(ref slots[i].Ramp, _stepSeconds, stepSeconds);
            }
        }

        Span<Bus> buses = CollectionsMarshal.AsSpan(_buses);
        for (int i = 0; i < buses.Length; i++)
        {
            RebaseRamp(ref buses[i].Ramp, _stepSeconds, stepSeconds);
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

    // Starts a ramp from its current value to a new one, TicksFor(seconds) ticks from the current tick.
    // A zero-tick ramp leaves Active false. The caller applies the target at once instead.
    private void StartRamp(ref Ramp ramp, float from, float to, float seconds, Ease ease)
    {
        long ticks = TicksFor(seconds);
        ramp.From = from;
        ramp.To = to;
        ramp.StartTick = _tick;
        ramp.EndTick = _tick + ticks;
        ramp.Ease = ease;
        ramp.Active = ticks > 0;
    }

    // Moves an active ramp to this tick, writing the eased value into the field it owns, and clears the
    // ramp on the tick it lands. Returns whether the value changed, which is false for an inactive ramp.
    private static bool AdvanceRamp(ref Ramp ramp, ref float value, long tick)
    {
        if (!ramp.Active)
        {
            return false;
        }

        if (tick >= ramp.EndTick)
        {
            value = ramp.To;
            ramp.Active = false;

            return true;
        }

        float fraction = (float)(tick - ramp.StartTick) / (ramp.EndTick - ramp.StartTick);
        value = ramp.From + ((ramp.To - ramp.From) * Easing.Apply(ramp.Ease, fraction));

        return true;
    }

    // Rebases an active ramp's ticks from the old step length to the new one, at the current tick,
    // preserving the wall-clock seconds elapsed and remaining.
    private void RebaseRamp(ref Ramp ramp, double oldStepSeconds, double newStepSeconds)
    {
        if (!ramp.Active)
        {
            return;
        }

        double elapsed = (_tick - ramp.StartTick) * oldStepSeconds;
        double remaining = (ramp.EndTick - _tick) * oldStepSeconds;

        ramp.EndTick = _tick + TicksAt(remaining, newStepSeconds);
        ramp.StartTick = _tick - TicksAt(elapsed, newStepSeconds);
    }

    // Ramps a voice to 0 over seconds on ease, then stops it on the landing tick. Shared by Stop(Voice,
    // float) and the outgoing half of CrossFade, which differ only in the curve.
    private void FadeStop(int index, float seconds, Ease ease)
    {
        ref Slot slot = ref _slots[index];

        if (TicksFor(seconds) == 0)
        {
            Raise(AudioCommandKind.Stop, index, in slot, 0f, 0f);
            Free(ref slot);

            return;
        }

        StartRamp(ref slot.Ramp, slot.Volume, 0f, seconds, ease);
        slot.FadeStops = true;
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
        slot.Ramp.Active = false;
        slot.FadeStops = false;
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
        (_unfocused ? _unfocusedVolume : 1f) * _buses[0].Volume * (slot.Bus == 0 ? 1f : _buses[slot.Bus].Volume) * slot.Volume;

    // Raises the gain of every live voice on the bus at this index, or of every live voice for master.
    private void RaiseGains(int bus)
    {
        Span<Slot> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!ReclaimIfExpired(ref slots[i]) && (bus == 0 || slots[i].Bus == bus))
            {
                Raise(AudioCommandKind.SetGain, i, in slots[i], Gain(in slots[i]), 0f);
            }
        }
    }

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

    private long TicksFor(double seconds) => TicksAt(seconds, _stepSeconds);

    private static long TicksAt(double seconds, double stepSeconds)
    {
        if (!(seconds > 0.0))
        {
            return 0;
        }

        double exact = seconds / stepSeconds;

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

        internal Ramp Ramp;

        // Whether this step's BeginStep walk moved Volume, by SetVolume or by the ramp. Recomputed every
        // step, never accumulated.
        internal bool Moved;
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

        internal Ramp Ramp;

        // Whether the active ramp is a fade to 0 that stops and frees this slot on its landing tick.
        internal bool FadeStops;
    }

    // A linear-time ramp on one float field, stepped by BeginStep. From and To are the values at
    // StartTick and EndTick; between them the field holds From plus the eased fraction of their span.
    private struct Ramp
    {
        internal float From;

        internal float To;

        internal long StartTick;

        internal long EndTick;

        internal Ease Ease;

        internal bool Active;
    }
}
