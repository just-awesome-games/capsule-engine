using System.Numerics;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Runtime;

public sealed class RandomSourceTests
{
    [Fact]
    public void TheSameSeedAndDrawsReplayTheSameSequence()
    {
        static (int Int, float Float, bool Bool)[] Draw(RandomSource random) =>
            [.. Enumerable.Range(0, 64).Select(_ => (random.Range(-5, 5), random.NextFloat(), random.Chance(0.5f)))];

        Assert.Equal(Draw(new RandomSource(7)), Draw(new RandomSource(7)));
    }

    // A seed of zero is the one value that could leave a xoshiro state at its all-zero fixed
    // point, where every draw would be zero forever.
    [Fact]
    public void ASeedOfZeroStillProducesAVaryingSequence()
    {
        RandomSource random = new(0);

        Assert.True(Enumerable.Range(0, 16).Select(_ => random.NextFloat()).Distinct().Count() > 1);
    }

    [Fact]
    public void AnIntegerRangeIncludesItsMinimumAndExcludesItsMaximum()
    {
        RandomSource random = new(42);

        int[] draws = [.. Enumerable.Range(0, 4_000).Select(_ => random.Range(3, 6))];

        Assert.Equal([3, 4, 5], draws.Distinct().Order());
    }

    [Fact]
    public void AnIntegerRangeSpanningOneValueReturnsIt()
    {
        RandomSource random = new(42);

        Assert.Equal(9, random.Range(9, 10));
        Assert.Equal(9, random.Range(9, 9));
    }

    [Fact]
    public void AnIntegerRangeSpanningTheWholeWidthDrawsWithoutOverflowing()
    {
        RandomSource random = new(42);

        int draw = random.Range(int.MinValue, int.MaxValue);

        Assert.InRange(draw, int.MinValue, int.MaxValue - 1);
    }

    [Fact]
    public void AFloatDrawStaysInTheUnitInterval()
    {
        RandomSource random = new(11);

        foreach (float value in Enumerable.Range(0, 4_000).Select(_ => random.NextFloat()))
        {
            Assert.InRange(value, 0f, 0.99999994f);
        }
    }

    [Fact]
    public void AFloatRangeStaysWithinItsBounds()
    {
        RandomSource random = new(11);

        foreach (float value in Enumerable.Range(0, 4_000).Select(_ => random.Range(-2.5f, 7.5f)))
        {
            Assert.InRange(value, -2.5f, 7.5f);
        }
    }

    [Fact]
    public void ACertainChanceAlwaysPassesAndAnImpossibleOneNever()
    {
        RandomSource random = new(3);

        Assert.All(Enumerable.Range(0, 256), _ => Assert.True(random.Chance(1f)));
        Assert.All(Enumerable.Range(0, 256), _ => Assert.False(random.Chance(0f)));
        Assert.False(random.Chance(float.NaN));
    }

    [Fact]
    public void AProbabilityIsHonouredOverManyDraws()
    {
        RandomSource random = new(5);

        int passes = Enumerable.Range(0, 10_000).Count(_ => random.Chance(0.25f));

        Assert.InRange(passes, 2_250, 2_750);
    }

    // A span computed in float overflows to infinity across the widest finite bounds, and the
    // scaled draw then reads NaN.
    [Fact]
    public void AFloatRangeAcrossTheWidestFiniteBoundsStaysFiniteAndInside()
    {
        RandomSource random = new(13);

        foreach (float value in Enumerable.Range(0, 4_000).Select(_ => random.Range(-float.MaxValue, float.MaxValue)))
        {
            Assert.True(float.IsFinite(value));
            Assert.InRange(value, -float.MaxValue, float.MaxValue);
            Assert.NotEqual(float.MaxValue, value);
        }
    }

    [Fact]
    public void AFloatRangeNeverReturnsItsExclusiveMaximum()
    {
        RandomSource random = new(17);

        // A span this narrow rounds every scaled draw onto one of its two bounds.
        foreach (float value in Enumerable.Range(0, 4_000).Select(_ => random.Range(1f, MathF.BitIncrement(1f))))
        {
            Assert.Equal(1f, value);
        }
    }

