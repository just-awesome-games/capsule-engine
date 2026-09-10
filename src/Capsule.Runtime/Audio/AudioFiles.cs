using Capsule.Audio;
using Capsule.Runtime.Assets;

namespace Capsule.Runtime.Audio;

// Where a clip's file is. Separate from the store so the resolution and its failure are testable
// without a sound device.
internal static class AudioFiles
{
    private const string OggExtension = ".ogg";

    private static readonly AssetFiles Files = new("audio", "Audio clip", "clip");

    // Whether the clip is decoded as it plays rather than held in memory for the scene. The two
    // shipped extensions split on exactly this: .wav is resident, .ogg streams.
    internal static bool IsStreamed(in AudioClip clip) =>
        string.Equals(clip.Extension, OggExtension, StringComparison.OrdinalIgnoreCase);

    internal static string RelativePathOf(in AudioClip clip) =>
        Files.RelativePathOf(clip.Name, clip.Extension);

    internal static string Locate(string baseDirectory, in AudioClip clip) =>
        Files.Locate(baseDirectory, clip.Name, clip.Extension);
}
