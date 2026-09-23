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

    // Set this voice's gain to the command's Gain.
    SetGain,

    // Set this voice's playback rate to the command's Pitch.
    SetPitch,

    // Set this voice's stereo position to the command's Pan.
    SetPan,
}

// One instruction from the pure mixer to whatever plays sound. Every field carries a settled value,
// not a delta. Applying a step's commands in order leaves the device in the state the mixer holds.
// Fields the Kind does not name are unset. Clip, Bus, Loop and StartSeconds are set on Play only.
// Gain is linear in [0, 1] with master, bus and voice volume already multiplied together. Pan is in
// [-1, 1] with -1 hard left.
internal readonly record struct AudioCommand(
    AudioCommandKind Kind,
    Voice Voice,
    AudioClip Clip,
    AudioBus Bus,
    float Gain,
    float Pitch,
    float Pan,
    bool Loop,
    double StartSeconds);
