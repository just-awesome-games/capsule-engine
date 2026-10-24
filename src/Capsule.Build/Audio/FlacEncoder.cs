using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using Capsule.Audio;

namespace Capsule.Build.Audio;

/// <summary>
/// Encodes a WAVE source of integer samples as FLAC (RFC 9639), losslessly and within the format's
/// streamable subset, which every stock decoder reads.
/// </summary>
/// <remarks>
/// Each frame tries the four stereo decorrelations, and each subframe tries constant, verbatim, every
/// fixed predictor and every LPC order to 12, keeping whichever codes in fewest bits.
/// </remarks>
internal static class FlacEncoder
{
    /// <summary>Samples per channel in every frame but the last.</summary>
    internal const int BlockSize = 4096;

    // The subset's limits at rates to 48 kHz: LPC order 12 and Rice partition order 8.
    private const int MaxLpcOrder = 12;
    private const int MaxFixedOrder = 4;
    private const int MaxPartitionOrder = 8;

    // Bits of each quantized LPC coefficient, sign included, and the largest shift the format codes.
    private const int Precision = 15;
    private const int MaxShift = 15;

    // Subframe types: 1 + 6 + 1 header bits ahead of each.
    private const int SubframeHeaderBits = 8;

    /// <summary>Encodes the WAVE file at <paramref name="wavePath"/> into a FLAC file at <paramref name="target"/>.</summary>
    /// <exception cref="FormatException">The source is not a WAVE of integer samples Capsule plays.</exception>
    internal static void Encode(string wavePath, string target)
    {
        byte[] data;
        WavProbe.Layout wave;
        using (FileStream source = new(wavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            wave = WavProbe.Locate(source);
            if (wave.Tag != 1)
            {
                throw new FormatException("holds float samples, which FLAC cannot carry. Ship it as authored.");
            }

            data = new byte[checked((int)(wave.Frames * wave.Channels * (wave.Bits / 8)))];
            source.Seek(wave.DataOffset, SeekOrigin.Begin);
            AudioProbe.Read(source, data);
        }

        using FileStream output = new(target, FileMode.Create, FileAccess.Write);
        Encode(data, wave.Channels, wave.Bits, wave.Rate, output);
    }

    /// <summary>Encodes interleaved little-endian samples laid out as a WAVE 'data' chunk holds them.</summary>
    /// <param name="pcm">Whole frames. 8-bit samples are unsigned, every wider one signed.</param>
    /// <param name="channels">1 or 2.</param>
    /// <param name="bits">8, 16, 24 or 32.</param>
    /// <param name="rate">Frames per second, from 1 to 2^20 - 1.</param>
    /// <param name="output">Where the FLAC file is written.</param>
    internal static void Encode(ReadOnlySpan<byte> pcm, int channels, int bits, uint rate, Stream output)
    {
        int width = bits / 8;
        long total = pcm.Length / (channels * width);
        if (rate >= 1 << 20)
        {
            throw new FormatException(
                $"plays at {AudioProbe.Number(rate)} Hz. FLAC carries rates below 1,048,576 Hz. Resample it to the rate it plays at.");
        }

        long[][] block = [.. Enumerable.Range(0, channels).Select(_ => new long[BlockSize])];
        Encoder encoder = new(bits, rate, channels);
        using MemoryStream frames = new();
        int minFrame = int.MaxValue;
        int maxFrame = 0;

        for (long first = 0, number = 0; first < total; first += BlockSize, number++)
        {
            int count = (int)Math.Min(BlockSize, total - first);
            Deinterleave(pcm, first, count, channels, bits, block);

            ReadOnlySpan<byte> frame = encoder.Frame(block, count, number);
            frames.Write(frame);
            minFrame = Math.Min(minFrame, frame.Length);
            maxFrame = Math.Max(maxFrame, frame.Length);
        }

        Span<byte> header = stackalloc byte[4 + 4 + FlacFormat.StreamInfoBytes];
        FlacFormat.Marker.CopyTo(header);
        header[4] = 0x80;
        header[7] = FlacFormat.StreamInfoBytes;

        // A stream shorter than one block declares its one block's length, within the format's floor of 16.
        int blockSize = (int)Math.Clamp(total, 16, BlockSize);
        BitWriter info = new(new byte[FlacFormat.StreamInfoBytes]);
        info.Write((ulong)blockSize, 16);
        info.Write((ulong)blockSize, 16);
        info.Write(total == 0 ? 0 : (ulong)minFrame, 24);
        info.Write((ulong)maxFrame, 24);
        info.Write(rate, 20);
        info.Write((ulong)(channels - 1), 3);
        info.Write((ulong)(bits - 1), 5);
        info.Write((ulong)total, 36);
        info.Written.CopyTo(header[8..]);
        Signature(pcm, bits).CopyTo(header[(8 + 18)..]);

        output.Write(header);
        output.Write(frames.GetBuffer().AsSpan(0, (int)frames.Length));
    }

    // The MD5 of the samples as signed little-endian integers, which is the WAVE layout but for 8-bit
    // samples, stored offset by 128 there. A stock tool verifies a decode against it.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351", Justification = "FLAC's format names MD5 as an integrity signature, not a security measure.")]
    private static byte[] Signature(ReadOnlySpan<byte> pcm, int bits)
    {
        if (bits != 8)
        {
            return MD5.HashData(pcm);
        }

        byte[] signed = pcm.ToArray();
        for (int i = 0; i < signed.Length; i++)
        {
            signed[i] ^= 0x80;
        }

        return MD5.HashData(signed);
    }

    private static void Deinterleave(ReadOnlySpan<byte> pcm, long first, int count, int channels, int bits, long[][] block)
    {
        int width = bits / 8;
        ReadOnlySpan<byte> frames = pcm.Slice((int)(first * channels * width), count * channels * width);
        for (int i = 0; i < count; i++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                ReadOnlySpan<byte> sample = frames.Slice(((i * channels) + channel) * width, width);
                block[channel][i] = bits switch
                {
                    8 => sample[0] - 128,
                    16 => BinaryPrimitives.ReadInt16LittleEndian(sample),
                    24 => sample[0] | (sample[1] << 8) | ((sbyte)sample[2] << 16),
                    _ => BinaryPrimitives.ReadInt32LittleEndian(sample),
                };
            }
        }
    }

