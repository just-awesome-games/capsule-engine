namespace Capsule.Audio;

/// <summary>
/// Pure data naming <c>assets/audio/{Name}{Extension}</c> beside the executable, with the duration
/// the build measured from the source and the loop region it read out of it, unless the game set
/// one on the clip. <c>Name</c> is the
/// source's path under the <c>audio</c> root: one or more forward-slash-separated portable file-name
/// segments, none empty, <c>.</c>, or <c>..</c>, and no extension. <c>Extension</c> begins with one
/// dot and contains no other dot or separator.
/// </summary>
/// <param name="Name">The source's path under the audio root.</param>
/// <param name="Extension">The source's extension, leading dot included.</param>
/// <param name="DurationSeconds">
/// Seconds the clip runs at unit pitch, measured at build time. Playback state is derived from it
/// rather than read back from the device, so a clip whose duration is zero ends on the step it
/// starts.
/// </param>
/// <param name="LoopRegion">
/// The stretch a looping voice repeats, or <see cref="AudioLoopRegion.None"/> where the file
/// authored none and the game set none. The game sets a region on an untagged clip, or clears an
/// imported one to <see cref="AudioLoopRegion.None"/>, with a <c>with</c> expression; the mixer
/// refuses a clip whose region does not fit it at play.
/// A looping voice plays from the clip's beginning to the region's end and then
/// repeats the region, gaplessly; one whose clip has no region repeats the whole clip. A voice that
/// does not loop ignores the region and plays to the clip's end.
/// </param>
public readonly record struct AudioClip(
    string Name,
    string Extension,
    double DurationSeconds,
    AudioLoopRegion LoopRegion = default);
