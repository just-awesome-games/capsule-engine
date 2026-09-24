namespace Capsule.Audio;

/// <summary>
/// Pure data naming <c>assets/{Name}{Extension}</c> beside the executable, with the duration
/// the build measured from the source and the loop region it read out of it. <c>Name</c> is the
/// source's path under the <c>audio</c> root: one or more forward-slash-separated portable
/// file-name segments, none of them empty, <c>.</c> or <c>..</c>, and no extension.
/// </summary>
/// <remarks><c>Extension</c> begins with one dot and contains no other dot or separator.</remarks>
/// <param name="Name">The source's path under <c>Assets/</c>.</param>
/// <param name="Extension">
/// The source's extension, leading dot included, either <c>.wav</c> or <c>.ogg</c>. The host holds a
/// <c>.wav</c> clip resident for every scene that uses it, and decodes a <c>.ogg</c> clip on its
/// background worker as it plays. A looping voice whose clip carries a <see cref="LoopRegion"/>
/// streams that way whatever its format.
/// </param>
/// <param name="DurationSeconds">
/// Seconds the clip runs at unit pitch, measured at build time. Playback state is derived from it and
/// not read back from the device. A clip of zero duration ends on the step it starts.
/// </param>
/// <param name="LoopRegion">
/// The stretch a looping voice repeats, or <see cref="AudioLoopRegion.None"/> where neither the file
/// nor the game set one. A looping voice plays from the clip's beginning to the region's end and then
/// repeats the region gaplessly. A clip with no region repeats in full, and a voice that does not loop
/// ignores the region. The mixer refuses a clip whose region does not fit it.
/// </param>
public readonly record struct AudioClip(
    string Name,
    string Extension,
    double DurationSeconds,
    AudioLoopRegion LoopRegion = default);