    [Fact]
    public void AFloatRangeSpanningOneValueReturnsIt()
    {
        RandomSource random = new(17);

        Assert.Equal(2.5f, random.Range(2.5f, 2.5f));
    }

    // The bug this exists to prevent: StS2 seeded its streams additively and shipped correlated
    // first draws across them. Streams of one seed must share nothing but the seed.
    [Fact]
    public void StreamsOfOneSeedAreDecorrelatedInTheirFirstDraws()
    {
        ulong[] first = [.. Enumerable.Range(0, 1_024).Select(stream => new RandomSource(9, (ulong)stream).NextUInt64())];

        Assert.Equal(1_024, first.Distinct().Count());

        // Every bit of a decorrelated first draw is a coin flip across the streams.
        for (int bit = 0; bit < 64; bit++)
        {
            int ones = first.Count(draw => ((draw >> bit) & 1) == 1);
            Assert.InRange(ones, 448, 576);
        }
    }

    // The other axis of the same bug: adjacent seeds must not be adjacent states.
    [Fact]
    public void AdjacentSeedsOfOneStreamAreDecorrelatedInTheirFirstDraws()
    {
        ulong[] first = [.. Enumerable.Range(0, 1_024).Select(seed => new RandomSource((ulong)seed, 7).NextUInt64())];

        Assert.Equal(1_024, first.Distinct().Count());

        for (int bit = 0; bit < 64; bit++)
        {
            int ones = first.Count(draw => ((draw >> bit) & 1) == 1);
            Assert.InRange(ones, 448, 576);
        }
    }

    // Injectivity, checked by inverting the construction: the pair is compressed into no single
    // word on the way in, so the state words give it back and no two pairs can share a run.
    [Fact]
    public void TheStateWordsRecoverTheSeedAndStreamTheyWereBuiltFrom()
    {
        (ulong Seed, ulong Stream)[] pairs =
        [
            (0, 0), (1, 0), (0, 1), (ulong.MaxValue, ulong.MaxValue), (ulong.MaxValue, 0),
            (0, ulong.MaxValue), (RandomSource.DefaultSeed, 12), (0x9E3779B97F4A7C15, 0xD1342543DE82EF95),
            (1, 0x45C23B99922C5393),
        ];

        foreach ((ulong seed, ulong stream) in pairs)
        {
            (ulong s0, ulong s1, _, _) = new RandomSource(seed, stream).StateWords;

            Assert.Equal(seed, Unavalanche(s0) ^ RandomSource.SeedConstant);
            Assert.Equal(stream, Unavalanche(s1) ^ s0);
        }
    }

    // Zeroing the first two state words is solvable — the seed that avalanches to zero, on stream
    // zero — and an all-zero xoshiro state never leaves zero.
    [Fact]
    public void ThePairThatZeroesTheFirstTwoStateWordsIsStillAWorkingGenerator()
    {
        RandomSource random = new(RandomSource.SeedConstant, 0);
        (ulong s0, ulong s1, ulong s2, ulong s3) = random.StateWords;

        Assert.Equal(0ul, s0);
        Assert.Equal(0ul, s1);
        Assert.NotEqual(0ul, s2);
        Assert.NotEqual(0ul, s3);
        Assert.True(Enumerable.Range(0, 16).Select(_ => random.NextUInt64()).Distinct().Count() > 8);
    }

