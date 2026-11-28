namespace Capsule.Audio;

// What one AudioCommand asks the host's voice table to do.
internal enum AudioCommandKind : byte
{
    // Start the named clip on this voice at the command's gain, pitch and loop flag.
    Play,

    // End this voice and release whatever the host holds for it.
    Stop,

    // Hold this voice where it is, to be continued by a later Resume.
    Pause,

    // Continue a paused voice from where it was held.
    Resume,

    SetGain,
    SetPitch,
    SetPan,

    // Set the named bus's gain, applied after its effects, to the command's Gain.
    SetBusGain,

    SetBusEffects,
}

// One instruction from the pure mixer to whatever plays sound. Every field carries a settled value,
// not a delta. Applying a step's commands in order leaves the device in the state the mixer holds.
// Fields the Kind does not name are unset. Clip, Loop and StartSeconds are set on Play only, Bus on
// Play and the bus kinds, and Effects on SetBusEffects. A null Bus is the master. A voice's Gain is linear in [0, 1], its own
// volume times its distance attenuation. A bus's is its volume, and the master's is the master volume
// times the unfocused volume while the input reads unfocused. Pan is in [-1, 1] with -1 hard left.
internal readonly record struct AudioCommand(
    AudioCommandKind Kind,
    Voice Voice,
    AudioClip Clip = default,
    AudioBus? Bus = null,
    float Gain = 0f,
    float Pitch = 0f,
    float Pan = 0f,
    bool Loop = false,
    double StartSeconds = 0.0,
    EffectLevels Effects = default);

// The values a bus's effects sound at after a step, in the units the host runs them in. A cutoff at
// its off end passes everything: AudioEffects.LowestHertz for the high-pass and the echo's low cut,
// AudioEffects.HighestHertz for the low-pass and the echo's high cut. EchoDelaySeconds is 0 while the
// bus has no echo line.
internal readonly record struct EffectLevels(
    float HighPassHertz,
    float LowPassHertz,
    float EchoDelaySeconds,
    float EchoFeedback,
    float EchoVolume,
    float EchoLowCutHertz,
    float EchoHighCutHertz,
    bool EchoInvertLeft = false)
{
    internal static EffectLevels None => new(
        AudioEffects.LowestHertz,
        AudioEffects.HighestHertz,
        0f,
        0f,
        0f,
        AudioEffects.LowestHertz,
        AudioEffects.HighestHertz);
}
