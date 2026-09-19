using System.Numerics;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Core;

public sealed class RandomDrawTests
{
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

    [Fact]
    public void AnInvertedRangeIsRejected()
    {
        RandomSource random = new(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.Range(5, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Range(5f, 4f));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Range(float.NaN, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Range(0f, float.PositiveInfinity));
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
    public void InitialPointsAcrossTheSixteenBitSeedSpaceStayInsideOrOnTheUnitCircle()
    {
        for (ulong seed = 0; seed <= ushort.MaxValue; seed++)
        {
            Vector2 point = new RandomSource(seed).InsideUnitCircle();
            double lengthSquared = Math.FusedMultiplyAdd(point.X, point.X, (double)point.Y * point.Y);

            Assert.True(lengthSquared <= 1, $"seed {seed} produced {point} outside the unit circle.");
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
}
