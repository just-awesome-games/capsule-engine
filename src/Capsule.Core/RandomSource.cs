using System.Numerics;

namespace Capsule;

/// <summary>
/// The deterministic random source game logic draws from, reached from a scene, an entity or a
/// component as their <c>Random</c>. A xoshiro256** generator seeded from <see cref="Seed"/> and
/// <see cref="Stream"/> by an injective map, with no wall clock, process entropy or ambient state
/// and advanced only by a draw: the same seed, stream and sequence of calls produce the same values
/// on every platform and under NativeAOT. Give every domain that must be independent its own stream
/// of the run's seed — <c>new RandomSource(Random.Seed, MyStreams.Map)</c>. A run is restored from
/// <see cref="Seed"/>, <see cref="Stream"/> and <see cref="DrawCount"/>: construct the source again
/// and <see cref="Advance"/> it by the count.
/// </summary>
/// <remarks>
/// The runtime owns one instance for the whole run — stream 0 of the seed the host configured —
/// handed to every scene it opens, so neither a transition nor a restart reseeds or rewinds it.
/// </remarks>
public sealed class RandomSource
{
    /// <summary>The seed a run uses unless the host configures one, so an unconfigured game still replays.</summary>
    public const ulong DefaultSeed = 1;

    // Distinct odd constants, so the pair that zeroes the first two state words still leaves the
    // other two non-zero. SeedConstant is internal because the seeding test inverts with it.
    internal const ulong SeedConstant = 0xA0761D6478BD642FUL;
    private const ulong ThirdWordConstant = 0xE7037ED1A0B428DBUL;
    private const ulong FourthWordConstant = 0x8EBC6AF09C88C6E3UL;

    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    /// <summary>Creates a source positioned at the start of a seed and stream's sequence.</summary>
    /// <param name="seed">Any 64-bit value, including zero; every seed yields a full-period stream.</param>
    /// <param name="stream">
    /// The domain this source serves, any 64-bit value; streams of one seed are independent, and
    /// adjacent ones are as far apart as distant ones.
    /// </param>
    public RandomSource(ulong seed = DefaultSeed, ulong stream = 0)
    {
        Seed = seed;
        Stream = stream;

        // Injective: Avalanche is a bijection, so s0 recovers the seed and s0 with s1 recovers the
        // stream, and two distinct pairs therefore never share a state.
        _s0 = Avalanche(seed ^ SeedConstant);
        _s1 = Avalanche(stream ^ _s0);

        // The last two words fold the first two together so no word is a lone input's, and are
        // built so the pair that zeroes s0 and s1 cannot zero them: an all-zero state is xoshiro's
        // fixed point.
        _s2 = Avalanche(_s0 ^ Avalanche(_s1 + ThirdWordConstant));
        _s3 = Avalanche(_s1 + Avalanche(_s0 + FourthWordConstant));
    }

    /// <summary>The seed this source was created from; the run's identity, shared by every stream of it.</summary>
    public ulong Seed { get; }

    /// <summary>The stream this source draws, which names the domain it serves within <see cref="Seed"/>.</summary>
    public ulong Stream { get; }

    /// <summary>The generator's state words, which the seeding test inverts to recover the pair.</summary>
    internal (ulong S0, ulong S1, ulong S2, ulong S3) StateWords => (_s0, _s1, _s2, _s3);

    /// <summary>
    /// Raw 64-bit outputs consumed since construction. With <see cref="Seed"/> and
    /// <see cref="Stream"/> it is the whole position, which <see cref="Advance"/> restores.
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

    /// <summary>
    /// Consumes <paramref name="draws"/> raw outputs, leaving the source where that many draws
    /// would. Linear in the count.
    /// </summary>
    /// <param name="draws">How many raw outputs to consume.</param>
    public void Advance(ulong draws)
    {
        for (ulong drawn = 0; drawn < draws; drawn++)
        {
            NextUInt64();
        }
    }

    /// <summary>Draws an integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    /// <param name="minInclusive">Lowest value the draw can return.</param>
    /// <param name="maxExclusive">One past the highest; equal to the minimum returns it and draws nothing.</param>
    /// <returns>A uniformly distributed value in the half-open range.</returns>
    /// <remarks>
    /// One draw almost always, and the only method whose cost is not fixed: a span that does not
    /// divide 2^32 rejects the outputs that would bias it, each rejection costing one more draw.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxExclusive"/> is below <paramref name="minInclusive"/>.</exception>
    public int Range(int minInclusive, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExclusive, minInclusive);

        uint span = (uint)(maxExclusive - minInclusive);
        if (span == 0)
        {
            return minInclusive;
        }

        // Lemire's multiply-shift: unbiased, where a modulo would favour the low end of a span
        // that does not divide 2^32.
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

    /// <summary>Draws a float in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>). One draw.</summary>
    /// <param name="minInclusive">Lowest value the draw can return.</param>
    /// <param name="maxExclusive">One past the highest; equal to the minimum returns it and draws nothing.</param>
    /// <returns>A uniformly distributed value scaled from <see cref="NextFloat"/>, always below the bound.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxExclusive"/> is below <paramref name="minInclusive"/>, or either bound is not finite.</exception>
    public float Range(float minInclusive, float maxExclusive)
    {
        RequireFinite(minInclusive, nameof(minInclusive));
        RequireFinite(maxExclusive, nameof(maxExclusive));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExclusive, minInclusive);

        if (minInclusive == maxExclusive)
        {
            return minInclusive;
        }