    // A combiner with algebraic symmetry would silently make two domains one run forever:
    // commutation, complementation, and a shared xor mask are the symmetries to rule out.
    [Fact]
    public void NoSymmetryOfASeedAndStreamMapsOntoAnotherPair()
    {
        // Pairs where every transformation below genuinely names a different pair: no seed equals
        // its own stream, and none is its stream's complement.
        (ulong Seed, ulong Stream)[] pairs =
        [
            (3, 8), (7, 0), (1, 2), (12_345, 99_999), (0x9E3779B97F4A7C15, 0xD1342543DE82EF95),
        ];

        Assert.NotEqual(First(0, ulong.MaxValue), First(ulong.MaxValue, 0));

        foreach ((ulong seed, ulong stream) in pairs)
        {
            ulong original = First(seed, stream);

            Assert.NotEqual(original, First(stream, seed));
            Assert.NotEqual(original, First(~stream, ~seed));
            Assert.NotEqual(original, First(~seed, ~stream));
            Assert.NotEqual(original, First(seed ^ 0xA5A5A5A5A5A5A5A5, stream ^ 0xA5A5A5A5A5A5A5A5));
        }
    }

    // The family (s, ~s), which a construction xoring two mixes would collapse to one state.
    [Fact]
    public void AStreamThatComplementsItsSeedIsStillItsOwnRun()
    {
        ulong[] complementary = [.. Enumerable.Range(0, 512).Select(seed => First((ulong)seed, ~(ulong)seed))];

        Assert.Equal(512, complementary.Distinct().Count());
        Assert.DoesNotContain(First(0, 0), complementary);
    }

    [Fact]
    public void TheSameSeedAndStreamReplayWhileADifferentStreamDiverges()
    {
        static ulong[] Draw(RandomSource random) => [.. Enumerable.Range(0, 16).Select(_ => random.NextUInt64())];

        Assert.Equal(Draw(new RandomSource(4, 2)), Draw(new RandomSource(4, 2)));
        Assert.NotEqual(Draw(new RandomSource(4, 2)), Draw(new RandomSource(4, 3)));
    }

    [Fact]
    public void ASourceIsRestoredFromItsSeedStreamAndDrawCount()
    {
        RandomSource run = new(88, 5);
        for (int draw = 0; draw < 37; draw++)
        {
            run.NextUInt64();
        }

        RandomSource restored = new(run.Seed, run.Stream);
        restored.Advance(run.DrawCount);

        Assert.Equal(37ul, run.DrawCount);
        Assert.Equal(run.NextUInt64(), restored.NextUInt64());
        Assert.Equal(run.DrawCount, restored.DrawCount);
    }

    [Fact]
    public void EveryFixedCostDrawCostsWhatItSays()
    {
        RandomSource random = new(2);

        Assert.Equal(1ul, Cost(random, source => source.NextUInt64()));
        Assert.Equal(1ul, Cost(random, source => source.NextFloat()));
        Assert.Equal(1ul, Cost(random, source => source.Chance(0.5f)));
        Assert.Equal(1ul, Cost(random, source => source.Range(0f, 1f)));
        Assert.Equal(1ul, Cost(random, source => source.WeightedIndex([1f, 2f, 3f])));
        Assert.Equal(2ul, Cost(random, source => source.Normal()));
        Assert.Equal(2ul, Cost(random, source => source.InsideUnitCircle()));
        Assert.Equal(0ul, Cost(random, source => source.Range(4, 4)));
        Assert.Equal(0ul, Cost(random, source => source.Shuffle<int>([])));
    }

    [Fact]
    public void ANormalDrawIsFiniteAndCentredOnItsMean()
    {
        RandomSource random = new(21);
        double total = 0;

        for (int draw = 0; draw < 10_000; draw++)
        {
            float value = random.Normal(10f, 2f);
            Assert.True(float.IsFinite(value));
            total += value;
        }

        Assert.InRange(total / 10_000, 9.9, 10.1);
    }

    // The scale and the add overflow a float long before a double, and the method promises a
    // number rather than an infinity.
    [Fact]
    public void ANormalDrawSaturatesRatherThanOverflowing()
    {
        RandomSource random = new(21);

        for (int draw = 0; draw < 256; draw++)
        {
            float high = random.Normal(float.MaxValue, float.MaxValue);
            float low = random.Normal(-float.MaxValue, float.MaxValue);

            Assert.True(float.IsFinite(high));
            Assert.True(float.IsFinite(low));
            Assert.InRange(high, -float.MaxValue, float.MaxValue);
            Assert.InRange(low, -float.MaxValue, float.MaxValue);
        }
    }

