using Capsule.Audio;

namespace Capsule.Runtime.Audio;

// The host's mix: every voice the game has sounding, resampled to the output's rate and summed into
// its bus's buffer. Each bus runs its effects and gain on its buffer and adds it into its parent's,
// children before parents. Voices on the master bus write to the output directly, and the master's
// effects and gain run on the output last. A voice whose bus is muted keeps its time unheard.
//
// Mix is pure. It opens no device and starts no thread, and the suite calls it directly. The thread
// that calls it in a run is host policy (MixThread). On that thread Mix takes no lock and allocates
// nothing. It talks to the game thread only through the two rings it was built with: commands in at
// the start of each block, and ended voices out.
//
// Output is passed through unclipped, as float. Values beyond 1 are the output's to handle.
internal sealed class VoiceMixer
{
    internal const int SampleRate = AudioOutput.SampleRate;
    internal const int Channels = AudioOutput.Channels;

    // The frames one pass mixes. Commands land and gain changes ramp on its boundaries.
    internal const int BlockFrames = 512;

    // About 2 ms. A stop, a pause, a steal and a resume fade over this many frames, which is within
    // one block. A voice stopped at a block's start has ended by its close.
    internal const int FadeFrames = SampleRate / 500;

    private readonly AudioRing<MixCommand> _commands;
    private readonly AudioRing<VoiceEnded> _ended;
    private readonly MixVoice[] _slots = new MixVoice[AudioMixer.MaxVoices];
    private readonly MixBus _master = new();

    // Voices fading out after a stop or a steal, or playing out an expired one-shot's tail, beside
    // whatever their slot has started since, and the voices free to take a slot's place. A voice is
    // in exactly one of the three.
    private readonly MixVoice[] _fading = new MixVoice[AudioMixer.MaxVoices];
    private readonly MixVoice[] _spare = new MixVoice[AudioMixer.MaxVoices];
    private int _fadingCount;
    private int _spareCount;

    // Every bus but the master, by index less one. The game thread hands over a larger array as it
    // meets a new bus.
    private MixBus[] _buses = [];

    // Each block in which a playing stream found its ring empty before its end, counted once per stream.
    private long _starved;

    internal VoiceMixer(AudioRing<MixCommand> commands, AudioRing<VoiceEnded> ended)
    {
        _commands = commands;
        _ended = ended;

        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new MixVoice();
            _spare[i] = new MixVoice();
        }