        // In double: a float span across bounds as wide as the type allows overflows to infinity,
        // and the scaled result then reads NaN.
        float value = (float)(minInclusive + (NextFloat() * ((double)maxExclusive - minInclusive)));

        // A double result below the bound can still round up to it on the way back to float.
        return value < maxExclusive ? value : MathF.BitDecrement(maxExclusive);
    }

    /// <summary>Draws a float in [0, 1). One draw.</summary>
    /// <returns>A uniformly distributed value on a grid of 2^-24, so every result is exact.</returns>
    public float NextFloat() => (NextUInt64() >> 40) * (1.0f / (1 << 24));

    /// <summary>Draws a bool that is true with probability <paramref name="probability"/>. One draw.</summary>
    /// <param name="probability">
    /// In [0, 1]; at or below 0 never passes, at or above 1 always passes, and NaN never passes.
    /// One draw is consumed whatever the value, so tuning a probability does not shift the stream.
    /// </param>
    public bool Chance(float probability) => NextFloat() < probability;

    /// <summary>
    /// Draws from a normal distribution of <paramref name="mean"/> and
    /// <paramref name="standardDeviation"/>. Always two draws.
    /// </summary>
    /// <param name="mean">The distribution's centre.</param>
    /// <param name="standardDeviation">The distribution's spread; 0 returns the mean, having drawn.</param>
    /// <returns>
    /// A finite value, unbounded in principle and within about five deviations in practice; one
    /// beyond what a float can hold saturates at <see cref="float.MaxValue"/>.
    /// </returns>
    /// <remarks>
    /// The value passes through a logarithm and a cosine, whose last bit is not guaranteed
    /// identical across platforms; the draw cost is, so streams stay aligned.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is not finite, or the deviation is negative.</exception>
    public float Normal(float mean = 0, float standardDeviation = 1)
    {
        RequireFinite(mean, nameof(mean));
        RequireFinite(standardDeviation, nameof(standardDeviation));
        ArgumentOutOfRangeException.ThrowIfNegative(standardDeviation);

        // 1 - u is in (0, 1]: the logarithm of a drawn zero is negative infinity.
        double radius = Math.Sqrt(-2.0 * Math.Log(1.0 - NextFloat()));
        double angle = 2.0 * Math.PI * NextFloat();

        // In double to the end: a wide mean and deviation overflow a float scale-and-add.
        return Saturate(mean + (standardDeviation * radius * Math.Cos(angle)));
    }

    /// <summary>
    /// Draws a point uniformly distributed over the unit disc, by area rather than by radius, so
    /// it does not cluster at the centre. Always two draws.
    /// </summary>
    /// <returns>A point inside or on the unit circle: its magnitude is at most 1.</returns>
    /// <remarks>
    /// The point passes through a sine and a cosine, whose last bit is not guaranteed identical
    /// across platforms; the draw cost is, so streams stay aligned.
    /// </remarks>
    public Vector2 InsideUnitCircle()
    {
        // The square root is what spreads the points by area.
        double radius = Math.Sqrt(NextFloat());
        double angle = 2.0 * Math.PI * NextFloat();

        return new Vector2((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
    }

    /// <summary>Draws one of <paramref name="values"/> uniformly. Costs one <see cref="Range(int, int)"/>.</summary>
    /// <param name="values">The values to draw from.</param>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    public T Pick<T>(ReadOnlySpan<T> values)
    {
        if (values.IsEmpty)
        {
            throw new ArgumentException("A pick needs at least one value to draw.", nameof(values));
        }

        return values[Range(0, values.Length)];
    }

    /// <summary>
    /// Shuffles <paramref name="values"/> in place into a uniformly distributed permutation.
    /// Fisher-Yates: one <see cref="Range(int, int)"/> per value after the first.
    /// </summary>
    /// <param name="values">The values to reorder.</param>
    public void Shuffle<T>(Span<T> values)
    {
        for (int index = values.Length - 1; index > 0; index--)
        {
            int swap = Range(0, index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }
    }

    /// <summary>
    /// Draws an index of <paramref name="weights"/> with probability proportional to its weight.
    /// Costs one <see cref="NextFloat"/>. A zero weight is never drawn.
    /// </summary>
    /// <param name="weights">Non-negative finite weights, at least one of them positive; they need not sum to 1.</param>
    /// <exception cref="ArgumentException">The weights are empty, hold a negative or non-finite one, or none is positive.</exception>
    public int WeightedIndex(ReadOnlySpan<float> weights)
    {
        double total = 0;
        int lastPositive = -1;
        for (int index = 0; index < weights.Length; index++)
        {
            float weight = weights[index];
            if (!float.IsFinite(weight) || weight < 0)
            {
                throw new ArgumentException(
                    $"weight {index} is {weight}; every weight is a finite, non-negative share of the whole.",
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
            throw new ArgumentException(
                "no weight is positive; a weighted draw needs at least one outcome that can happen.",
                nameof(weights));
        }

        double target = NextFloat() * total;
        double cumulative = 0;
        for (int index = 0; index <= lastPositive; index++)
        {
            cumulative += weights[index];

            // Strictly below, so an index carrying no weight cannot be the one this lands on.
            if (target < cumulative)
            {
                return index;
            }
        }

        // Only reachable when summing in a different order rounds the target past the total.
        return lastPositive;
    }

    // float.MaxValue is exact as a double, so this catches every value the cast would otherwise
    // round to an infinity.
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

    private static void RequireFinite(float value, string parameterName)
    {
        // NaN and the infinities pass every comparison-based range guard.
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A range bound must be a finite number.");
        }
    }
}
