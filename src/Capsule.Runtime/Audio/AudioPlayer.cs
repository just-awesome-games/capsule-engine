using Capsule.Audio;

namespace Capsule.Runtime.Audio;

// The game thread's side of the mix: one entry per mixer slot holding the generation it was last
// played at, the translation of each step's commands into the mix thread's, and the retirement of
// the voices the mix thread reports ended. A bus's gain and effects are its state, not a hold on its
// voices, and pass the host's suspension untouched.
//
// The mixer in Core is the authority on what is playing, and nothing here is read back into it. A
// command naming a generation the table does not hold addresses a voice that has been stolen,
// expired or stopped, and is dropped. The mixer raises no Stop for a one-shot that ran out, and the
// mix thread's report retires it here instead.
//
// A warm play of a resident clip, a stop or a change allocates nothing. A streamed play opens the
// clip's file and a decoder over it, because a file handle is per play. Meeting a bus for the first
// time allocates its buffer, and changing a bus's echo delay allocates the echo's line.
internal sealed class AudioPlayer
{
    // Commands one step of 64 voices raises are a few hundred at most. The mix thread drains every
    // block, and this holds many steps of the worst case.
    internal const int CommandCapacity = 4096;

    // Every voice played is reported ended once. Between two drains that is at most the plays sent
    // since, the voices sounding and the voices fading out beside them.
    internal const int EndedCapacity = CommandCapacity + (2 * AudioMixer.MaxVoices);

    // How long a full command ring is waited on for the mix thread to drain before the run fails.
    private static readonly TimeSpan FullPatience = TimeSpan.FromSeconds(1);

    private readonly SoundStore _sounds;
    private readonly AudioStreamer _streams;
    private readonly AudioRing<MixCommand> _commands;
    private readonly AudioRing<VoiceEnded> _ended;
    private readonly Entry[] _slots = new Entry[AudioMixer.MaxVoices];

    // Bus names to the mixer's bus indices, 0 being the master bus.
    private readonly Dictionary<string, int> _buses = new(StringComparer.Ordinal);
    private MixBus[] _mixBuses = [];

    // The echo delay each bus's line was last handed for, by index with 0 the master, or 0 for none.
    private float[] _lineDelays = new float[1];

    // The host's suspension, a layer over each voice's own pause. While it is on, every voice is held
    // whatever the game asked, and what the game asks meanwhile is remembered instead of applied. A
    // resume restores the voices the game has playing.
    private bool _suspended;

    // Whether the mix has stopped for good, after which nothing is posted or opened.
    private bool _silenced;

    internal AudioPlayer(SoundStore sounds, AudioStreamer streams, AudioRing<MixCommand> commands, AudioRing<VoiceEnded> ended)
    {
        _sounds = sounds;
        _streams = streams;
        _commands = commands;
        _ended = ended;
    }

    // Holds every voice, including one the game paused, whose pause outlives the suspension. A voice
    // played while suspended starts held and begins on Resume.
    internal void Suspend()
    {
        if (_suspended)
        {
            return;
        }

        _suspended = true;
        Hold(MixCommandKind.Pause);
    }

    // Lets every voice the game has playing sound again. A voice the game paused stays paused.
    internal void Resume()
    {
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        Hold(MixCommandKind.Resume);
    }

