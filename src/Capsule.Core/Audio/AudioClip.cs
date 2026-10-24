using System.ComponentModel;

namespace Capsule.Audio;

/// <summary>
/// Pure data naming the audio source <c>{Name}{Extension}</c>, with the duration the build measured
/// from it and the loop region it read out of it. <c>Name</c> is the source's path under the
/// <c>audio</c> root: one or more forward-slash-separated portable file-name segments, none of them
/// empty, <c>.</c> or <c>..</c>, and no extension.
/// </summary>
/// <remarks>
/// <c>Extension</c> begins with one dot and contains no other dot or separator. A <c>.wav</c> of
/// integer samples ships as <c>assets/{Name}.flac</c>, losslessly. A <c>.wav</c> of float samples and
/// an <c>.ogg</c> ship as authored, as <c>assets/{Name}{Extension}</c>.
/// </remarks>
/// <param name="Name">The source's path under <c>Assets/</c>.</param>
/// <param name="Extension">
/// The source's extension, leading dot included, either <c>.wav</c> or <c>.ogg</c>. The host decodes a
/// <c>.wav</c> clip whole when it loads and holds it resident for every scene that uses it, and decodes a
/// <c>.ogg</c> clip on its background worker as it plays. A looping voice whose clip carries a
/// <see cref="LoopRegion"/> streams that way whatever its format. A <c>.wav</c> clip with a loop region
/// is also decoded to the samples the region repeats when it loads, and its first looping play reads no
/// file. A region-less <c>.wav</c> played from an offset decodes those samples on that first play.
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
    AudioLoopRegion LoopRegion = default)
{
    // The extension a .wav source of integer samples ships under.
    internal const string Flac = ".flac";

    private const string Wave = ".wav";

    // Whether the source is a float WAV, the one form that ships as authored though it is a .wav.
    private bool FloatSamples { get; }

    /// <summary>A clip whose <c>.wav</c> source holds float samples, which ship as authored. Generated code calls it.</summary>
    /// <param name="name">The source's path under <c>Assets/</c>.</param>
    /// <param name="extension">The source's extension, leading dot included.</param>
    /// <param name="durationSeconds">Seconds the clip runs at unit pitch, measured at build time.</param>
    /// <param name="loopRegion">The stretch a looping voice repeats.</param>
    /// <param name="floatSamples">Whether the source's samples are float, which FLAC cannot carry.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public AudioClip(string name, string extension, double durationSeconds, AudioLoopRegion loopRegion, bool floatSamples)
        : this(name, extension, durationSeconds, loopRegion) => FloatSamples = floatSamples;

    // Whether the host decodes the clip as it plays instead of holding it for the scene. The
    // extension decides: .wav is resident, .ogg streams.
    internal bool IsStreamed => string.Equals(Extension, ".ogg", StringComparison.OrdinalIgnoreCase);

    // The extension of the file the build shipped for the source, below assets/ at Name.
    internal string ShippedExtension => Shipped(Extension, FloatSamples);

    // The rule the build writes by and the host opens by. FLAC carries integer samples losslessly
    // and float samples not at all.
    internal static string Shipped(string extension, bool floatSamples) =>
        !floatSamples && string.Equals(extension, Wave, StringComparison.OrdinalIgnoreCase) ? Flac : extension;
}
