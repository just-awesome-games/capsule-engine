using System.Buffers.Binary;
using static Capsule.Build.Audio.AudioProbe;

namespace Capsule.Build.Audio;

/// <summary>Measures a RIFF/WAVE source: its length from the 'data' chunk and its loop from the first 'smpl' loop.</summary>
internal static class WavProbe
{
    // WAVE_FORMAT_EXTENSIBLE, whose sub-format GUID opens with the real format tag.
    private const int Extensible = 0xFFFE;

    // The 'fmt ' body an extensible file carries through the sub-format's first two bytes.
    private const int ExtensibleFormatBytes = 26;

    // The format tags of integer PCM and IEEE float samples.
    private const int PcmTag = 1;
    private const int FloatTag = 3;

    internal static Measurement Measure(FileStream stream)
    {
        Layout wave = Locate(stream);

        return new Measurement(
            wave.Frames / (double)wave.Rate,
            Region(wave.LoopStart, wave.LoopEnd, wave.Frames, wave.Rate),
            wave.FloatSamples);
    }

    // RIFF: a 12-byte header, then 'id' + little-endian size + payload padded to an even length. A
    // writer may leave the last chunk's pad byte off. Only 'fmt ', 'data' and 'smpl' are read, and every
    // other chunk is skipped. Refuses every shape Measure refuses, and leaves the stream at its end.
    internal static Layout Locate(FileStream stream)
    {
        Span<byte> riff = stackalloc byte[12];
        Read(stream, riff);

        if (!riff[..4].SequenceEqual("RIFF"u8) || !riff[8..].SequenceEqual("WAVE"u8))
        {
            throw new FormatException("is no RIFF/WAVE file. Export it as a WAVE file.");
        }

        Span<byte> chunk = stackalloc byte[8];
        Span<byte> format = stackalloc byte[16];

        int tag = 0;
        int channels = 0;
        int bits = 0;
        uint rate = 0;
        long dataBytes = -1;
        long dataOffset = 0;
        long? loopStart = null;
        long? loopEnd = null;

        while (TryRead(stream, chunk))
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            long end = stream.Position + size;

            ReadOnlySpan<byte> id = chunk[..4];
            if (id.SequenceEqual("fmt "u8))
            {
                if (size < format.Length)
                {
                    throw new FormatException(
                        $"carries a {Number(size)}-byte 'fmt ' chunk. A WAVE format chunk is at least {Number(format.Length)} bytes. Export the file again.");
                }

                Read(stream, format);

                tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
                if (tag == Extensible)
                {
                    tag = SubFormat(stream, size);
                }

                if (tag is not (PcmTag or FloatTag))
                {
                    throw new FormatException(
                        $"is WAVE format {Number(tag)}. Capsule reads PCM (1) and IEEE float (3). Re-encode it as 16-bit PCM.");
                }

                channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
                rate = BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                dataBytes = size;
                dataOffset = stream.Position;
            }
            else if (id.SequenceEqual("smpl"u8))
            {
                SampleLoop(stream, size, ref loopStart, ref loopEnd);
            }

            if (end > stream.Length)
            {
                throw new FormatException("ends inside a RIFF chunk. The file is truncated. Export it again.");
            }

            stream.Seek(Math.Min(end + (size & 1), stream.Length), SeekOrigin.Begin);
        }

        if (rate == 0 || channels == 0 || bits == 0 || bits % 8 != 0)
        {
            throw new FormatException(
                "carries no usable 'fmt ' chunk. Its rate, channel count or sample width is zero or not a whole number of bytes. Re-encode it as 16-bit PCM.");
        }

        RequireMonoOrStereo(channels);

        if (tag == PcmTag ? bits is not (8 or 16 or 24 or 32) : bits != 32)
        {
            throw new FormatException(
                $"is WAVE format {Number(tag)} at {Number(bits)} bits. Capsule plays 8-, 16-, 24- and 32-bit PCM and 32-bit float. Re-encode it as 16-bit PCM.");
        }

        if (dataBytes < 0)
        {
            throw new FormatException("carries no 'data' chunk, and its length cannot be measured. Export the file again.");
        }

        long frames = dataBytes / (channels * (bits / 8));
        if (frames == 0)
        {
            throw new FormatException("carries a 'data' chunk holding no whole sample frame, and a clip sounds at least one. Export the file again with its audio.");
        }

        return new Layout(tag == FloatTag, channels, bits, rate, dataOffset, frames, loopStart, loopEnd);
    }

    // The format tag an extensible 'fmt ' body's sub-format names, read with the stream past the body's
    // first 16 bytes.
    private static int SubFormat(FileStream stream, uint size)
    {
        if (size < ExtensibleFormatBytes)
        {
            throw new FormatException(
                $"carries a {Number(size)}-byte extensible 'fmt ' chunk. An extensible format chunk is at least {Number(ExtensibleFormatBytes)} bytes. Re-encode it as 16-bit PCM.");
        }

        Span<byte> extension = stackalloc byte[ExtensibleFormatBytes - 16];
        Read(stream, extension);

        return BinaryPrimitives.ReadUInt16LittleEndian(extension[8..]);
    }

    // The 'smpl' chunk: 36 fixed bytes, the last of which count the sample loops, then 24 bytes per
    // loop. A clip plays one region, and only the first loop is read. Its end is the last sample
    // sounded, and the region ends one sample past it.
    private static void SampleLoop(FileStream stream, uint size, ref long? start, ref long? end)
    {
        const int Header = 36;
        const int Loop = 24;

        if (size < Header)
        {
            throw new FormatException(
                $"carries a {Number(size)}-byte 'smpl' chunk. A sampler chunk is at least {Number(Header)} bytes. Export the file again.");
        }

        Span<byte> header = stackalloc byte[Header];
        Read(stream, header);

        uint loops = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
        if (loops == 0)
        {
            return;
        }

        if (size < Header + Loop)
        {
            throw new FormatException(
                $"declares {Number(loops)} sample loop(s) in a {Number(size)}-byte 'smpl' chunk, which holds none. Export the file again.");
        }

        Span<byte> loop = stackalloc byte[Loop];
        Read(stream, loop);

        start = BinaryPrimitives.ReadUInt32LittleEndian(loop[8..]);
        end = BinaryPrimitives.ReadUInt32LittleEndian(loop[12..]) + 1L;
    }

    /// <summary>Where a source's samples sit and what shape they are, as <see cref="Locate"/> found them.</summary>
    /// <param name="FloatSamples">Whether the samples are 32-bit IEEE float instead of integer PCM.</param>
    /// <param name="Channels">1 or 2.</param>
    /// <param name="Bits">8, 16, 24 or 32. 8-bit samples are unsigned.</param>
    /// <param name="Rate">Frames per second, above zero.</param>
    /// <param name="DataOffset">Where the 'data' chunk's samples begin in the file.</param>
    /// <param name="Frames">Whole frames the 'data' chunk holds, at least one. A trailing partial frame is not one.</param>
    /// <param name="LoopStart">The first 'smpl' loop's first sample, or null.</param>
    /// <param name="LoopEnd">The sample one past the first 'smpl' loop's last, or null.</param>
    internal readonly record struct Layout(
        bool FloatSamples,
        int Channels,
        int Bits,
        uint Rate,
        long DataOffset,
        long Frames,
        long? LoopStart,
        long? LoopEnd);
}
