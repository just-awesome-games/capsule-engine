using Capsule.Audio;

namespace Capsule.Runtime.Audio;

// The host's voice table: one entry per mixer slot, holding the generation the slot was last played
// at and the voice sounding for it.
//
// The mixer is the authority on what is playing; nothing here is ever read back into it. A command
// naming a generation the table does not hold addresses a voice that has since been stolen, expired
// or stopped, and is dropped — the mixer raises no Stop for a one-shot that simply ran out, so a
// finished voice is retired here instead.
internal sealed class AudioPlayer(SoundStore sounds) : IDisposable
{
    private readonly Entry[] _slots = new Entry[AudioMixer.MaxVoices];

    // Applied after every step rather than once a frame: the mixer rewrites its commands each step
    // and a frame may run several.
    internal void Apply(ReadOnlySpan<AudioCommand> commands)
    {
        RetireFinished();

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

                _slots[slot] = new Entry(
                    command.Voice.Generation,
                    sounds.Play(
                        command.Clip,
                        command.Gain,
                        command.Pitch,
                        command.Pan,
                        command.Loop,
                        command.StartSeconds));

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
                    voice.Pause();
                    break;

                case AudioCommandKind.Resume:
                    voice.Resume();
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

    // Every frame, including one that runs no step: a streaming voice hands the device its next
    // buffers here, and the queue it feeds drains at display rate rather than at step rate.
    internal void Update()
    {
        Span<Entry> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Voice is { } voice)
            {
                voice.Update();
            }
        }

        RetireFinished();
    }

    public void Dispose()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            Retire(i);
        }
    }

    private void RetireFinished()
    {
        Span<Entry> slots = _slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Voice is { Finished: true })
            {
                Retire(i);
            }
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
    }
}