    // Whether a residual is one RFC 9639 lets a subframe carry: a 32-bit value other than the most
    // negative, whose magnitude no 32-bit integer holds.
    private static bool Residual32(long residual) => residual is > int.MinValue and <= int.MaxValue;

    // Folds a signed residual onto the unsigned integers, as Rice coding takes it: 0, -1, 1, -2, ...
    private static ulong Fold(long residual) => (ulong)((residual << 1) ^ (residual >> 63));

    // Bits a two's-complement field needs to hold every residual whose folded value is at most folded.
    private static int SignedWidth(ulong folded) => folded == 0 ? 0 : 64 - BitOperations.LeadingZeroCount(folded >> 1) + 1;

    // One stream's frames, built with scratch reused across every frame of it.
    private sealed class Encoder(int bits, uint rate, int channels)
    {
        private readonly byte[] _frame = new byte[(BlockSize * 34 * 2 / 8) + 4096];

        // Mid and side of a stereo frame. Side carries one bit more than the samples.
        private readonly long[] _mid = new long[BlockSize];
        private readonly long[] _side = new long[BlockSize];

        private readonly Subframe[] _plans = [new(), new(), new(), new()];

        internal ReadOnlySpan<byte> Frame(long[][] block, int count, long number)
        {
            int assignment = channels - 1;
            Subframe first;
            Subframe second;

            if (channels == 1)
            {
                first = _plans[0].Plan(block[0].AsSpan(0, count), bits);
                second = first;
            }
            else
            {
                Span<long> left = block[0].AsSpan(0, count);
                Span<long> right = block[1].AsSpan(0, count);
                for (int i = 0; i < count; i++)
                {
                    _mid[i] = (left[i] + right[i]) >> 1;
                    _side[i] = left[i] - right[i];
                }

                Subframe l = _plans[0].Plan(left, bits);
                Subframe r = _plans[1].Plan(right, bits);
                Subframe m = _plans[2].Plan(_mid.AsSpan(0, count), bits);
                Subframe s = _plans[3].Plan(_side.AsSpan(0, count), bits + 1);

                // Independent, left-side, side-right, mid-side: channel assignments 1, 8, 9 and 10.
                (int Assignment, Subframe First, Subframe Second)[] choices = [(1, l, r), (8, l, s), (9, s, r), (10, m, s)];
                (assignment, first, second) = choices.MinBy(choice => choice.First.Bits + choice.Second.Bits);
            }

            BitWriter writer = new(_frame);
            Header(writer, count, assignment, number);
            first.Write(writer);
            if (channels == 2)
            {
                second.Write(writer);
            }

            writer.AlignToByte();
            ushort crc = FlacFormat.Crc16(writer.Written);
            writer.Write(crc, 16);

            return writer.Written;
        }

