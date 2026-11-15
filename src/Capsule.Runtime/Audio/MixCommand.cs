using Capsule.Audio;

namespace Capsule.Runtime.Audio;

// What one MixCommand asks the mix thread to do.
internal enum MixCommandKind : byte
{
    // Start a voice in the slot, replacing whatever the slot held.
    Play,

    // Fade the voice out and end it, or end it at once if it is silent.
    Stop,

    // Fade the voice out and hold it where it is, or hold it at once if it is silent.
    Pause,

    // Fade a held voice back in from where it was held.
    Resume,

    SetGain,

    SetPitch,

    SetPan,

    // Replace the mixer's buses with the command's, which hold one more bus than before.
    Buses,

    // Set the gain the bus at index Bus applies after its effects.
    SetBusGain,

    // Set the effects of the bus at index Bus, and hand it a new echo line when Line is set.
    SetBusEffects,
}

// One instruction from the game thread to the mix thread, addressed to a slot at a generation, or for
// the bus kinds to the bus at index Bus. Gain is linear: a voice's own volume times its attenuation, or
// a bus's volume, which the mix applies after that bus's effects. Pitch is a playback-rate multiplier
// and Pan is in [-1, 1], -1 hard left. The init members are set on Play only, Bus on Play and the bus
// kinds, Buses on Buses only, and Effects and Line on SetBusEffects only.
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

    internal MixBus[]? Buses { get; init; }

    internal EffectLevels Effects { get; init; }

    // A new, empty echo line sized to Effects.EchoDelaySeconds, interleaved stereo. Null keeps the line
    // the bus has, or drops it when the delay is 0.
    internal float[]? Line { get; init; }
}

// The mix thread's report that the voice it was sent as slot and generation has ended and holds
// nothing any longer. Every played voice is reported exactly once, whether it ran out, was stopped or
// was replaced. Its stream, if it had one, is then free to go back to the pool.
internal readonly record struct VoiceEnded(int Slot, int Generation, AudioStream? Stream);