    // Stops every voice the game has, paused or held, as a replay's fast-forward drops the sounds it
    // started unheard. A held voice has faded to nothing and ends without a sound.
    internal void StopAll()
    {
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            ref Entry entry = ref _slots[slot];
            if (entry.Live)
            {
                Post(new MixCommand(MixCommandKind.Stop, slot, entry.Generation));
                entry = default;
            }
        }
    }

    // Once the mix thread has faulted. The run plays on silently, and every stream goes back to its
    // worker's pool, because no mix reads one again.
    internal void Silence()
    {
        _silenced = true;
        _streams.RetireAll();
    }

    internal void Apply(ReadOnlySpan<AudioCommand> commands)
    {
        if (_silenced)
        {
            return;
        }

        foreach (AudioCommand command in commands)
        {
            if (command.Kind == AudioCommandKind.SetBusGain)
            {
                Post(new MixCommand(MixCommandKind.SetBusGain, 0, 0, Gain: command.Gain) { Bus = Bus(command.Bus) });

                continue;
            }

            if (command.Kind == AudioCommandKind.SetBusEffects)
            {
                Post(Effects(command));

                continue;
            }

            int slot = command.Voice.Slot;
            if ((uint)slot >= (uint)_slots.Length)
            {
                continue;
            }

            int generation = command.Voice.Generation;
            if (command.Kind == AudioCommandKind.Play)
            {
                // A steal raises the old voice's Stop first, but an expired one-shot raises nothing.
                // The mixer replaces whatever the slot still holds.
                Post(Play(command));
                _slots[slot] = new Entry(generation);

                continue;
            }

            ref Entry entry = ref _slots[slot];
            if (!entry.Live || entry.Generation != generation)
            {
                continue;
            }

            switch (command.Kind)
            {
                case AudioCommandKind.Stop:
                    entry = default;
                    Post(new MixCommand(MixCommandKind.Stop, slot, generation));
                    break;

                case AudioCommandKind.Pause:
                    entry.Paused = true;
                    if (!_suspended)
                    {
                        Post(new MixCommand(MixCommandKind.Pause, slot, generation));
                    }

                    break;

                case AudioCommandKind.Resume:
                    entry.Paused = false;
                    if (!_suspended)
                    {
                        Post(new MixCommand(MixCommandKind.Resume, slot, generation));
                    }

                    break;

                case AudioCommandKind.SetGain:
                    Post(new MixCommand(MixCommandKind.SetGain, slot, generation, Gain: command.Gain));
                    break;

                case AudioCommandKind.SetPitch:
                    Post(new MixCommand(MixCommandKind.SetPitch, slot, generation, Pitch: command.Pitch));
                    break;

                case AudioCommandKind.SetPan:
                    Post(new MixCommand(MixCommandKind.SetPan, slot, generation, Pan: command.Pan));
                    break;
            }
        }
    }

    // Once a frame, on the game thread. Retires what the mix thread reports ended: a one-shot that
    // ran out leaves the table, and a stream goes back to its worker's pool.
    internal void Update()
    {
        if (_ended.Overflowed)
        {
            throw new InvalidOperationException(
                $"The mix thread reported more ended voices than its ring holds ({_ended.Capacity}), and a streamed voice's decoder may never be released. This is an engine fault: raise AudioPlayer.EndedCapacity.");
        }

        while (_ended.TryRead(out VoiceEnded ended))
        {
            ended.Stream?.Retire();

            ref Entry entry = ref _slots[ended.Slot];
            if (entry.Live && entry.Generation == ended.Generation)
            {
                entry = default;
            }
        }
    }

    private MixCommand Play(in AudioCommand command)
    {
        AudioClip clip = command.Clip;
        MixCommand play = new(MixCommandKind.Play, command.Voice.Slot, command.Voice.Generation, command.Gain, command.Pitch, command.Pan)
        {
            Bus = Bus(command.Bus),
            Loop = command.Loop,
            Held = _suspended,
        };

        if (clip.IsStreamed)
        {
            AudioStream stream = _streams.Play(_sounds.Open(clip), clip, command.Loop, command.StartSeconds);

            // The decoder has already sought to the start. The frame only tells the mixer to fade in.
            return play with { Stream = stream, StartFrame = command.StartSeconds > 0.0 ? 1 : 0 };
        }

        PcmAudio samples = _sounds.Get(clip);
        bool region = command.Loop && clip.LoopRegion.HasRegion;

        // Rounded to the frame the clip time names, as the build measured the region.
        return play with
        {
            Samples = samples,
            LoopStart = region ? (long)Math.Round(clip.LoopRegion.StartSeconds * samples.SampleRate) : 0,
            LoopEnd = region ? (long)Math.Round(clip.LoopRegion.EndSeconds * samples.SampleRate) : samples.Frames,
            StartFrame = (long)Math.Round(command.StartSeconds * samples.SampleRate),
        };
    }

    // A line is allocated only when the delay changes, which cuts the repeats ringing on the old one. A
    // fade or an equal set keeps the line the bus has, and a delay of 0 drops it.
    private MixCommand Effects(in AudioCommand command)
    {
        int bus = Bus(command.Bus);
        EffectLevels levels = command.Effects;
        float delay = levels.EchoDelaySeconds;
        float[]? line = null;

        if (delay != _lineDelays[bus])
        {
            _lineDelays[bus] = delay;
            int frames = Math.Max(1, (int)Math.Round(delay * (double)VoiceMixer.SampleRate));
            line = delay > 0f ? new float[frames * VoiceMixer.Channels] : null;
        }

        return new MixCommand(MixCommandKind.SetBusEffects, 0, 0) { Bus = bus, Effects = levels, Line = line };
    }

    // The mixer's index for bus, null being the master. A bus heard for the first time hands the mixer a
    // larger set of buses, after its parent's.
    private int Bus(AudioBus? bus)
    {
        if (bus?.Parent is null)
        {
            return 0;
        }

        return _buses.TryGetValue(bus.Name, out int index) ? index : Register(bus.Name, Bus(bus.Parent));
    }

    private int Register(string name, int parent)
    {
        MixBus[] buses = [.. _mixBuses, new MixBus(parent)];
        _mixBuses = buses;
        Array.Resize(ref _lineDelays, buses.Length + 1);

        Post(new MixCommand(MixCommandKind.Buses, 0, 0) { Buses = buses });
        _buses.Add(name, buses.Length);

        return buses.Length;
    }

    private void Hold(MixCommandKind kind)
    {
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            Entry entry = _slots[slot];
            if (entry.Live && !entry.Paused)
            {
                Post(new MixCommand(kind, slot, entry.Generation));
            }
        }
    }

    // A full ring would drop the command, and a dropped Stop would play a voice forever. The mix thread
    // drains it every block. One that has not drained it in a second has stalled, and the run fails
    // here rather than play on wrong. A mix thread that faulted silences the player by the frame's end,
    // and the ring holds many frames of commands. A fault never fills it.
    private void Post(in MixCommand command)
    {
        if (_silenced)
        {
            return;
        }

        if (_commands.TryWrite(command))
        {
            return;
        }

        long deadline = Environment.TickCount64 + (long)FullPatience.TotalMilliseconds;
        while (!_commands.TryWrite(command))
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new InvalidOperationException(
                    $"The audio command ring is full ({_commands.Capacity} commands) and the mix thread has not drained it. This is an engine fault: the mix thread has stalled.");
            }

            Thread.Sleep(1);
        }
    }

    private struct Entry(int generation)
    {
        internal int Generation = generation;

        internal bool Live = true;

        // Whether the game itself paused this voice. The host's suspension is layered over it.
        internal bool Paused;
    }
}