        private void Header(BitWriter writer, int count, int assignment, long number)
        {
            writer.Write(FlacFormat.Sync, 16);

            int sizeCode = count switch
            {
                192 => 1,
                576 or 1152 or 2304 or 4608 => 2 + BitOperations.Log2((uint)(count / 576)),
                _ when count >= 256 && BitOperations.IsPow2(count) => 8 + BitOperations.Log2((uint)(count / 256)),
                _ when count <= 256 => 6,
                _ => 7,
            };

            int listed = FlacFormat.Rates.IndexOf((int)rate);
            int rateCode = listed > 0 ? listed
                : rate % 1000 == 0 && rate / 1000 <= 255 ? 12
                : rate <= 0xFFFF ? 13
                : rate % 10 == 0 && rate / 10 <= 0xFFFF ? 14
                : 0;

            writer.Write((ulong)sizeCode, 4);
            writer.Write((ulong)rateCode, 4);
            writer.Write((ulong)assignment, 4);
            writer.Write((ulong)FlacFormat.Depths.IndexOf(bits), 3);
            writer.Write(0, 1);
            Utf8(writer, number);

            if (sizeCode == 6)
            {
                writer.Write((ulong)(count - 1), 8);
            }
            else if (sizeCode == 7)
            {
                writer.Write((ulong)(count - 1), 16);
            }

            if (rateCode == 12)
            {
                writer.Write(rate / 1000, 8);
            }
            else if (rateCode == 13)
            {
                writer.Write(rate, 16);
            }
            else if (rateCode == 14)
            {
                writer.Write(rate / 10, 16);
            }

            writer.Write(FlacFormat.Crc8(writer.Written), 8);
        }

        // The frame number in the variable-length coding UTF-8 uses for code points.
        private static void Utf8(BitWriter writer, long value)
        {
            if (value < 0x80)
            {
                writer.Write((ulong)value, 8);

                return;
            }

            int continuation = value < 0x800 ? 1 : value < 0x10000 ? 2 : value < 0x200000 ? 3 : value < 0x4000000 ? 4 : 5;
            int leadBits = 6 - continuation;
            ulong lead = (0xFFUL << (7 - continuation)) & 0xFF;
            writer.Write(lead | (((ulong)value >> (6 * continuation)) & ((1UL << leadBits) - 1)), 8);
            for (int i = continuation - 1; i >= 0; i--)
            {
                writer.Write(0x80 | (((ulong)value >> (6 * i)) & 0x3F), 8);
            }
        }
    }

    // One channel's best coding, planned against an estimate of each candidate's bits and written once.
    private sealed class Subframe
    {
        private const int Constant = 0;
        private const int Verbatim = 1;
        private const int Fixed = 2;
        private const int Lpc = 3;

