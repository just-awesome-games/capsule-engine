using System.Numerics;

namespace Capsule;

/// <summary>
/// The deterministic random source game logic draws from, reached as a run's <c>Random</c>. It is a
/// xoshiro256** generator seeded from <see cref="Seed"/> and <see cref="Stream"/>. It reads no wall
/// clock, process entropy or ambient state and advances only on a draw, so one seed, stream and
/// sequence of calls produce the same values on every platform. Give each independent domain its own
/// stream of the run's seed.
/// <para>
/// A run holds one instance for its life, so neither a transition nor a restart reseeds it. To
/// restore a position, construct the source again and advance it by <see cref="DrawCount"/> draws.
/// </para>
/// </summary>
public sealed class RandomSource
{
    /// <summary>The seed a run uses unless the host configures one. An unconfigured game still replays.</summary>
    public const ulong DefaultSeed = 1;

    // Distinct odd constants. The seed and stream pair that zeroes the first two state words leaves the
    // other two non-zero.
    private const ulong SeedConstant = 0xA0761D6478BD642FUL;
    private const ulong ThirdWordConstant = 0xE7037ED1A0B428DBUL;
    private const ulong FourthWordConstant = 0x8EBC6AF09C88C6E3UL;

    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    /// <summary>Creates a source positioned at the start of a seed and stream's sequence.</summary>
    /// <param name="seed">Any 64-bit value, including zero. Every seed yields a full-period stream.</param>
    /// <param name="stream">
    /// The domain this source serves, as any 64-bit value. Streams of one seed are independent, and
    /// adjacent stream numbers are as far apart as distant ones.
    /// </param>
    public RandomSource(ulong seed = DefaultSeed, ulong stream = 0)
    {
        Seed = seed;
        Stream = stream;

        // Avalanche is a bijection, so s0 recovers the seed and s0 with s1 recovers the stream. Two
        // distinct seed and stream pairs never share a state.
        _s0 = Avalanche(seed ^ SeedConstant);
        _s1 = Avalanche(stream ^ _s0);

        // The last two words fold the first two together, so no word derives from a single input. The
        // seed and stream pair that zeroes s0 and s1 cannot zero these. An all-zero state is xoshiro's
        // fixed point.
        _s2 = Avalanche(_s0 ^ Avalanche(_s1 + ThirdWordConstant));
        _s3 = Avalanche(_s1 + Avalanche(_s0 + FourthWordConstant));
    }

    /// <summary>The seed this source was created from. It is the run's identity, shared by every stream of it.</summary>
    public ulong Seed { get; }

    /// <summary>The stream this source draws. It names the domain the source serves within <see cref="Seed"/>.</summary>
    public ulong Stream { get; }

    /// <summary>
    /// Raw 64-bit outputs consumed since construction. With <see cref="Seed"/> and
    /// <see cref="Stream"/> it gives the full position, which <see cref="Advance"/> restores.
    /// </summary>
    public ulong DrawCount { get; private set; }

    /// <summary>Draws the raw 64-bit output the other methods are built from. One draw.</summary>
    public ulong NextUInt64()
    {
        ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);

        DrawCount++;