    [Fact]
    public void ANormalDrawOfNoDeviationIsTheMean()
    {
        RandomSource random = new(21);

        Assert.Equal(3f, random.Normal(3f, 0f));
    }

    [Fact]
    public void APointInTheUnitCircleStaysInsideOrOnIt()
    {
        RandomSource random = new(23);

        foreach (Vector2 point in Enumerable.Range(0, 10_000).Select(_ => random.InsideUnitCircle()))
        {
            Assert.True(point.Length() <= 1f, $"{point} is outside the unit circle.");
        }
    }

    // Uniform by area, not by radius: half the disc's area is outside a radius of 1/sqrt(2), so a
    // method that spread points evenly along the radius instead would fail this badly.
    [Fact]
    public void PointsInTheUnitCircleAreSpreadByArea()
    {
        RandomSource random = new(29);

        int inner = Enumerable.Range(0, 10_000).Count(_ => random.InsideUnitCircle().Length() < 0.70710678f);

        Assert.InRange(inner, 4_800, 5_200);
    }

    [Fact]
    public void APickComesFromTheValuesOffered()
    {
        RandomSource random = new(31);
        int[] values = [3, 5, 8];

        int[] picked = [.. Enumerable.Range(0, 1_000).Select(_ => random.Pick<int>(values))];

        Assert.Equal([3, 5, 8], picked.Distinct().Order());
        Assert.Throws<ArgumentException>(() => random.Pick<int>([]));
    }

    [Fact]
    public void AShuffleIsAPermutationOfWhatItWasGiven()
    {
        RandomSource random = new(37);
        int[] values = [.. Enumerable.Range(0, 32)];

        random.Shuffle<int>(values);

        Assert.Equal(Enumerable.Range(0, 32), values.Order());
        Assert.NotEqual(Enumerable.Range(0, 32), values);
    }

    [Fact]
    public void AWeightedDrawNeverLandsOnAZeroWeight()
    {
        RandomSource random = new(41);

        int[] drawn = [.. Enumerable.Range(0, 10_000).Select(_ => random.WeightedIndex([0f, 3f, 0f, 1f, 0f]))];

        Assert.Equal([1, 3], drawn.Distinct().Order());

        // Three parts to one: the heavier outcome takes about three quarters of the draws.
        Assert.InRange(drawn.Count(index => index == 1), 7_300, 7_700);
    }

    [Fact]
    public void AWeightedDrawWithNothingToDrawIsRejected()
    {
        RandomSource random = new(41);

        Assert.Throws<ArgumentException>(() => random.WeightedIndex([]));
        Assert.Throws<ArgumentException>(() => random.WeightedIndex([0f, 0f]));
        Assert.Throws<ArgumentException>(() => random.WeightedIndex([1f, -1f]));
        Assert.Throws<ArgumentException>(() => random.WeightedIndex([1f, float.PositiveInfinity]));
    }

    // The source is reached, never passed: an entity and its components draw from the one their
    // scene holds, and a game supplies the seed by supplying the source.
    [Fact]
    public void AnEntityAndItsComponentsDrawFromTheirScenesSource()
    {
        RandomSource run = new(0xBEEF, 3);
        Drawer entity = new();
        Scene scene = new();
        scene.Add(entity);

        using SceneSimulation simulation = new(scene, random: run);

        Assert.Same(run, scene.Random);
        Assert.Same(run, entity.Random);
        Assert.Same(run, entity.Probe.Random);
        Assert.Same(run, entity.SeenOnStart);
    }

    [Fact]
    public void ASceneRunWithNoSourceGetsTheDefaultStream()
    {
        Scene scene = new();

        using SceneSimulation simulation = new(scene);

        Assert.Equal(RandomSource.DefaultSeed, scene.Random.Seed);
        Assert.Equal(0ul, scene.Random.Stream);
    }

