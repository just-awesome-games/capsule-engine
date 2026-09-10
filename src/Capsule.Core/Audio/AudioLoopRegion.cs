namespace Capsule.Audio;

/// <summary>
/// The stretch of a clip a looping voice repeats, in seconds from the clip's start, half-open:
/// <c>[StartSeconds, EndSeconds)</c>. <see cref="None"/> is no region at all.
/// <para>
/// A region reaches Capsule two ways. The build reads one authored in the audio file — a WAV
/// <c>smpl</c> chunk's first sample loop, or the Ogg Vorbis comments <c>LOOPSTART</c> and
/// <c>LOOPLENGTH</c> — into the generated clip, whose bounds are then a whole sample count over the
/// file's rate, so the host recovers the exact sample the file names. A game sets one on the clip
/// instead with a <c>with</c> expression:
/// <c>CapsuleAssets.Audio.Music.Theme with { LoopRegion = new AudioLoopRegion(43.316, 76.164) }</c>.
/// </para>
/// <para>
/// The same two sources are Godot's, which takes a stream's region from the import or from code on
/// the stream, as in <c>AudioStreamOggVorbis.loop_offset</c>; the file authoring is the RPG Maker
/// <c>LOOPSTART</c>/<c>LOOPLENGTH</c> tag pair.
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
