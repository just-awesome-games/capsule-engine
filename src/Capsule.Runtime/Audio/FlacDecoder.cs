using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using Capsule.Audio;

namespace Capsule.Runtime.Audio;

// Decodes a shipped FLAC file (RFC 9639) whole into a WAVE file of the same samples, which the device
// and PcmAudio then read as they read a shipped WAVE. It decodes every subframe type, Rice partitioning
// and stereo decorrelation the format defines, at 8, 16, 24 and 32 bits per sample.
//
// A load-time cost. It allocates the WAVE file its STREAMINFO sizes and nothing per frame, and rents
// one block of scratch per channel for the length of the decode.
internal static class FlacDecoder
{
    private const int WaveHeaderBytes = 44;

    // Channel assignments 8 to 10 code a stereo pair as left and side, side and right, or mid and side.
    private const int LeftSide = 8;
    private const int SideRight = 9;
    private const int MidSide = 10;

    internal static byte[] ToWave(ReadOnlySpan<byte> file, string clipName)
    {
        if (file.Length < 4 || !file[..4].SequenceEqual(FlacFormat.Marker))
        {
            throw Malformed(clipName, "is no FLAC stream");
        }

        Declared info = ReadStreamInfo(file, clipName, out int offset);
        int width = info.Bits / 8;

        long imageBytes = WaveHeaderBytes + (info.Total * info.Channels * width);
        if (imageBytes > Array.MaxLength)
        {
            throw Malformed(clipName, $"declares {info.Total} samples, more than a clip can hold");
        }

        byte[] wave = new byte[imageBytes];
        WriteWaveHeader(wave, info);

        long[] scratch = ArrayPool<long>.Shared.Rent(info.MaxBlock * info.Channels);
        try
        {
            long decoded = 0;
            while (decoded < info.Total)
            {
                int count = DecodeFrame(file, ref offset, info, scratch, decoded, clipName);
                Interleave(scratch, info, count, wave.AsSpan(WaveHeaderBytes + (int)(decoded * info.Channels * width)));
                decoded += count;
            }
        }
        finally
        {
            ArrayPool<long>.Shared.Return(scratch);
        }

        return wave;
    }

    private static Declared ReadStreamInfo(ReadOnlySpan<byte> file, string clipName, out int offset)
    {
        offset = 4;
        Declared info = default;
        bool first = true;
        bool last = false;

        while (!last)
        {
            if (offset + 4 > file.Length)
            {
                throw Truncated(clipName);
            }

            last = (file[offset] & 0x80) != 0;
            int type = file[offset] & 0x7F;
            int length = (file[offset + 1] << 16) | (file[offset + 2] << 8) | file[offset + 3];
            offset += 4;

            if (offset + length > file.Length)
            {
                throw Truncated(clipName);
            }

            if (first)
            {
                if (type != 0 || length != FlacFormat.StreamInfoBytes)
                {
                    throw Malformed(clipName, "opens with no FLAC STREAMINFO block");
                }

                info = StreamInfo(file.Slice(offset, length), clipName);
                first = false;
            }

            offset += length;
        }

        return info;
    }

    private static Declared StreamInfo(ReadOnlySpan<byte> block, string clipName)
    {
        int minBlock = BinaryPrimitives.ReadUInt16BigEndian(block);
        int maxBlock = BinaryPrimitives.ReadUInt16BigEndian(block[2..]);
        int rate = (block[10] << 12) | (block[11] << 4) | (block[12] >> 4);
        int channels = ((block[12] >> 1) & 7) + 1;
        int bits = (((block[12] & 1) << 4) | (block[13] >> 4)) + 1;
        long total = ((long)(block[13] & 0x0F) << 32) | BinaryPrimitives.ReadUInt32BigEndian(block[14..]);

        if (minBlock < 16 || maxBlock < minBlock || rate == 0)
        {
            throw Malformed(clipName, "carries a FLAC STREAMINFO block whose block sizes or sample rate are invalid");
        }

        if (channels is not (1 or 2))
        {
            throw new NotSupportedException($"Audio clip '{clipName}' is {channels}-channel. Capsule plays mono and stereo.");
        }

        if (bits is not (8 or 16 or 24 or 32))
        {
            throw new NotSupportedException(
                $"Audio clip '{clipName}' is {bits}-bit FLAC. Capsule plays 8-, 16-, 24- and 32-bit samples. Rebuild the game to ship it again.");
        }

        return new Declared(maxBlock, rate, channels, bits, total);
    }