    // No throwaway source stands in before the run's: a scene that has not started has none, so a
    // draw that would silently ignore the configured seed fails instead.
    [Fact]
    public void ReadingTheSourceBeforeTheSceneStartsThrows()
    {
        Scene scene = new();
        Drawer entity = new();

        InvalidOperationException fromDetached = Assert.Throws<InvalidOperationException>(() => entity.Random);
        InvalidOperationException fromComponent = Assert.Throws<InvalidOperationException>(() => entity.Probe.Random);

        scene.Add(entity);

        InvalidOperationException fromScene = Assert.Throws<InvalidOperationException>(() => scene.Random);
        InvalidOperationException fromAttached = Assert.Throws<InvalidOperationException>(() => entity.Random);

        foreach (InvalidOperationException failure in new[] { fromDetached, fromComponent, fromScene, fromAttached })
        {
            Assert.Contains("OnStart", failure.Message, StringComparison.Ordinal);
        }
    }

    // The composing path the default instance hid: a document's entities are attached inside the
    // scene's constructor, so their OnAddedToScene runs before any source exists.
    [Fact]
    public void ADocumentComposedEntityDiscoversTheRunsSourceInOnStart()
    {
        RandomSource run = new(0x5EED);
        SceneFixtures.SpawnScene scene = new(
            SceneFixtures.Registry(("prober", static spawn => new SpawnedProber(spawn))),
            new EntitySpawn(1, "prober", Vector2.Zero));

        SpawnedProber prober = Assert.IsType<SpawnedProber>(scene.Entities[0]);

        Assert.NotNull(prober.AddedFailure);
        Assert.Contains("OnStart", prober.AddedFailure!.Message, StringComparison.Ordinal);

        using SceneSimulation simulation = new(scene, random: run);

        Assert.Same(run, prober.SeenOnStart);
        Assert.Equal(new RandomSource(0x5EED).NextFloat(), prober.FirstDraw);
    }

    private static ulong First(ulong seed, ulong stream) => new RandomSource(seed, stream).NextUInt64();

    // The inverse of the SplitMix64 finalizer: each shift-xor undone by its own doubling, each
    // multiply by the modular inverse of its constant.
    private static ulong Unavalanche(ulong value)
    {
        ulong x = value;
        x ^= x >> 31;
        x ^= x >> 62;
        x *= 0x319642B2D24D8EC3UL;
        x ^= x >> 27;
        x ^= x >> 54;
        x *= 0x96DE1B173F119089UL;
        x ^= x >> 30;
        x ^= x >> 60;

        return x;
    }

    private sealed class Drawer : Entity
    {
        internal Drawer()
            : base(Vector2.Zero)
        {
            Probe = new ProbeComponent();
            Add(Probe);
        }

        internal ProbeComponent Probe { get; }

        internal RandomSource? SeenOnStart { get; private set; }

        protected internal override void OnStart() => SeenOnStart = Random;
    }

    private sealed class ProbeComponent : Component;

    private sealed class SpawnedProber : Entity
    {
        internal SpawnedProber(EntitySpawn spawn)
            : base(spawn.Position)
        {
        }

        internal InvalidOperationException? AddedFailure { get; private set; }

        internal RandomSource? SeenOnStart { get; private set; }

        internal float FirstDraw { get; private set; }

        protected internal override void OnAddedToScene()
        {
            try
            {
                _ = Random;
            }
            catch (InvalidOperationException failure)
            {
                AddedFailure = failure;
            }
        }

        protected internal override void OnStart()
        {
            SeenOnStart = Random;
            FirstDraw = Random.NextFloat();
        }
    }

    private static ulong Cost(RandomSource random, Action<RandomSource> draw)
    {
        ulong before = random.DrawCount;
        draw(random);

        return random.DrawCount - before;
    }

    [Fact]
    public void AnInvertedRangeIsRejected()
    {
        RandomSource random = new(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.Range(5, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Range(5f, 4f));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Range(float.NaN, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Range(0f, float.PositiveInfinity));
    }
}
