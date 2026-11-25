namespace Capsule.Audio;

/// <summary>
/// The effects one bus runs on its sound, set with <see cref="AudioMixer.SetEffects"/> and faded with
/// <see cref="AudioMixer.FadeEffects"/>. <c>default(AudioEffects)</c> is no effects.
/// </summary>
/// <remarks>
/// A bus sums its voices, echoes the sum, high-passes it, low-passes it and then applies its volume. A
/// filter muffles the echo's repeats too. A parent bus takes the voices on it and its children's
/// output, then runs its own effects and volume, and the master does the same last. An echo rings on after the voice that fed it
/// stops and while its bus is paused, and its bus's volume silences it.
/// </remarks>
/// <example>
/// <code>
/// Run.Audio.SetEffects(AudioBuses.Sfx, new AudioEffects { Echo = new AudioEcho(0.25f) { Feedback = 0.4f } });
/// Run.Audio.FadeEffects(AudioBuses.Sfx, Run.Audio.GetEffects(AudioBuses.Sfx) with { LowPassHertz = 900f }, 0.2f);
/// </code>
/// </example>
public readonly record struct AudioEffects
{
    // The audible band the cutoffs are held to. A cutoff faded to null moves to the end it is off at.
    internal const float LowestHertz = 20f;

    internal const float HighestHertz = 20_000f;

    /// <summary>The bus's echo, or null, the default, for none.</summary>
    public AudioEcho? Echo { get; init; }

    /// <summary>
    /// The frequency in hertz, in [20, 20000], below which the bus is cut at 12 dB per octave, or null,
    /// the default, for no cut.
    /// </summary>
    public float? HighPassHertz { get; init; }

    /// <summary>
    /// The frequency in hertz, in [20, 20000], above which the bus is cut at 12 dB per octave, or null,
    /// the default, for no cut.
    /// </summary>
    public float? LowPassHertz { get; init; }
}