        private readonly long[] _shifted = new long[BlockSize];
        private readonly long[] _residual = new long[BlockSize];
        private readonly long[] _bestResidual = new long[BlockSize];
        private readonly double[] _windowed = new double[BlockSize];
        private readonly double[] _autocorrelation = new double[MaxLpcOrder + 1];
        private readonly double[][] _coefficients = [.. Enumerable.Range(0, MaxLpcOrder).Select(_ => new double[MaxLpcOrder])];
        private readonly int[] _quantized = new int[MaxLpcOrder];
        private readonly int[] _bestQuantized = new int[MaxLpcOrder];
        private readonly Partitions _partitions = new();
        private readonly Partitions _bestPartitions = new();
        private readonly Window _window = new();

        private int _type;
        private int _bits;
        private int _wasted;
        private int _count;
        private int _order;
        private int _shift;

        // The estimated length of the planned subframe in bits.
        internal long Bits { get; private set; }

        internal Subframe Plan(ReadOnlySpan<long> samples, int bits)
        {
            _count = samples.Length;
            _bits = bits;
            _wasted = 0;

            long all = 0;
            bool constant = true;
            foreach (long sample in samples)
            {
                all |= sample;
                constant &= sample == samples[0];
            }

            if (constant)
            {
                _type = Constant;
                _shifted[0] = samples[0];
                Bits = SubframeHeaderBits + bits;

                return this;
            }

            _wasted = BitOperations.TrailingZeroCount(all);
            int depth = bits - _wasted;
            Span<long> shifted = _shifted.AsSpan(0, _count);
            for (int i = 0; i < _count; i++)
            {
                shifted[i] = samples[i] >> _wasted;
            }

            long overhead = SubframeHeaderBits + _wasted;
            _type = Verbatim;
            Bits = overhead + ((long)_count * depth);

            for (int order = 0; order <= MaxFixedOrder && order < _count; order++)
            {
                if (FixedResidual(shifted, order, _residual) && Consider(overhead + (order * depth), order) is { } planned)
                {
                    Keep(Fixed, order, 0, planned);
                }
            }

            if (_count > MaxLpcOrder)
            {
                PlanLpc(shifted, depth, overhead);
            }

            return this;
        }

        internal void Write(BitWriter writer)
        {
            writer.Write(0, 1);
            writer.Write((ulong)(_type switch { Constant => 0, Verbatim => 1, Fixed => 8 + _order, _ => 32 + _order - 1 }), 6);

            if (_wasted == 0)
            {
                writer.Write(0, 1);
            }
            else
            {
                writer.Write(1, 1);
                writer.Unary((ulong)(_wasted - 1));
            }

            int depth = _bits - _wasted;
            ReadOnlySpan<long> shifted = _shifted.AsSpan(0, _count);

            switch (_type)
            {
                case Constant:
                    writer.Write((ulong)shifted[0], _bits);
                    break;

                case Verbatim:
                    foreach (long sample in shifted)
                    {
                        writer.Write((ulong)sample, depth);
                    }

                    break;

                default:
                    for (int i = 0; i < _order; i++)
                    {
                        writer.Write((ulong)shifted[i], depth);
                    }

                    if (_type == Lpc)
                    {
                        writer.Write(Precision - 1, 4);
                        writer.Write((ulong)_shift, 5);
                        for (int i = 0; i < _order; i++)
                        {
                            writer.Write((ulong)_bestQuantized[i], Precision);
                        }
                    }

                    _bestPartitions.Write(writer, _bestResidual.AsSpan(_order, _count - _order), _count, _order);
                    break;
            }
        }

        private static bool FixedResidual(ReadOnlySpan<long> x, int order, long[] residual)
        {
            for (int i = order; i < x.Length; i++)
            {
                long r = order switch
                {
                    0 => x[i],
                    1 => x[i] - x[i - 1],
                    2 => x[i] - (2 * x[i - 1]) + x[i - 2],
                    3 => x[i] - (3 * x[i - 1]) + (3 * x[i - 2]) - x[i - 3],
                    _ => x[i] - (4 * x[i - 1]) + (6 * x[i - 2]) - (4 * x[i - 3]) + x[i - 4],
                };

                if (!Residual32(r))
                {
                    return false;
                }

                residual[i] = r;
            }

            return true;
        }

