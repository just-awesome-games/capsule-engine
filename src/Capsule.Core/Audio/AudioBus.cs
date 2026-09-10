namespace Capsule.Audio;

/// <summary>
/// A named group voices are mixed and paused through — music, sfx, dialogue. A bus registers with a
/// <see cref="AudioMixer"/> the first time that mixer is asked to change it, at volume 1 and
/// unpaused; names are compared ordinally.
/// <para>
/// The default value is <see cref="Master"/>, which every voice is scaled and paused by whichever
/// bus it plays on.
/// </para>
/// <para>
/// A bus's volume and pause state belong to the mixer, and the mixer belongs to the run: both stand
/// across every scene transition until something sets them again. Level a bus once, from the boot
/// scene's start or a settings screen, not per scene.
/// </para>
/// </summary>
/// <param name="Name">The bus's name; null or empty is <see cref="Master"/>.</param>
public readonly record struct AudioBus(string Name)
{
    /// <summary>The bus every other one is nested under: its volume scales every voice, and pausing it pauses every voice.</summary>
    public static AudioBus Master => default;
}
