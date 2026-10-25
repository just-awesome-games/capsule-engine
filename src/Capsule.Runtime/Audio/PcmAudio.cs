using System.Buffers.Binary;

namespace Capsule.Runtime.Audio;

// One resident clip's samples, decoded whole when its scene loads it and read in place by every voice
// that plays it. Interleaved, at the clip's own rate and channel count. 8- and 16-bit sources are held
// as 16-bit integers, half the memory of floats, and 24- and 32-bit integer and float sources as
// floats. A 16-bit sample s reads as s / 32768.
//
// A scene releasing the clip frees nothing here. A voice still playing it holds its own reference, and
// the collector frees the samples once no voice and no scene holds them.
//
// The build's probe reads the same container to measure the clip, but it is a build-time tool the
// runtime does not reference. This reads what playback needs.
internal sealed class PcmAudio : IDisposable
{
    // The scale from a 16-bit sample to [-1, 1). A power of two, which keeps the conversion exact.
    internal const float Pcm16Scale = 1f / 32768f;

    private const int Extensible = 0xFFFE;

    // Exactly one of Pcm16 and Float holds the samples.
    internal short[]? Pcm16 { get; }

    internal float[]? Float { get; }

    internal int Channels { get; }

    internal int SampleRate { get; }

    internal long Frames { get; }

    internal PcmAudio(short[] samples, int channels, int sampleRate)
        : this(channels, sampleRate, samples.Length) => Pcm16 = samples;

    internal PcmAudio(float[] samples, int channels, int sampleRate)
        : this(channels, sampleRate, samples.Length) => Float = samples;

    private PcmAudio(int channels, int sampleRate, int samples)
    {
        Channels = channels;
        SampleRate = sampleRate;
        Frames = samples / channels;
    }

    // Decodes a WAVE file already in memory, which leaves the bytes with the caller. A clip's WAVE is
    // usually 16-bit PCM. 8-bit unsigned, 24- and 32-bit PCM and 32-bit IEEE float are the other
    // shapes the build's probe admits.
    internal static PcmAudio FromWav(ReadOnlySpan<byte> bytes, string clipName)
    {
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

                // An extensible chunk names its format in the first two bytes of its sub-format GUID.
                if (tag == Extensible && length >= 26)
                {
                    tag = BinaryPrimitives.ReadUInt16LittleEndian(format[24..]);
                }
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
                $"Audio clip '{clipName}' is {channels}-channel. Capsule plays mono and stereo.");
        }

        return Decode(data, tag, bits, channels, rate, clipName);
    }

    private static PcmAudio Decode(ReadOnlySpan<byte> data, int tag, int bits, int channels, int rate, string clipName)
    {
        int width = bits / 8;
        bool admitted = tag == 3 ? bits == 32 : tag == 1 && bits is (8 or 16 or 24 or 32);
        if (!admitted)
        {
            throw new NotSupportedException(
                $"Audio clip '{clipName}' is WAVE format {tag} at {bits} bits. Capsule plays 8-, 16-, 24- and 32-bit PCM and 32-bit float. Re-encode it as 16-bit PCM.");
        }

        int count = data.Length / width;
        count -= count % channels;

        if (tag == 1 && bits <= 16)
        {
            short[] pcm = new short[count];
            for (int i = 0; i < pcm.Length; i++)
            {
                pcm[i] = bits == 8
                    ? (short)((data[i] - 128) << 8)
                    : BinaryPrimitives.ReadInt16LittleEndian(data[(i * 2)..]);
            }

            return new PcmAudio(pcm, channels, rate);
        }

        float[] samples = new float[count];
        for (int i = 0; i < samples.Length; i++)
        {
            ReadOnlySpan<byte> sample = data.Slice(i * width, width);

            samples[i] = (tag, bits) switch
            {
                (1, 24) => (sample[0] | (sample[1] << 8) | ((sbyte)sample[2] << 16)) / 8388608f,
                (1, 32) => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f,
                _ => BinaryPrimitives.ReadSingleLittleEndian(sample),
            };
        }

        return new PcmAudio(samples, channels, rate);
    }

    // Nothing is freed. A scene's release only drops the store's reference.
    public void Dispose()
    {
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
