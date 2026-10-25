namespace Capsule.Runtime.Audio;

// What one MixCommand asks the mix thread to do.
internal enum MixCommandKind : byte
{
    // Start a voice in the slot, replacing whatever the slot held.
    Play,

    // Fade the voice out and end it.
    Stop,

    // Fade the voice out and hold it where it is.
    Pause,

    // Fade a held voice back in from where it was held.
    Resume,

    SetGain,

    SetPitch,

    SetPan,

    // Replace the mixer's bus buffers with the command's, which hold one more bus than before.
    Buses,
}

// One instruction from the game thread to the mix thread, addressed to a slot at a generation. Gain
// is linear and already resolved against the master and bus volumes. Pitch is a playback-rate
// multiplier and Pan is in [-1, 1], -1 hard left. The init members are set on Play only, and Buses on
// Buses only.
internal readonly record struct MixCommand(MixCommandKind Kind, int Slot, int Generation, float Gain = 0f, float Pitch = 1f, float Pan = 0f)
{
    // A resident clip's samples, or null for a streamed voice.
    internal PcmAudio? Samples { get; init; }

    // A streamed voice's decoded frames, or null for a resident one.
    internal AudioStream? Stream { get; init; }

    // The index of the voice's bus, 0 being the master bus.
    internal int Bus { get; init; }

    // Whether the voice repeats. A resident voice repeats from LoopStart once it reaches LoopEnd, and a
    // stream loops on its decode worker and plays straight through here.
    internal bool Loop { get; init; }

    internal long LoopStart { get; init; }

    internal long LoopEnd { get; init; }

    // The source frame a resident voice starts on. A stream's decoder has already sought there. Any
    // start but 0 fades in.
    internal long StartFrame { get; init; }

    // Whether the voice starts held, as a voice played while the host is suspended does.
    internal bool Held { get; init; }

    internal float[][]? Buses { get; init; }
}

// The mix thread's report that the voice it was sent as slot and generation has ended and holds
// nothing any longer. Every played voice is reported exactly once, whether it ran out, was stopped or
// was replaced. Its stream, if it had one, is then free to go back to the pool.
internal readonly record struct VoiceEnded(int Slot, int Generation, AudioStream? Stream);