        private void PlanLpc(ReadOnlySpan<long> x, int depth, long overhead)
        {
            ReadOnlySpan<double> window = _window.Of(_count);
            for (int i = 0; i < _count; i++)
            {
                _windowed[i] = x[i] * window[i];
            }

            ReadOnlySpan<double> windowed = _windowed.AsSpan(0, _count);
            for (int lag = 0; lag <= MaxLpcOrder; lag++)
            {
                double sum = 0;
                for (int i = lag; i < windowed.Length; i++)
                {
                    sum += windowed[i] * windowed[i - lag];
                }

                _autocorrelation[lag] = sum;
            }

            if (_autocorrelation[0] == 0)
            {
                return;
            }

            int orders = LevinsonDurbin(_autocorrelation, _coefficients);
            for (int order = 1; order <= orders; order++)
            {
                if (Quantize(_coefficients[order - 1].AsSpan(0, order), _quantized, out int shift)
                    && LpcResidual(x, _quantized.AsSpan(0, order), shift, _residual)
                    && Consider(overhead + (order * depth) + 4 + 5 + (order * Precision), order) is { } planned)
                {
                    Keep(Lpc, order, shift, planned);
                    _quantized.AsSpan(0, order).CopyTo(_bestQuantized);
                }
            }
        }

        // Whether the residual just computed beats the plan so far, and its total bits if it does.
        private long? Consider(long overhead, int order)
        {
            long bits = overhead + _partitions.Plan(_residual.AsSpan(order, _count - order), _count, order);

            return bits < Bits ? bits : null;
        }

        private void Keep(int type, int order, int shift, long bits)
        {
            _type = type;
            _order = order;
            _shift = shift;
            Bits = bits;
            _residual.AsSpan(order, _count - order).CopyTo(_bestResidual.AsSpan(order));
            _bestPartitions.CopyFrom(_partitions);
        }

        // The predictor of every order to the most the autocorrelation supports, coefficient j weighing
        // the sample j + 1 back. Returns how many orders were solved.
        private static int LevinsonDurbin(double[] autocorrelation, double[][] coefficients)
        {
            Span<double> lpc = stackalloc double[MaxLpcOrder];
            double error = autocorrelation[0];

            for (int i = 0; i < MaxLpcOrder; i++)
            {
                double reflection = -autocorrelation[i + 1];
                for (int j = 0; j < i; j++)
                {
                    reflection -= lpc[j] * autocorrelation[i - j];
                }

                reflection /= error;
                lpc[i] = reflection;

                int k = 0;
                for (; k < (i >> 1); k++)
                {
                    double held = lpc[k];
                    lpc[k] += reflection * lpc[i - 1 - k];
                    lpc[i - 1 - k] += reflection * held;
                }

                if ((i & 1) != 0)
                {
                    lpc[k] += lpc[k] * reflection;
                }

                error *= 1.0 - (reflection * reflection);
                for (int j = 0; j <= i; j++)
                {
                    coefficients[i][j] = -lpc[j];
                }

                if (error <= 0 || !double.IsFinite(error))
                {
                    return i + 1;
                }
            }

            return MaxLpcOrder;
        }

        // Rounds the coefficients to Precision bits under a common shift, carrying each rounding error
        // into the next. False when the shift the coefficients need is negative, which the format forbids.
        private static bool Quantize(ReadOnlySpan<double> coefficients, int[] quantized, out int shift)
        {
            double largest = 0;
            foreach (double coefficient in coefficients)
            {
                largest = Math.Max(largest, Math.Abs(coefficient));
            }

            shift = 0;
            if (largest <= 0 || !double.IsFinite(largest))
            {
                return false;
            }

            shift = Math.Min(MaxShift, Precision - 1 - Math.ILogB(largest) - 1);
            if (shift < 0)
            {
                return false;
            }

            const int Largest = (1 << (Precision - 1)) - 1;
            double error = 0;
            for (int i = 0; i < coefficients.Length; i++)
            {
                error += coefficients[i] * (1 << shift);
                int q = Math.Clamp((int)Math.Round(error, MidpointRounding.AwayFromZero), -Largest - 1, Largest);
                error -= q;
                quantized[i] = q;
            }

            return true;
        }

