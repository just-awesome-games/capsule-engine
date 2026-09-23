namespace Capsule.Audio;

/// <summary>
/// A named group voices are mixed and paused through, such as music, sfx or dialogue.
/// </summary>
/// <remarks>
/// A bus registers with a <see cref="AudioMixer"/> the first time that mixer plays on it or changes
/// it, at volume 1 and unpaused. Names are compared ordinally. The default value is
/// <see cref="Master"/>, and the mixer treats an empty name as <see cref="Master"/> too. A bus's
/// volume and pause state belong to the run. Both stand across a scene transition until something
/// sets them again.
/// </remarks>
public readonly record struct AudioBus(string Name)
{
    /// <summary>The bus every other one is nested under. Its volume scales every voice, and pausing it pauses every voice.</summary>
    public static AudioBus Master => default;
}
