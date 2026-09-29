using Capsule.Audio;
using Capsule.Runtime.Assets;

namespace Capsule.Runtime.Audio;

// Where a clip's file is. Separate from the store so resolution and its failure are testable without
// a sound device.
internal static class AudioFiles
{
    private static readonly AssetFiles Files = new("Audio clip", "clip");

    internal static Stream Open(HostPlatform platform, in AudioClip clip) =>
        Files.Open(platform, clip.Name, clip.Extension);
}
