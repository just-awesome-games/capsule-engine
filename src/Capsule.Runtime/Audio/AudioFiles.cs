using Capsule.Audio;
using Capsule.Runtime.Assets;

namespace Capsule.Runtime.Audio;

// Where a clip's shipped file is, and how a resident clip's is read. Separate from the store so
// resolution and its failure are testable without a sound device.
internal static class AudioFiles
{
    private static readonly AssetFiles Files = new("Audio clip", "clip");

    internal static Stream Open(HostPlatform platform, in AudioClip clip) =>
        Files.Open(platform, clip.Name, clip.ShippedExtension);

    // A resident clip's samples as a WAVE file in memory: the shipped .wav read whole, or the shipped
    // FLAC decoded into a WAVE file of the same samples. Throws when the file is missing or malformed.
    internal static byte[] ReadWave(HostPlatform platform, in AudioClip clip)
    {
        byte[] file;
        using (Stream stream = Open(platform, clip))
        {
            if (stream.CanSeek)
            {
                file = new byte[stream.Length];
                stream.ReadExactly(file);
            }
            else
            {
                using MemoryStream copy = new();
                stream.CopyTo(copy);
                file = copy.ToArray();
            }
        }

        return clip.ShippedExtension == AudioClip.Flac ? FlacDecoder.ToWave(file, clip.Name) : file;
    }
}
