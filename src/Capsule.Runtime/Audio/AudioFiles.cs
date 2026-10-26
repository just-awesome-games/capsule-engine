using Capsule.Audio;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Audio.Vorbis;

namespace Capsule.Runtime.Audio;

// Where a clip's shipped file is, how a resident clip's is read and how a streamed clip's is opened.
// Separate from the store, which keeps resolution and its failure testable without a sound device.
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

    // A resident clip's samples, decoded whole. Throws when the file is missing or malformed.
    internal static PcmAudio ReadResident(HostPlatform platform, in AudioClip clip) =>
        clip.IsVorbis ? DecodeVorbis(platform, clip) : PcmAudio.FromWav(ReadWave(platform, clip), clip.Name);

    // A streamed clip's decoder, for one play. Throws when the file is missing, malformed or wider
    // than stereo.
    internal static IPcmSource OpenStream(HostPlatform platform, in AudioClip clip) =>
        new VorbisPcmSource(OpenVorbis(platform, clip));

    // A short Ogg Vorbis clip held at 16 bits, the width a .wav effect is held at. A decoded peak past
    // full scale saturates.
    private static PcmAudio DecodeVorbis(HostPlatform platform, in AudioClip clip)
    {
        using VorbisReader reader = OpenVorbis(platform, clip);

        // The build measured the clip from this file. A frame of slack covers the measure's rounding, and
        // decoding stops at it as playback does.
        short[] samples = new short[((int)Math.Ceiling(clip.DurationSeconds * reader.SampleRate) + 1) * reader.Channels];
        float[] block = new float[4096 * reader.Channels];
        int count = 0;
        int read;
        while ((read = reader.ReadSamples(block.AsSpan(0, Math.Min(block.Length, samples.Length - count)))) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                samples[count++] = (short)Math.Clamp(MathF.Round(block[i] * 32768f), short.MinValue, short.MaxValue);
            }
        }

        Array.Resize(ref samples, count);

        return new PcmAudio(samples, reader.Channels, reader.SampleRate);
    }

    private static VorbisReader OpenVorbis(HostPlatform platform, in AudioClip clip)
    {
        Stream file = Open(platform, clip);
        if (!file.CanSeek)
        {
            file.Dispose();
            throw new InvalidOperationException(
                $"The platform opened audio clip '{clip.Name}' as a stream that cannot seek. Return a seekable stream from its HostPlatform.OpenContent.");
        }

        VorbisReader reader = new(file);

        // The mixer reads mono and stereo. Anything wider would need downmixing.
        if (reader.Channels is not (1 or 2))
        {
            int channels = reader.Channels;
            reader.Dispose();

            throw new NotSupportedException(
                $"Audio clip '{clip.Name}' is {channels}-channel. Capsule plays mono and stereo Ogg Vorbis.");
        }

        return reader;
    }
}
