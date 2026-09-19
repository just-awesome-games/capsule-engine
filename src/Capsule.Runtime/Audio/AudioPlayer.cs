using Capsule.Audio;

namespace Capsule.Runtime.Audio;

// The host's voice table: one entry per mixer slot, holding the generation the slot was last played
// at and the voice sounding for it.
//
// The mixer is the authority on what is playing, and nothing here is read back into it. A command
// naming a generation the table does not hold addresses a voice that has been stolen, expired or
// stopped, and is dropped. The mixer raises no Stop for a one-shot that ran out, and a finished voice
// is retired here instead.
internal sealed class AudioPlayer(SoundStore sounds) : IDisposable
{
    private readonly Entry[] _slots = new Entry[AudioMixer.MaxVoices];

    // The host's suspension, a layer over each voice's own pause. While it is on, every voice is held
    // whatever the game asked, and what the game asks meanwhile is remembered instead of applied, so a
    // resume restores the voices the game has playing.
    private bool _suspended;

    // Holds every voice, including one the game paused, whose pause outlives the suspension. A
    // voice played while suspended starts held and begins on Resume.
    internal void Suspend()
    {
        if (_suspended)
        {
            return;
        }

        _suspended = true;
        foreach (ref Entry entry in _slots.AsSpan())
        {
            if (entry.Voice is { } voice && !entry.Paused)
            {
                voice.Pause();
            }
        }
    }

    // Lets every voice the game has playing sound again. A voice the game paused stays paused.
    internal void Resume()
    {
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        foreach (ref Entry entry in _slots.AsSpan())
        {
            if (entry.Voice is { } voice && !entry.Paused)
            {
                voice.Resume();
            }
        }
    }

    internal void Apply(ReadOnlySpan<AudioCommand> commands)
    {
        foreach (AudioCommand command in commands)
        {
            int slot = command.Voice.Slot;
            if ((uint)slot >= (uint)_slots.Length)
            {
                continue;
            }

            if (command.Kind == AudioCommandKind.Play)
            {
                // A steal raises the old voice's Stop first, but an expired one-shot raises
                // nothing, so the slot may still hold a voice the device has already finished.
                Retire(slot);

                IAudioVoice started = sounds.Play(
                    command.Clip,
                    command.Gain,
                    command.Pitch,
                    command.Pan,
                    command.Loop,
                    command.StartSeconds);
                _slots[slot] = new Entry(command.Voice.Generation, started);
                if (_suspended)
                {
                    started.Pause();
                }

                continue;
            }

            ref Entry entry = ref _slots[slot];
            if (entry.Voice is not { } voice || entry.Generation != command.Voice.Generation)
            {
                continue;
            }

            switch (command.Kind)
            {
                case AudioCommandKind.Stop:
                    Retire(slot);
                    break;

                case AudioCommandKind.Pause:
                    entry.Paused = true;
                    voice.Pause();
                    break;

                case AudioCommandKind.Resume:
                    entry.Paused = false;
                    if (!_suspended)
                    {
                        voice.Resume();
                    }

                    break;

                case AudioCommandKind.SetGain:
                    voice.SetGain(command.Gain);
                    break;

                case AudioCommandKind.SetPitch:
                    voice.SetPitch(command.Pitch);
                    break;

                case AudioCommandKind.SetPan:
                    voice.SetPan(command.Pan);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown audio command kind '{command.Kind}'.");
            }
        }
    }

    // Every frame, including one that runs no step. A streaming voice hands the device its next buffers
    // here, and the queue it feeds drains at display rate, not step rate.
    internal void Update()
    {
        foreach (ref Entry entry in _slots.AsSpan())
        {
            entry.Voice?.Update();
        }

        // Done here instead of on each step's Apply. A voice the device finished is retired on the
        // frame that noticed, and a frame that ran eight steps walks the table once.
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            if (_slots[slot].Voice is { Finished: true })
            {
                Retire(slot);
            }
        }
    }

    public void Dispose()
    {
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            Retire(slot);
        }
    }

    private void Retire(int slot)
    {
        ref Entry entry = ref _slots[slot];
        IAudioVoice? voice = entry.Voice;
        entry = default;
        voice?.Dispose();
    }

    private struct Entry(int generation, IAudioVoice voice)
    {
        internal int Generation = generation;

        internal IAudioVoice? Voice = voice;

        // Whether the game itself paused this voice. The host's suspension is layered over it.
        internal bool Paused;
    }
}