        _spareCount = _spare.Length;
    }

    internal long Starved => Volatile.Read(ref _starved);

    // Fills output with the next frames of the mix, interleaved stereo. Each block of up to
    // BlockFrames first applies every command the game thread has sent.
    internal void Mix(Span<float> output)
    {
        while (output.Length >= Channels)
        {
            int frames = Math.Min(BlockFrames, output.Length / Channels);
            MixBlock(output[..(frames * Channels)], frames);
            output = output[(frames * Channels)..];
        }
    }

    private void MixBlock(Span<float> master, int frames)
    {
        while (_commands.TryRead(out MixCommand command))
        {
            Apply(command);
        }

        master.Clear();
        int samples = frames * Channels;
        _master.Quiet = _master.Muted;
        // A parent comes before its children. It has settled by the time they read it.
        foreach (MixBus bus in _buses)
        {
            bus.Buffer.AsSpan(0, samples).Clear();

            // Under a muted bus above it a bus's sound goes unheard, unless its echo has repeats to keep.
            bus.Quiet = bus.Muted || (ParentOf(bus).Quiet && !bus.Ringing);
        }

        foreach (MixVoice voice in _slots)
        {
            if (voice.Live && Render(voice, master, frames))
            {
                End(voice);
            }
        }

        for (int i = 0; i < _fadingCount;)
        {
            MixVoice voice = _fading[i];
            if (Render(voice, master, frames))
            {
                End(voice);
                _spare[_spareCount++] = voice;
                _fading[i] = _fading[--_fadingCount];
            }
            else
            {
                i++;
            }
        }

        // Children run first. A parent then processes the sum of everything under it.
        for (int b = _buses.Length - 1; b >= 0; b--)
        {
            MixBus bus = _buses[b];
            Span<float> sum = bus.Buffer.AsSpan(0, samples);
            bus.Process(sum, frames);
            if (!bus.Quiet)
            {
                Span<float> into = bus.Parent == 0 ? master : _buses[bus.Parent - 1].Buffer.AsSpan(0, samples);
                for (int i = 0; i < samples; i++)
                {
                    into[i] += sum[i];
                }
            }
        }

        _master.Process(master, frames);
    }

    private bool Render(MixVoice voice, Span<float> master, int frames)
    {
        int index = voice.Bus - 1;
        bool ended = (uint)index < (uint)_buses.Length
            ? voice.Render(_buses[index].Buffer.AsSpan(0, frames * Channels), frames, _buses[index].Quiet)
            : voice.Render(master, frames, _master.Quiet);
        if (voice.Starved)
        {
            Volatile.Write(ref _starved, _starved + 1);
        }

        return ended;
    }

    private void Apply(in MixCommand command)
    {
        switch (command.Kind)
        {
            case MixCommandKind.Buses:
                _buses = command.Buses!;
                return;

            case MixCommandKind.SetBusGain:
                BusAt(command.Bus)?.SetGain(command.Gain);
                return;

            case MixCommandKind.SetBusEffects:
                BusAt(command.Bus)?.SetEffects(command.Effects, command.Line);
                return;
        }

        if ((uint)command.Slot >= (uint)_slots.Length)
        {
            return;
        }

        if (command.Kind == MixCommandKind.Play)
        {
            // A play reaching a live voice with no stop before it means the game let that one-shot
            // expire. The mix runs up to a block behind, and its last samples play out untouched.
            Release(command.Slot, fade: false);
            _slots[command.Slot].Start(command);

            return;
        }

        MixVoice voice = _slots[command.Slot];
        if (!voice.Live || voice.Generation != command.Generation)
        {
            return;
        }

        switch (command.Kind)
        {
            case MixCommandKind.Stop:
                Release(command.Slot, fade: true);
                break;

            case MixCommandKind.Pause:
                voice.Pause();
                break;

            case MixCommandKind.Resume:
                voice.Resume();
                break;

            case MixCommandKind.SetGain:
                voice.SetGain(command.Gain);
                break;

            case MixCommandKind.SetPitch:
                voice.SetPitch(command.Pitch);
                break;

            case MixCommandKind.SetPan:
                voice.SetPan(command.Pan);
                break;
        }
    }

    // Frees the slot for its next voice. A voice that was heard plays on beside it, fading out when
    // fade is set and running to its own end when not. One that was not heard, or one past the spare
    // voices, ends at once.
    private void Release(int slot, bool fade)
    {
        MixVoice voice = _slots[slot];
        if (!voice.Live)
        {
            return;
        }

        if (!voice.Audible || _spareCount == 0)
        {
            End(voice);

            return;
        }

        // A loop has no end to run to, and a voice the host's suspension is fading to a hold would sit
        // held in the list. Either would keep its spare voice for good.
        if (fade || voice.Loops || voice.Pausing)
        {
            voice.Stop();
        }

        _fading[_fadingCount++] = voice;
        _slots[slot] = _spare[--_spareCount];
    }

    private MixBus ParentOf(MixBus bus) => bus.Parent == 0 ? _master : _buses[bus.Parent - 1];

    private MixBus? BusAt(int index) => index == 0 ? _master : (uint)(index - 1) < (uint)_buses.Length ? _buses[index - 1] : null;

    private void End(MixVoice voice)
    {
        _ended.Write(new VoiceEnded(voice.Slot, voice.Generation, voice.Stream));
        voice.Clear();
    }
}
