namespace Capsule.Audio;

/// <summary>
/// The stretch of a clip a looping voice repeats, in seconds from the clip's start, half-open:
/// <c>[StartSeconds, EndSeconds)</c>. <see cref="None"/> is no region at all.
/// <para>
/// A region reaches Capsule two ways. The build reads one authored in the audio file into the
/// generated clip, whose bounds are then a whole sample count over the file's rate, so the host
/// recovers the exact sample the file names: a WAV's first <c>smpl</c> sample loop; or an Ogg
/// Vorbis file's comments, whose names are case-insensitive and whose values are whole sample
/// counts — <c>LOOPSTART</c> with <c>LOOPLENGTH</c> is <c>[start, start + length)</c>,
/// <c>LOOPSTART</c> with <c>LOOPEND</c> is <c>[start, end)</c> (a <c>LOOPLENGTH</c> present wins
/// over <c>LOOPEND</c>), and <c>LOOPSTART</c> alone loops to the end of the file; a
/// <c>LOOPLENGTH</c> or <c>LOOPEND</c> without <c>LOOPSTART</c> is no region, as is a file tagging
/// none. The build fails a tag that is not a whole number and a region that starts before zero,
/// ends at or before its start, or ends past the clip. A game sets one on the clip instead with a
/// <c>with</c> expression:
/// <c>CapsuleAssets.Audio.Music.Theme with { LoopRegion = new AudioLoopRegion(43.316, 76.164) }</c>.
/// </para>
/// </summary>
/// <param name="StartSeconds">Where a repeat resumes from; at or after zero.</param>
/// <param name="EndSeconds">Where a repeat is taken, exclusive; after <paramref name="StartSeconds"/>.</param>
public readonly record struct AudioLoopRegion(double StartSeconds, double EndSeconds)
{
    /// <summary>No region: a looping voice repeats the whole clip.</summary>
    public static AudioLoopRegion None => default;

    /// <summary>
    /// Whether this names a region at all: a start at or after zero and an end past it. False for
    /// <see cref="None"/>, and the predicate the host splits its playback paths on.
    /// </summary>
    public bool HasRegion => StartSeconds >= 0.0 && EndSeconds > StartSeconds;
}
