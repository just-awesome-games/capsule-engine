namespace Capsule.Audio;

/// <summary>
/// The stretch of a clip a looping voice repeats, in seconds from the clip's start, as the
/// half-open range <c>[StartSeconds, EndSeconds)</c>.
/// </summary>
/// <remarks>
/// <see cref="None"/> is no region. The build reads a region authored in the audio file, and a game
/// sets or clears one on the clip with a <c>with</c> expression.
/// </remarks>
/// <param name="StartSeconds">Where a repeat resumes from, at or after zero.</param>
/// <param name="EndSeconds">Where a repeat is taken, exclusive and after <paramref name="StartSeconds"/>.</param>
public readonly record struct AudioLoopRegion(double StartSeconds, double EndSeconds)
{
    /// <summary>No region. A looping voice repeats the full clip.</summary>
    public static AudioLoopRegion None => default;

    /// <summary>Whether this names a region, meaning a start at or after zero and an end past it.</summary>
    public bool HasRegion => StartSeconds >= 0.0 && EndSeconds > StartSeconds;
}
