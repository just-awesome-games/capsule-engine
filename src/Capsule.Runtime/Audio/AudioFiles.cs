using Capsule.Audio;
using Capsule.Runtime.Assets;

namespace Capsule.Runtime.Audio;

// Where a clip's file is. Separate from the store so resolution and its failure are testable without
// a sound device.
internal static class AudioFiles
{
    private const string OggExtension = ".ogg";

    private static readonly AssetFiles Files = new("Audio clip", "clip");

    // Whether the clip is decoded as it plays instead of held in memory for the scene. The extension
    // decides: .wav is resident, .ogg streams.
    internal static bool IsStreamed(in AudioClip clip) =>
        string.Equals(clip.Extension, OggExtension, StringComparison.OrdinalIgnoreCase);

    internal static Stream Open(HostPlatform platform, in AudioClip clip) =>
        Files.Open(platform, clip.Name, clip.Extension);
}
