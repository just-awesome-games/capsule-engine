namespace Capsule.Audio;

/// <summary>
/// A bus's echo: its sound repeated every <see cref="DelaySeconds"/>, each repeat <see cref="Feedback"/>
/// as loud as the one before. Build it with the constructor, which sets the defaults below.
/// </summary>
/// <remarks>
/// <c>default(AudioEcho)</c> carries a delay of zero that the mixer refuses. The delay never fades: a
/// changed delay takes effect on the call that changes it and cuts the repeats already ringing.
/// </remarks>
/// <param name="DelaySeconds">The seconds from the sound to its first repeat and between repeats, in (0, 4].</param>
public readonly record struct AudioEcho(float DelaySeconds)
{
    // The longest delay a bus's line is allocated for.
    internal const float LongestDelaySeconds = 4f;

    /// <summary>
    /// Each repeat's linear amplitude beside the one before, in [0, 1), and 0.5 by default. 0 gives a
    /// single repeat.
    /// </summary>
    public float Feedback { get; init; } = 0.5f;

    /// <summary>The repeats' linear amplitude in [0, 1], heard beside the unchanged sound. 0.5 by default.</summary>
    public float Volume { get; init; } = 0.5f;

    /// <summary>
    /// The frequency in hertz, in [20, 20000], below which every repeat is cut again at 12 dB per octave,
    /// or null, the default, for no cut. Each repeat sounds thinner than the one before.
    /// </summary>
    public float? LowCutHertz { get; init; }

    /// <summary>
    /// The frequency in hertz, in [20, 20000], above which every repeat is cut again at 12 dB per octave,
    /// or null, the default, for no cut. Each repeat sounds darker than the one before.
    /// </summary>
    public float? HighCutHertz { get; init; }

    /// <summary>
    /// Whether the repeats' left side is inverted, which moves a centred sound's repeats out to the sides
    /// and cancels them when the output is mixed to mono. False by default.
    /// </summary>
    public bool InvertLeft { get; init; }
}
