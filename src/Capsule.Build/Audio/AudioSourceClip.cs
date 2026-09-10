using Capsule.Audio;

namespace Capsule.Build.Audio;

/// <summary>One shipped audio source, as the generated registry declares it.</summary>
/// <param name="Key">The source's path under the audio root, forward slashes and no extension.</param>
/// <param name="Extension">The source's extension, leading dot included.</param>
/// <param name="DurationSeconds">Seconds the source runs, measured from its container.</param>
/// <param name="Loop">The loop region the source authors, or none.</param>
internal readonly record struct AudioSourceClip(
    string Key,
    string Extension,
    double DurationSeconds,
    AudioLoopRegion Loop = default);
