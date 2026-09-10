using System.Buffers.Binary;

namespace Capsule.Runtime.Audio;

// One clip's samples decoded whole and shared by every voice that streams them: what a resident
// clip loops a region from. Interleaved and normalised to [-1, 1], which is the shape the device
// buffer is encoded from, so the widths a WAV may carry are converted once here rather than per read.
//
// The build's probe reads the same container to measure the clip, but it is a build-time tool the
// runtime does not reference; this reads only what playback needs.
internal sealed class PcmAudio(float[] samples, int channels, int sampleRate)
{
    // No samples at all: what a cursor holds once the clip it read has been let go of.
    internal static PcmAudio None { get; } = new([], 1, 1);

    internal int Channels => channels;

    internal int SampleRate => sampleRate;

    // Interleaved and normalised; read by a cursor over them and never copied.
    internal ReadOnlySpan<float> Samples => samples;

    // 16-bit PCM is what a shipped WAV is; 8-bit unsigned, 24-bit PCM and 32-bit IEEE float are the
    // other shapes SoundEffect accepts and the build's probe admits.
    internal static PcmAudio FromWav(string path, string clipName)
    {
        byte[] file = File.ReadAllBytes(path);
        ReadOnlySpan<byte> bytes = file;

        if (bytes.Length < 12 || !Is(bytes[..4], "RIFF") || !Is(bytes[8..12], "WAVE"))
        {
            throw new InvalidDataException($"Audio clip '{clipName}' is no RIFF/WAVE file.");
        }

        int tag = 0;
        int channels = 0;
        int bits = 0;
        int rate = 0;
        ReadOnlySpan<byte> data = default;

        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            ReadOnlySpan<byte> id = bytes.Slice(offset, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
            offset += 8;

            int length = (int)Math.Min(size, (uint)(bytes.Length - offset));

            if (Is(id, "fmt ") && length >= 16)
            {
                ReadOnlySpan<byte> format = bytes.Slice(offset, length);
                tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
                rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
            }
            else if (Is(id, "data"))
            {
                data = bytes.Slice(offset, length);
            }

            offset += length + (length & 1);
        }

        if (channels is not (1 or 2))
        {
            throw new NotSupportedException(
                $"Audio clip '{clipName}' is {channels}-channel; Capsule plays mono and stereo.");
        }

        return new PcmAudio(Decode(data, tag, bits, clipName), channels, rate);
    }

    private static float[] Decode(ReadOnlySpan<byte> data, int tag, int bits, string clipName)
    {
        int width = bits / 8;
        if (width == 0 || (tag == 3 && bits != 32) || (tag == 1 && bits is not (8 or 16 or 24 or 32)))
        {
            throw new NotSupportedException(
                $"Audio clip '{clipName}' is WAVE format {tag} at {bits} bits; Capsule plays 8-, 16-, 24- and 32-bit PCM and 32-bit float.");
        }

        float[] samples = new float[data.Length / width];

        for (int i = 0; i < samples.Length; i++)
        {
            ReadOnlySpan<byte> sample = data.Slice(i * width, width);

            samples[i] = (tag, bits) switch
            {
                (1, 8) => (sample[0] - 128) / 128f,
                (1, 16) => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
                (1, 24) => ((sample[0] | (sample[1] << 8) | ((sbyte)sample[2] << 16)) / 8388608f),
                (1, 32) => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f,
                _ => BinaryPrimitives.ReadSingleLittleEndian(sample),
            };
        }

        return samples;
    }

    private static bool Is(ReadOnlySpan<byte> bytes, string ascii)
    {
        for (int i = 0; i < ascii.Length; i++)
        {
            if (bytes[i] != (byte)ascii[i])
            {
                return false;
            }
        }

        return true;
    }
}