        private static bool LpcResidual(ReadOnlySpan<long> x, ReadOnlySpan<int> coefficients, int shift, long[] residual)
        {
            int order = coefficients.Length;
            for (int i = order; i < x.Length; i++)
            {
                long prediction = 0;
                for (int j = 0; j < order; j++)
                {
                    prediction += coefficients[j] * x[i - 1 - j];
                }

                long r = x[i] - (prediction >> shift);
                if (!Residual32(r))
                {
                    return false;
                }

                residual[i] = r;
            }

            return true;
        }
    }

    // A residual's Rice partitioning: its partition order and each partition's parameter, where an
    // escaped partition stores its residuals raw at a fixed width instead.
    private sealed class Partitions
    {
        private const int Partitions8 = 1 << MaxPartitionOrder;

        // Per partition at the finest order: the sum of folded residuals and the largest of them.
        private readonly ulong[] _sums = new ulong[Partitions8 * 2];
        private readonly ulong[] _largest = new ulong[Partitions8 * 2];

        private readonly int[] _parameters = new int[Partitions8];

        // A parameter of -1 escapes its partition, and _raw holds the width of its residuals.
        private readonly int[] _raw = new int[Partitions8];

        private int _order;
        private bool _wide;

        // Plans the cheapest partitioning of residual and returns its estimated bits.
        internal long Plan(ReadOnlySpan<long> residual, int count, int predictorOrder)
        {
            int finest = 0;
            while (finest < MaxPartitionOrder && count % (2 << finest) == 0 && (count >> (finest + 1)) > predictorOrder)
            {
                finest++;
            }

            int partitions = 1 << finest;
            int length = count >> finest;
            for (int p = 0; p < partitions; p++)
            {
                int start = p == 0 ? 0 : (p * length) - predictorOrder;
                int end = ((p + 1) * length) - predictorOrder;
                ulong sum = 0;
                ulong largest = 0;
                for (int i = start; i < end; i++)
                {
                    ulong folded = Fold(residual[i]);
                    sum += folded;
                    largest = Math.Max(largest, folded);
                }

                _sums[p] = sum;
                _largest[p] = largest;
            }

            long best = long.MaxValue;
            Span<int> parameters = stackalloc int[Partitions8];
            Span<int> raw = stackalloc int[Partitions8];

            for (int order = finest; order >= 0; order--)
            {
                int partitionCount = 1 << order;
                if (order < finest)
                {
                    for (int p = 0; p < partitionCount; p++)
                    {
                        _sums[p] = _sums[2 * p] + _sums[(2 * p) + 1];
                        _largest[p] = Math.Max(_largest[2 * p], _largest[(2 * p) + 1]);
                    }
                }

                long bits = 2 + 4;
                bool wide = false;
                for (int p = 0; p < partitionCount; p++)
                {
                    long samples = (count >> order) - (p == 0 ? predictorOrder : 0);
                    (int parameter, long rice) = Parameter(_sums[p], samples);
                    int width = SignedWidth(_largest[p]);
                    long escaped = 5 + (samples * width);

                    // The escape's 5-bit width field holds at most 31.
                    if (escaped < rice && width < 32)
                    {
                        parameters[p] = -1;
                        raw[p] = width;
                        bits += escaped;
                    }
                    else
                    {
                        parameters[p] = parameter;
                        bits += rice;
                        wide |= parameter > 14;
                    }
                }

                bits += partitionCount * (wide ? 5 : 4);
                if (bits < best)
                {
                    best = bits;
                    _order = order;
                    _wide = wide;
                    parameters[..partitionCount].CopyTo(_parameters);
                    raw[..partitionCount].CopyTo(_raw);
                }
            }

            return best;
        }

