using System.Numerics;
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

    // One bit per slot holding a voice, so the per-frame walks touch only the slots that sound
    // rather than the whole table; the mask is exactly as wide as AudioMixer.MaxVoices.
    private ulong _live;

    // The host's suspension, a layer over each voice's own pause: while on, every voice is held
    // whatever the game asked, and what the game asks meanwhile is remembered, not applied, so a
    // resume restores exactly the voices the game has playing.
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
        for (ulong live = _live; live != 0; live &= live - 1)
        {
            ref Entry entry = ref _slots[BitOperations.TrailingZeroCount(live)];
            if (entry.Voice is { } voice && !entry.Paused)
            {
                voice.Pause();
            }
        }
    }

    // Lets every voice the game has playing sound again; one the game paused stays paused.
    internal void Resume()
    {
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        for (ulong live = _live; live != 0; live &= live - 1)
        {
            ref Entry entry = ref _slots[BitOperations.TrailingZeroCount(live)];
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
                _live |= 1UL << slot;
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

    // Every frame, including one that runs no step: a streaming voice hands the device its next
    // buffers here, and the queue it feeds drains at display rate rather than at step rate.
    internal void Update()
    {
        for (ulong live = _live; live != 0; live &= live - 1)
        {
            int slot = BitOperations.TrailingZeroCount(live);
            if (_slots[slot].Voice is { } voice)
            {
                voice.Update();
            }
        }

        // Here rather than on each step's Apply: a voice the device finished is retired on the
        // frame that noticed, and a frame that ran eight steps walks the table once.
        for (ulong live = _live; live != 0; live &= live - 1)
        {
            int slot = BitOperations.TrailingZeroCount(live);
            if (_slots[slot].Voice is { Finished: true })
            {
                Retire(slot);
            }
        }
    }

    public void Dispose()
    {
        for (ulong live = _live; live != 0; live &= live - 1)
        {
            Retire(BitOperations.TrailingZeroCount(live));
        }
    }

    private void Retire(int slot)
    {
        ref Entry entry = ref _slots[slot];
        IAudioVoice? voice = entry.Voice;
        entry = default;
        _live &= ~(1UL << slot);
        voice?.Dispose();
    }

    private struct Entry(int generation, IAudioVoice voice)
    {
        internal int Generation = generation;

        internal IAudioVoice? Voice = voice;

        // Whether the game itself paused this voice; the host's suspension is layered over it.
        internal bool Paused;
    }
}
