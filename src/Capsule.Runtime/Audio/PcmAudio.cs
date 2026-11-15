using System.Buffers.Binary;

namespace Capsule.Runtime.Audio;

// One resident clip's samples, decoded whole when its scene loads it and read in place by every voice
// that plays it. Interleaved, at the clip's own rate and channel count. 8- and 16-bit sources and short
// Ogg Vorbis clips are held as 16-bit integers, half the memory of floats, and 24- and 32-bit integer
// and float sources as floats. A 16-bit sample s reads as s / 32768.
internal sealed class PcmAudio : IDisposable
{
    // The scale from a 16-bit sample to [-1, 1). A power of two, which keeps the conversion exact.
    internal const float Pcm16Scale = 1f / 32768f;

    // The WAVE format tags read: integer PCM, IEEE float, and an extensible chunk naming either.
    private const int Pcm = 1;
    private const int IeeeFloat = 3;
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

    // Decodes a WAVE file already in memory, which leaves the bytes with the caller. A shipped WAVE is
    // 32-bit IEEE float, because the build ships integer samples as FLAC. It also reads 8-bit unsigned
    // and 16-, 24- and 32-bit PCM, and FlacDecoder holds the same samples for the same integers.
    internal static PcmAudio FromWav(ReadOnlySpan<byte> bytes, string clipName)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8))
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

            if (id.SequenceEqual("fmt "u8) && length >= 16)
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
            else if (id.SequenceEqual("data"u8))
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
        bool admitted = tag == IeeeFloat ? bits == 32 : tag == Pcm && bits is (8 or 16 or 24 or 32);
        if (!admitted)
        {
            throw new NotSupportedException(
                $"Audio clip '{clipName}' is WAVE format {tag} at {bits} bits. Capsule plays 8-, 16-, 24- and 32-bit PCM and 32-bit float. Re-encode it as 16-bit PCM.");
        }

        int count = data.Length / width;
        count -= count % channels;

        if (tag == Pcm && bits <= 16)
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
                (Pcm, 24) => (sample[0] | (sample[1] << 8) | ((sbyte)sample[2] << 16)) / 8388608f,
                (Pcm, 32) => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f,
                _ => BinaryPrimitives.ReadSingleLittleEndian(sample),
            };
        }

        return new PcmAudio(samples, channels, rate);
    }

    // Frees nothing. A voice still playing the clip holds its own reference, and the collector frees
    // the samples once no voice and no scene holds them.
    public void Dispose()
    {
    }
}
