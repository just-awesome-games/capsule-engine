namespace Capsule.Audio;

/// <summary>What one <see cref="AudioCommand"/> asks the host's voice table to do.</summary>
public enum AudioCommandKind : byte
{
    /// <summary>Start the named clip on this voice at the command's gain, pitch and loop flag.</summary>
    Play,

    /// <summary>End this voice and release whatever the host holds for it.</summary>
    Stop,

    /// <summary>Hold this voice where it is, to be continued by a later <see cref="Resume"/>.</summary>
    Pause,

    /// <summary>Continue a paused voice from where it was held.</summary>
    Resume,

    /// <summary>Set this voice's gain to the command's <see cref="AudioCommand.Gain"/>.</summary>
    SetGain,

    /// <summary>Set this voice's playback rate to the command's <see cref="AudioCommand.Pitch"/>.</summary>
    SetPitch,

    /// <summary>Set this voice's stereo position to the command's <see cref="AudioCommand.Pan"/>.</summary>
    SetPan,
}

/// <summary>
/// One instruction from the pure mixer to whatever plays sound. Every field carries a settled value,
/// not a delta, so applying a step's commands in order leaves the device in the state the mixer holds.
/// Fields the <see cref="Kind"/> does not name are unset.
/// </summary>
/// <param name="Kind">What to do.</param>
/// <param name="Voice">The voice to do it to.</param>
/// <param name="Clip">The clip to start. Set on <see cref="AudioCommandKind.Play"/> only.</param>
/// <param name="Bus">
/// The bus the started voice mixes and pauses through. Set on <see cref="AudioCommandKind.Play"/>
/// only. <see cref="Gain"/> is already resolved against it, so this names the group for a host that
/// routes or meters by bus.
/// </param>
/// <param name="Gain">Linear amplitude in [0, 1], with master, bus and voice volume already multiplied together.</param>
/// <param name="Pitch">Playback-rate multiplier, on a play or a pitch change.</param>
/// <param name="Pan">Stereo position in [-1, 1] with -1 hard left, on a play or a pan change.</param>
/// <param name="Loop">Whether the voice repeats. Set on <see cref="AudioCommandKind.Play"/> only.</param>
/// <param name="StartSeconds">Clip time the voice begins at. Set on <see cref="AudioCommandKind.Play"/> only.</param>
public readonly record struct AudioCommand(
    AudioCommandKind Kind,
    Voice Voice,
    AudioClip Clip,
    AudioBus Bus,
    float Gain,
    float Pitch,
    float Pan,
    bool Loop,
    double StartSeconds);
