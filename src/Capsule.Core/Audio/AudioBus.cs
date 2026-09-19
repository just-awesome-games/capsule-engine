namespace Capsule.Audio;

/// <summary>
/// A named group voices are mixed and paused through, such as music, sfx or dialogue. A bus registers
/// with a <see cref="AudioMixer"/> the first time that mixer is asked to change it, at volume 1 and
/// unpaused. Names are compared ordinally. The default value is <see cref="Master"/>, which scales and
/// pauses every voice whichever bus it plays on. A bus's volume and pause state belong to the run, so
/// both stand across a scene transition until something sets them again.
/// </summary>
public readonly record struct AudioBus(string Name)
{
    /// <summary>The bus every other one is nested under. Its volume scales every voice, and pausing it pauses every voice.</summary>
    public static AudioBus Master => default;
}