    // Decodes the frame at offset into scratch, one block per channel, and moves offset past it.
    // Returns the frame's samples per channel.
    private static int DecodeFrame(ReadOnlySpan<byte> file, ref int offset, in Declared info, long[] scratch, long decoded, string clipName)
    {
        int start = offset;
        Header header = ReadHeader(file, start, info, clipName);
        if (header.Count > info.MaxBlock || decoded + header.Count > info.Total)
        {
            throw Malformed(clipName, "holds more samples than its FLAC STREAMINFO block declares");
        }

        BitReader reader = new(file, header.Length + start, clipName);
        for (int channel = 0; channel < info.Channels; channel++)
        {
            bool side = (header.Assignment == LeftSide && channel == 1)
                || (header.Assignment == SideRight && channel == 0)
                || (header.Assignment == MidSide && channel == 1);

            Subframe(ref reader, scratch.AsSpan(channel * info.MaxBlock, header.Count), info.Bits + (side ? 1 : 0), clipName);
        }

        reader.AlignToByte();
        int end = reader.BytePosition;
        if (end + 2 > file.Length)
        {
            throw Truncated(clipName);
        }

        if (BinaryPrimitives.ReadUInt16BigEndian(file[end..]) != FlacFormat.Crc16(file[start..end]))
        {
            throw Malformed(clipName, "carries a FLAC frame whose CRC does not match its bytes, and the file is damaged");
        }

        offset = end + 2;
        Decorrelate(scratch, info.MaxBlock, header);

        return header.Count;
    }

    private static Header ReadHeader(ReadOnlySpan<byte> file, int start, in Declared info, string clipName)
    {
        int at = start;
        Need(file, at + 5, clipName);

        if ((BinaryPrimitives.ReadUInt16BigEndian(file[at..]) & 0xFFFE) != FlacFormat.Sync)
        {
            throw Malformed(clipName, "carries no FLAC frame where one should begin");
        }

        int sizeCode = file[at + 2] >> 4;
        int rateCode = file[at + 2] & 0x0F;
        int assignment = file[at + 3] >> 4;
        int depthCode = (file[at + 3] >> 1) & 7;
        if (sizeCode == 0 || rateCode == 15 || assignment > MidSide || FlacFormat.Depths[depthCode] < 0 || (file[at + 3] & 1) != 0)
        {
            throw Malformed(clipName, "carries a FLAC frame header using a code the format reserves");
        }

        int channels = assignment < LeftSide ? assignment + 1 : 2;
        if (channels != info.Channels || (depthCode != 0 && FlacFormat.Depths[depthCode] != info.Bits))
        {
            throw Malformed(clipName, "carries a FLAC frame whose channels or bit depth differ from its STREAMINFO block");
        }

        at += 4;

        // The frame or sample number, coded as UTF-8 codes a code point, in one to seven bytes.
        int lead = BitOperations.LeadingZeroCount((uint)(byte)~file[at]) - 24;
        if (lead == 1 || lead > 7)
        {
            throw Malformed(clipName, "carries a FLAC frame header with a malformed frame number");
        }

        at += lead == 0 ? 1 : lead;

        // A block size or a rate the codes cannot name follows the number, then the header's CRC.
        int sizeBytes = sizeCode switch { 6 => 1, 7 => 2, _ => 0 };
        int rateBytes = rateCode switch { 12 => 1, 13 or 14 => 2, _ => 0 };
        Need(file, at + sizeBytes + rateBytes + 1, clipName);

        int count = sizeCode switch
        {
            1 => 192,
            <= 5 => 576 << (sizeCode - 2),
            6 => file[at] + 1,
            7 => BinaryPrimitives.ReadUInt16BigEndian(file[at..]) + 1,
            _ => 256 << (sizeCode - 8),
        };

        at += sizeBytes + rateBytes;
        if (file[at] != FlacFormat.Crc8(file[start..at]))
        {
            throw Malformed(clipName, "carries a FLAC frame header whose CRC does not match it, and the file is damaged");
        }

        return new Header(count, assignment, at + 1 - start);
    }