        internal void CopyFrom(Partitions other)
        {
            _order = other._order;
            _wide = other._wide;
            other._parameters.AsSpan(0, 1 << _order).CopyTo(_parameters);
            other._raw.AsSpan(0, 1 << _order).CopyTo(_raw);
        }

        internal void Write(BitWriter writer, ReadOnlySpan<long> residual, int count, int predictorOrder)
        {
            int escape = _wide ? 31 : 15;
            int parameterBits = _wide ? 5 : 4;
            writer.Write(_wide ? 1UL : 0UL, 2);
            writer.Write((ulong)_order, 4);

            int index = 0;
            for (int p = 0; p < 1 << _order; p++)
            {
                int samples = (count >> _order) - (p == 0 ? predictorOrder : 0);
                ReadOnlySpan<long> partition = residual.Slice(index, samples);
                index += samples;

                if (_parameters[p] < 0)
                {
                    writer.Write((ulong)escape, parameterBits);
                    writer.Write((ulong)_raw[p], 5);
                    foreach (long r in partition)
                    {
                        writer.Write((ulong)r, _raw[p]);
                    }

                    continue;
                }

                int k = _parameters[p];
                writer.Write((ulong)k, parameterBits);
                foreach (long r in partition)
                {
                    ulong folded = Fold(r);
                    writer.Unary(folded >> k);
                    writer.Write(folded, k);
                }
            }
        }

        // The parameter coding a partition in fewest bits, estimated from the sum of its folded
        // residuals, and those bits.
        private static (int Parameter, long Bits) Parameter(ulong sum, long samples)
        {
            if (samples == 0)
            {
                return (0, 0);
            }

            int guess = sum <= (ulong)samples ? 0 : BitOperations.Log2(sum / (ulong)samples);
            int best = 0;
            long fewest = long.MaxValue;
            for (int k = Math.Max(0, guess - 1); k <= Math.Min(30, guess + 1); k++)
            {
                long bits = (samples * (k + 1)) + (long)(sum >> k);
                if (bits < fewest)
                {
                    fewest = bits;
                    best = k;
                }
            }

            return (best, fewest);
        }
    }

    // The Tukey window at ratio 0.5: flat over its middle half, a raised cosine over each quarter at
    // its ends. Built once per block length.
    private sealed class Window
    {
        private double[] _values = [];

        internal ReadOnlySpan<double> Of(int length)
        {
            if (_values.Length != length)
            {
                _values = new double[length];
                Array.Fill(_values, 1.0);

                int taper = (length / 4) - 1;
                for (int n = 0; n < taper && taper > 0; n++)
                {
                    _values[n] = 0.5 - (0.5 * Math.Cos(Math.PI * n / taper));
                    _values[length - taper - 1 + n] = 0.5 - (0.5 * Math.Cos(Math.PI * (n + taper) / taper));
                }
            }

            return _values;
        }
    }

    // Bits written most significant first into a buffer one frame fits in.
    private sealed class BitWriter(byte[] buffer)
    {
        private ulong _pending;
        private int _pendingBits;
        private int _length;

        // The whole bytes written so far.
        internal ReadOnlySpan<byte> Written => buffer.AsSpan(0, _length);

        // Writes the low bits of value, up to 64 of them.
        internal void Write(ulong value, int bits)
        {
            if (bits > 32)
            {
                Write(value >> 32, bits - 32);
                bits = 32;
            }

            if (bits == 0)
            {
                return;
            }

            _pending = (_pending << bits) | (value & ((1UL << bits) - 1));
            _pendingBits += bits;
            while (_pendingBits >= 8)
            {
                _pendingBits -= 8;
                buffer[_length++] = (byte)(_pending >> _pendingBits);
            }
        }

        // Writes zeros zeros, then a one.
        internal void Unary(ulong zeros)
        {
            for (; zeros >= 32; zeros -= 32)
            {
                Write(0, 32);
            }

            Write(1, (int)zeros + 1);
        }

        internal void AlignToByte()
        {
            if (_pendingBits > 0)
            {
                Write(0, 8 - _pendingBits);
            }
        }
    }
}
