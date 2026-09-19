using Capsule.Tests.Scenes;

namespace Capsule.Tests.Core;

public sealed class RandomReplayTests
{
    // The raw stream is the replay contract: every other draw is derived from it, so pinning it
    // pins them all. The distributions are covered by their own specs rather than by golden bits.
    [Fact]
    public void ReplayOutputsAreStable()
    {
        RandomSource raw = new(0, 0);
        ulong[] expected =
        [
            6214935894119219593, 17444575831771567405, 16051968927679601219, 2122169594285274152,
        ];

        Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(_ => raw.NextUInt64()));
    }

    // A seed of zero is the one value that could leave a xoshiro state at its all-zero fixed
    // point, where every draw would be zero forever.
    [Fact]
    public void ASeedOfZeroStillProducesAVaryingSequence()
    {
        RandomSource random = new(0);

        Assert.True(Enumerable.Range(0, 16).Select(_ => random.NextFloat()).Distinct().Count() > 1);
    }

    // The bug this exists to prevent: StS2 seeded its streams additively and shipped correlated
    // first draws. Neither axis may sit adjacent in state — streams of one seed share nothing but
    // the seed, and adjacent seeds of one stream are as far apart as distant ones.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AdjacentSeedsOrStreamsAreDecorrelatedInTheirFirstDraws(bool varyTheStream)
    {
        ulong[] first =
        [
            .. Enumerable.Range(0, 1_024).Select(index => varyTheStream
                ? new RandomSource(9, (ulong)index).NextUInt64()
                : new RandomSource((ulong)index, 7).NextUInt64()),
        ];

        Assert.Equal(1_024, first.Distinct().Count());

        // Every bit of a decorrelated first draw is a coin flip across the thousand sources.
        for (int bit = 0; bit < 64; bit++)
        {
            int ones = first.Count(draw => ((draw >> bit) & 1) == 1);
            Assert.InRange(ones, 448, 576);
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
        Assert.Equal(0ul, Cost(random, source => source.Range(4, 4)));
        Assert.Equal(0ul, Cost(random, source => source.Shuffle<int>([])));
    }

    private static ulong First(ulong seed, ulong stream) => new RandomSource(seed, stream).NextUInt64();

    private static ulong Cost(RandomSource random, Action<RandomSource> draw)
    {
        ulong before = random.DrawCount;
        draw(random);

        return random.DrawCount - before;
    }
}