    private static void Subframe(ref BitReader reader, Span<long> samples, int bits, string clipName)
    {
        uint head = reader.Read(8);
        int type = (int)(head >> 1) & 0x3F;
        int wasted = (head & 1) != 0 ? (int)reader.Unary() + 1 : 0;
        int depth = bits - wasted;
        if ((head & 0x80) != 0 || depth <= 0)
        {
            throw Malformed(clipName, "carries a FLAC subframe header that is invalid");
        }

        switch (type)
        {
            case 0:
                samples.Fill(reader.ReadSigned(depth));
                break;

            case 1:
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = reader.ReadSigned(depth);
                }

                break;

            case >= 8 and <= 12:
                Fixed(ref reader, samples, type - 8, depth, clipName);
                break;

            case >= 32:
                Lpc(ref reader, samples, type - 31, depth, clipName);
                break;

            default:
                throw Malformed(clipName, "carries a FLAC subframe of a type the format reserves");
        }

        if (wasted > 0)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] <<= wasted;
            }
        }
    }

    private static void Fixed(ref BitReader reader, Span<long> samples, int order, int depth, string clipName)
    {
        WarmUp(ref reader, samples, order, depth, clipName);
        Residual(ref reader, samples, order, clipName);

        switch (order)
        {
            case 1:
                for (int i = 1; i < samples.Length; i++)
                {
                    samples[i] += samples[i - 1];
                }

                break;

            case 2:
                for (int i = 2; i < samples.Length; i++)
                {
                    samples[i] += (2 * samples[i - 1]) - samples[i - 2];
                }

                break;

            case 3:
                for (int i = 3; i < samples.Length; i++)
                {
                    samples[i] += (3 * samples[i - 1]) - (3 * samples[i - 2]) + samples[i - 3];
                }

                break;

            case 4:
                for (int i = 4; i < samples.Length; i++)
                {
                    samples[i] += (4 * samples[i - 1]) - (6 * samples[i - 2]) + (4 * samples[i - 3]) - samples[i - 4];
                }

                break;
        }
    }

    private static void Lpc(ref BitReader reader, Span<long> samples, int order, int depth, string clipName)
    {
        WarmUp(ref reader, samples, order, depth, clipName);

        int precision = (int)reader.Read(4) + 1;
        int shift = (int)reader.ReadSigned(5);
        if (precision == 16 || shift < 0)
        {
            throw Malformed(clipName, "carries a FLAC LPC subframe whose precision or shift the format forbids");
        }

        Span<long> coefficients = stackalloc long[32];
        coefficients = coefficients[..order];
        for (int j = 0; j < order; j++)
        {
            coefficients[j] = reader.ReadSigned(precision);
        }

        Residual(ref reader, samples, order, clipName);

        for (int i = order; i < samples.Length; i++)
        {
            long prediction = 0;
            ReadOnlySpan<long> history = samples.Slice(i - order, order);
            for (int j = 0; j < order; j++)
            {
                prediction += coefficients[j] * history[order - 1 - j];
            }

            samples[i] += prediction >> shift;
        }
    }

    private static void WarmUp(ref BitReader reader, Span<long> samples, int order, int depth, string clipName)
    {
        if (order > samples.Length)
        {
            throw Malformed(clipName, "carries a FLAC subframe whose predictor order exceeds its block");
        }

        for (int i = 0; i < order; i++)
        {
            samples[i] = reader.ReadSigned(depth);
        }
    }

    // Reads the Rice-coded residual into samples from order on, where prediction then adds to it.
    private static void Residual(ref BitReader reader, Span<long> samples, int order, string clipName)
    {
        uint method = reader.Read(2);
        if (method > 1)
        {
            throw Malformed(clipName, "carries a FLAC residual coded by a method the format reserves");
        }

        int parameterBits = method == 0 ? 4 : 5;
        uint escape = method == 0 ? 15u : 31u;
        int partitionOrder = (int)reader.Read(4);
        int length = samples.Length >> partitionOrder;
        if (length << partitionOrder != samples.Length || length < order)
        {
            throw Malformed(clipName, "carries a FLAC residual whose partitions do not fit its block");
        }

        int at = order;
        for (int partition = 0; partition < 1 << partitionOrder; partition++)
        {
            int end = (partition + 1) * length;
            uint parameter = reader.Read(parameterBits);

            if (parameter == escape)
            {
                int width = (int)reader.Read(5);
                for (; at < end; at++)
                {
                    samples[at] = reader.ReadSigned(width);
                }

                continue;
            }

            int k = (int)parameter;
            for (; at < end; at++)
            {
                ulong folded = ((ulong)reader.Unary() << k) | reader.Read(k);

                // RFC 9639 bounds a residual to 32 bits and forbids the most negative of them.
                if (folded >= uint.MaxValue)
                {
                    throw Malformed(clipName, "carries a FLAC residual outside the range the format allows");
                }

                samples[at] = (long)(folded >> 1) ^ -(long)(folded & 1);
            }
        }
    }

    private static void Decorrelate(long[] scratch, int stride, in Header header)
    {
        if (header.Assignment < LeftSide)
        {
            return;
        }

        Span<long> first = scratch.AsSpan(0, header.Count);
        Span<long> second = scratch.AsSpan(stride, header.Count);
        for (int i = 0; i < first.Length; i++)
        {
            long a = first[i];
            long b = second[i];
            switch (header.Assignment)
            {
                case LeftSide:
                    second[i] = a - b;
                    break;

                case SideRight:
                    first[i] = a + b;
                    break;

                default:
                    long mid = (a << 1) | (b & 1);
                    first[i] = (mid + b) >> 1;
                    second[i] = (mid - b) >> 1;
                    break;
            }
        }
    }

    // Writes the frame's samples into the WAVE data at their width, channels interleaved. 8-bit WAVE
    // samples are unsigned.
    private static void Interleave(long[] scratch, in Declared info, int count, Span<byte> data)
    {
        int width = info.Bits / 8;
        for (int channel = 0; channel < info.Channels; channel++)
        {
            ReadOnlySpan<long> samples = scratch.AsSpan(channel * info.MaxBlock, count);
            int step = info.Channels * width;
            int at = channel * width;

            switch (width)
            {
                case 1:
                    for (int i = 0; i < count; i++, at += step)
                    {
                        data[at] = (byte)(samples[i] + 128);
                    }

                    break;

                case 2:
                    for (int i = 0; i < count; i++, at += step)
                    {
                        BinaryPrimitives.WriteInt16LittleEndian(data[at..], (short)samples[i]);
                    }

                    break;

                case 3:
                    for (int i = 0; i < count; i++, at += step)
                    {
                        long sample = samples[i];
                        data[at] = (byte)sample;
                        data[at + 1] = (byte)(sample >> 8);
                        data[at + 2] = (byte)(sample >> 16);
                    }

                    break;

                default:
                    for (int i = 0; i < count; i++, at += step)
                    {
                        BinaryPrimitives.WriteInt32LittleEndian(data[at..], (int)samples[i]);
                    }

                    break;
            }
        }
    }

    // The canonical 44-byte header of integer PCM: 'RIFF', 'fmt ' with format 1, and 'data'.
    private static void WriteWaveHeader(Span<byte> wave, in Declared info)
    {
        int width = info.Bits / 8;
        int data = wave.Length - WaveHeaderBytes;

        "RIFF"u8.CopyTo(wave);
        BinaryPrimitives.WriteInt32LittleEndian(wave[4..], wave.Length - 8);
        "WAVEfmt "u8.CopyTo(wave[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(wave[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(wave[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(wave[22..], (short)info.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(wave[24..], info.Rate);
        BinaryPrimitives.WriteInt32LittleEndian(wave[28..], info.Rate * info.Channels * width);
        BinaryPrimitives.WriteInt16LittleEndian(wave[32..], (short)(info.Channels * width));
        BinaryPrimitives.WriteInt16LittleEndian(wave[34..], (short)info.Bits);
        "data"u8.CopyTo(wave[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(wave[40..], data);
    }

    private static void Need(ReadOnlySpan<byte> file, int end, string clipName)
    {
        if (end > file.Length)
        {
            throw Truncated(clipName);
        }
    }

    private static InvalidDataException Truncated(string clipName) =>
        Malformed(clipName, "ends inside its FLAC stream, and the file is truncated");

    private static InvalidDataException Malformed(string clipName, string defect) =>
        new($"Audio clip '{clipName}' {defect}. Rebuild the game to ship it again.");

    // What STREAMINFO declares: the longest block, the rate, the channels, the bit depth and the
    // samples per channel.
    private readonly record struct Declared(int MaxBlock, int Rate, int Channels, int Bits, long Total);

    // A frame header: its samples per channel, its channel assignment and its length in bytes.
    private readonly record struct Header(int Count, int Assignment, int Length);

    // Bits read most significant first. The cache holds the next bits left-aligned, and the bits below
    // the count are zero.
    private ref struct BitReader(ReadOnlySpan<byte> data, int position, string clipName)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _next = position;
        private ulong _cache;
        private int _count;

        // The byte the reader stands at, once aligned.
        internal readonly int BytePosition => _next - (_count >> 3);

        // Reads up to 32 bits as an unsigned value.
        internal uint Read(int bits)
        {
            if (bits == 0)
            {
                return 0;
            }

            if (_count < bits)
            {
                Refill(bits);
            }

            uint value = (uint)(_cache >> (64 - bits));
            _cache <<= bits;
            _count -= bits;

            return value;
        }

        // Reads up to 33 bits as a two's-complement value. A side channel of 32-bit samples needs 33.
        internal long ReadSigned(int bits)
        {
            if (bits == 0)
            {
                return 0;
            }

            if (bits > 32)
            {
                long high = ReadSigned(bits - 32);

                return (high << 32) | Read(32);
            }

            return (long)((ulong)Read(bits) << (64 - bits)) >> (64 - bits);
        }

        // Counts zeros up to the next one, and consumes both.
        internal uint Unary()
        {
            uint zeros = 0;
            while (true)
            {
                if (_count == 0)
                {
                    Refill(1);
                }

                int leading = BitOperations.LeadingZeroCount(_cache);
                if (leading < _count)
                {
                    _cache = (_cache << leading) << 1;
                    _count -= leading + 1;

                    return zeros + (uint)leading;
                }

                zeros += (uint)_count;
                _cache = 0;
                _count = 0;
            }
        }

        internal void AlignToByte()
        {
            int drop = _count & 7;
            _cache <<= drop;
            _count -= drop;
        }

        private void Refill(int bits)
        {
            while (_count <= 56 && _next < _data.Length)
            {
                _cache |= (ulong)_data[_next++] << (56 - _count);
                _count += 8;
            }

            if (_count < bits)
            {
                throw Truncated(clipName);
            }
        }
    }
}