        return result;
    }

    /// <summary>Consumes <paramref name="draws"/> raw outputs, leaving the source where that many draws would. Linear in the count.</summary>
    public void Advance(ulong draws)
    {
        for (ulong drawn = 0; drawn < draws; drawn++)
        {
            NextUInt64();
        }
    }

    /// <summary>Draws an integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/> ).</summary>
    /// <param name="minInclusive">Lowest value the draw can return.</param>
    /// <param name="maxExclusive">One past the highest. Equal to the minimum returns it and draws nothing.</param>
    /// <returns>A uniformly distributed value in the half-open range.</returns>
    /// <remarks>
    /// Usually one draw, and this method's cost is not fixed. A span that does not divide 2^32 rejects
    /// the outputs that would bias it, and each rejection costs one more draw.
    /// </remarks>
    public int Range(int minInclusive, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExclusive, minInclusive);

        uint span = (uint)(maxExclusive - minInclusive);
        if (span == 0)
        {
            return minInclusive;
        }

        // Lemire's multiply-shift. A modulo here would favour the low end of a span that does not
        // divide 2^32.
        ulong product = (ulong)(uint)NextUInt64() * span;
        if ((uint)product < span)
        {
            uint threshold = (uint)((0x1_0000_0000UL - span) % span);
            while ((uint)product < threshold)
            {
                product = (ulong)(uint)NextUInt64() * span;
            }
        }

        return minInclusive + (int)(product >> 32);
    }

    /// <summary>Draws a float in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/> ). One draw.</summary>
    /// <param name="minInclusive">Lowest value the draw can return. Must be finite.</param>
    /// <param name="maxExclusive">One past the highest, and finite. Equal to the minimum returns it and draws nothing.</param>
    /// <returns>A uniformly distributed value scaled from <see cref="NextFloat"/>, below the bound.</returns>
    public float Range(float minInclusive, float maxExclusive)
    {
        Guard.Finite(minInclusive, nameof(minInclusive));
        Guard.Finite(maxExclusive, nameof(maxExclusive));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExclusive, minInclusive);

        if (minInclusive == maxExclusive)
        {
            return minInclusive;
        }

        // Scaled in double. A float span across the widest bounds the type allows overflows to infinity,
        // and the scaled result then reads NaN.
        float value = (float)(minInclusive + (NextFloat() * ((double)maxExclusive - minInclusive)));

        // A double result below the bound can still round up to it on the way back to float.
        return value < maxExclusive ? value : MathF.BitDecrement(maxExclusive);
    }

    /// <summary>Draws a float in [<see cref="FloatRange.Min"/>, <see cref="FloatRange.Max"/> ). One draw.</summary>
    /// <param name="range">The span to draw from. A constant range returns <see cref="FloatRange.Min"/> and draws nothing.</param>
    public float Range(FloatRange range) => Range(range.Min, range.Max);

    /// <summary>Draws a float in [0, 1). One draw.</summary>
    /// <returns>A uniformly distributed value on a grid of 2^-24, so every result is exact in float.</returns>
    public float NextFloat() => (NextUInt64() >> 40) * (1.0f / (1 << 24));

    /// <summary>Draws a bool that is true with probability <paramref name="probability"/>. One draw.</summary>
    /// <param name="probability">
    /// In [0, 1]. At or below 0 never passes, at or above 1 always passes, and NaN never passes.
    /// One draw is consumed whatever the value, so tuning a probability does not shift the stream.
    /// </param>
    public bool Chance(float probability) => NextFloat() < probability;

    /// <summary>
    /// Draws from an approximately normal distribution of <paramref name="mean"/> and
    /// <paramref name="standardDeviation"/>. Always two draws, and the same result bits on every
    /// supported platform.
    /// </summary>
    /// <param name="mean">The distribution's centre. Must be finite.</param>
    /// <param name="standardDeviation">The spread, finite and not negative. Zero draws and returns the mean.</param>
    /// <returns>
    /// A finite value within six deviations of the mean. A value beyond what a float can hold
    /// saturates at <see cref="float.MaxValue"/>.
    /// </returns>
    public float Normal(float mean = 0, float standardDeviation = 1)
    {
        Guard.Finite(mean, nameof(mean));
        Guard.Finite(standardDeviation, nameof(standardDeviation));
        ArgumentOutOfRangeException.ThrowIfNegative(standardDeviation);

        ulong first = NextUInt64();
        ulong second = NextUInt64();
        int sum = 0;
        for (int sample = 0; sample < 6; sample++)
        {
            sum += (int)(first & 0x3ff);
            sum += (int)(second & 0x3ff);
            first >>= 10;
            second >>= 10;
        }

        // Twelve independent uniforms have mean 6138 and variance 1048575. Dividing by 1024 gives a unit
        // normal approximation with exact binary scaling and a six-sigma bound.
        double deviation = (sum - 6138) * (1.0 / 1024.0);
        return Saturate(Math.FusedMultiplyAdd(standardDeviation, deviation, mean));
    }

    /// <summary>
    /// Draws a point uniformly distributed over the unit disc by area, so points do not cluster at the
    /// centre. Two draws per attempt, rejecting the corners of the square, which accepts about four
    /// attempts in five.
    /// </summary>
    /// <returns>A point inside or on the unit circle, with magnitude at most 1.</returns>
    public Vector2 InsideUnitCircle()
    {
        while (true)
        {
            float x = (NextFloat() * 2f) - 1f;
            float y = (NextFloat() * 2f) - 1f;

            // Squared in double. A float sum would round some outside points onto the circle and break
            // the magnitude bound.
            if (((double)x * x) + ((double)y * y) <= 1.0)
            {
                return new Vector2(x, y);
            }
        }
    }

    /// <summary>Draws one of <paramref name="values"/> uniformly. Costs one <see cref="Range(int, int)"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    public T Pick<T>(params ReadOnlySpan<T> values)
    {
        if (values.IsEmpty)
        {
            throw new ArgumentException("Expected at least one value to draw from.", nameof(values));
        }

        return values[Range(0, values.Length)];
    }

    /// <summary>
    /// Shuffles <paramref name="values"/> in place into a uniformly distributed permutation.
    /// Fisher-Yates, costing one <see cref="Range(int, int)"/> per value after the first.
    /// </summary>
    public void Shuffle<T>(Span<T> values)
    {
        for (int index = values.Length - 1; index > 0; index--)
        {
            int swap = Range(0, index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }
    }

    /// <summary>Shuffles an array in place, as the span overload does.</summary>
    public void Shuffle<T>(T[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        Shuffle(values.AsSpan());
    }

    /// <summary>
    /// Draws an index of <paramref name="weights"/> with probability proportional to its weight.
    /// Costs one <see cref="NextFloat"/>. A zero weight is never drawn.
    /// </summary>
    /// <param name="weights">Non-negative finite weights, at least one of them positive. They need not sum to 1.</param>
    /// <exception cref="ArgumentException">The weights are empty, hold a negative or non-finite value, or none is positive.</exception>
    public int WeightedIndex(params ReadOnlySpan<float> weights)
    {
        double total = 0;
        int lastPositive = -1;
        for (int index = 0; index < weights.Length; index++)
        {
            float weight = weights[index];
            if (!float.IsFinite(weight) || weight < 0)
            {
                throw new ArgumentException(
                    $"Weight {index} is {weight}. Expected a finite, non-negative weight.",
                    nameof(weights));
            }

            if (weight > 0)
            {
                lastPositive = index;
            }

            total += weight;
        }

        if (lastPositive < 0)
        {
            throw new ArgumentException("Expected at least one positive weight.", nameof(weights));
        }

        double target = NextFloat() * total;
        double cumulative = 0;
        for (int index = 0; index <= lastPositive; index++)
        {
            cumulative += weights[index];

            // Strictly below. An index carrying no weight is never drawn.
            if (target < cumulative)
            {
                return index;
            }
        }

        // Reachable only when summing in a different order rounds the target past the total.
        return lastPositive;
    }

    // float.MaxValue is exact as a double, so this catches values the cast would round to an infinity.
    private static float Saturate(double value) => value switch
    {
        >= float.MaxValue => float.MaxValue,
        <= -float.MaxValue => -float.MaxValue,
        _ => (float)value,
    };

    private static ulong Avalanche(ulong value)
    {
        ulong z = value;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
