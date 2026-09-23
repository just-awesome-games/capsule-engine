namespace Capsule.Tests.Core;

// The platform's own sine and exponential are correctly rounded by no standard, so simulation code
// may not call them. These are the replacements: what is asserted is that they are accurate enough
// to stand in for the real functions over the range they claim, and honest about where that ends,
// since being identical everywhere is what they are built of.
public sealed class DeterministicMathTests
{
    // The bound every member publishes: absolute for a sine, relative for the exponential. Neither
    // is correctly rounded, so this is about a unit in the last place rather than half of one.
    private const double Bound = 6e-8;

    // The magnitude sine and cosine claim their accuracy out to.
    private const float SineRange = 16777216f;

    [Fact]
    public void SineOverTheRangeItClaims_IsWithinThePublishedBound()
    {
        double worst = 0.0;

        // Dense over the first turns, where a curve or an oscillator actually lives.
        for (int sample = -200_000; sample <= 200_000; sample++)
        {
            worst = Math.Max(worst, SineError(sample / 10_000f));
        }

        // Then coarse the whole way out to the claimed magnitude, on a step that is no multiple of a
        // turn, so the fold is sampled at every phase rather than at one.
        for (int sample = -1_000_000; sample <= 1_000_000; sample++)
        {
            worst = Math.Max(worst, SineError((float)(sample * 16.777216)));
        }

        Assert.True(worst < Bound, $"sine is out by {worst}.");
    }

    // The fold subtracts a whole number of turns, and it is the last turn that has the least of the
    // argument's own precision left to spend on it.
    [Fact]
    public void SineAtTheEndOfItsRange_IsStillWithinThatBound()
    {
        double worst = 0.0;

        foreach (float radians in new[] { SineRange, -SineRange, SineRange - 1f, SineRange - 0.5f, SineRange / 2f })
        {
            worst = Math.Max(worst, SineError(radians));
        }

        Assert.True(worst < Bound, $"sine is out by {worst} at the end of its range.");
    }

    [Fact]
    public void CosineIsSineAQuarterTurnOn_ToTheSameAccuracy()
    {
        double worst = 0.0;

        for (int sample = -100_000; sample <= 100_000; sample++)
        {
            float radians = sample / 10_000f;
            worst = Math.Max(worst, Math.Abs(DeterministicMath.Cos(radians) - Math.Cos(radians)));
        }

        for (int sample = -1_000_000; sample <= 1_000_000; sample += 7)
        {
            float radians = (float)(sample * 16.777216);
            worst = Math.Max(worst, Math.Abs(DeterministicMath.Cos(radians) - Math.Cos(radians)));
        }

        Assert.True(worst < Bound, $"cosine is out by {worst}.");
    }

    // The bits, not just the class: IEEE fixes neither the payload nor the sign a NaN propagates
    // with, so a member that promises identical bits everywhere has to hand back the one NaN.
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void AnAngleThatIsNotAnAngle_IsTheOneNotANumber(float radians)
    {
        int canonical = BitConverter.SingleToInt32Bits(float.NaN);

        Assert.Equal(canonical, BitConverter.SingleToInt32Bits(DeterministicMath.Sin(radians)));
        Assert.Equal(canonical, BitConverter.SingleToInt32Bits(DeterministicMath.Cos(radians)));
    }

    [Fact]
    public void TwoRaisedToAnExponent_IsWithinThePublishedBoundRelatively()
    {
        double worst = 0.0;

        for (int sample = -126_000; sample <= 127_000; sample++)
        {
            float exponent = sample / 1_000f;
            double expected = Math.Pow(2.0, exponent);
            worst = Math.Max(worst, Math.Abs((DeterministicMath.Exp2(exponent) - expected) / expected));
        }

        Assert.True(worst < Bound, $"the exponential is out by {worst} relatively.");
    }

    // The whole part is a power of two and scaling by it is exact, so a whole exponent is not an
    // approximation at all: the curve families that reach an endpoint through it land on it.
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 2f)]
    [InlineData(-1f, 0.5f)]
    [InlineData(10f, 1024f)]
    [InlineData(-10f, 1f / 1024f)]
    public void AWholeExponent_IsExact(float exponent, float expected)
    {
        Assert.Equal(expected, DeterministicMath.Exp2(exponent));
    }

    [Fact]
    public void AWholeExponentAtEitherEndOfTheNormalRange_IsExact()
    {
        Assert.Equal(BitConverter.Int32BitsToSingle(1 << 23), DeterministicMath.Exp2(-126f));
        Assert.Equal(BitConverter.Int32BitsToSingle(254 << 23), DeterministicMath.Exp2(127f));
    }

    // The last exponent a float still holds something finite for: 2^128 is past it, 2^127.5 is not.
    [Fact]
    public void AnExponentBetweenTheLastWholeOneAndTheEnd_IsFinite()
    {
        double expected = Math.Pow(2.0, 127.5);
        float highest = DeterministicMath.Exp2(127.5f);

        Assert.True(float.IsFinite(highest), "2^127.5 is inside what a float holds.");
        Assert.True(Math.Abs((highest - expected) / expected) < Bound, $"2^127.5 is out by {(highest - expected) / expected}.");
    }

    // Under the normal range a float's steps are fixed rather than relative, and flushing to zero
    // there would lose every value between the smallest subnormal and the smallest normal.
    [Fact]
    public void AWholeExponentUnderTheNormalRange_IsExactlyThatSubnormal()
    {
        for (int power = -149; power <= -127; power++)
        {
            float expected = BitConverter.Int32BitsToSingle(1 << (power + 149));

            Assert.Equal(expected, DeterministicMath.Exp2(power));
        }
    }

    [Fact]
    public void AFractionalExponentUnderTheNormalRange_IsTheNearestSubnormal()
    {
        // Counted in smallest subnormals, 2^-147.5 is 2.83 of them, 2^-148.5 is 1.41, 2^-149.9 is
        // 0.54 and 2^-150 is half of one, which ties to even. Rounded, never flushed.
        Assert.Equal(BitConverter.Int32BitsToSingle(3), DeterministicMath.Exp2(-147.5f));
        Assert.Equal(BitConverter.Int32BitsToSingle(1), DeterministicMath.Exp2(-148.5f));
        Assert.Equal(BitConverter.Int32BitsToSingle(1), DeterministicMath.Exp2(-149.9f));
        Assert.Equal(0f, DeterministicMath.Exp2(-150f));
    }

    [Theory]
    [InlineData(128f, float.PositiveInfinity)]
    [InlineData(200f, float.PositiveInfinity)]
    [InlineData(float.PositiveInfinity, float.PositiveInfinity)]
    [InlineData(-151f, 0f)]
    [InlineData(-200f, 0f)]
    [InlineData(float.NegativeInfinity, 0f)]
    public void AnExponentAtTheEndsOfWhatAFloatHolds_IsWhatThatEndRoundsTo(float exponent, float expected)
    {
        Assert.Equal(expected, DeterministicMath.Exp2(exponent));
    }

    [Fact]
    public void AnExponentThatIsNotANumber_IsTheOneNotANumber()
    {
        Assert.Equal(
            BitConverter.SingleToInt32Bits(float.NaN),
            BitConverter.SingleToInt32Bits(DeterministicMath.Exp2(float.NaN)));
    }

    // The arctangent's result reaches pi, where half a float's step is just under 1.2e-7.
    [Fact]
    public void AnAngleToAPoint_IsWithinItsPublishedBound()
    {
        const double AngleBound = 1.2e-7;
        double worst = 0.0;

        // Every direction around the circle, at radii from a thousandth of a unit to a million, so the
        // fold about a sixth of pi and the swap about the diagonal are crossed in all four quadrants.
        foreach (float radius in new[] { 0.001f, 1f, 37.5f, 1_000_000f })
        {
            for (int sample = 0; sample < 200_000; sample++)
            {
                double turn = sample * (2.0 * Math.PI / 200_000);
                float x = (float)(radius * Math.Cos(turn));
                float y = (float)(radius * Math.Sin(turn));
                worst = Math.Max(worst, Math.Abs(DeterministicMath.Atan2(y, x) - Math.Atan2(y, x)));
            }
        }

        Assert.True(worst < AngleBound, $"the arctangent is out by {worst}.");
    }

    // Signed zeros compare equal, so the cases are a list and not inline data.
    [Fact]
    public void AnAngleOnAnAxisOrAtInfinity_FollowsTheSystemConventionsBitForBit()
    {
        (float Y, float X)[] points =
        [
            (0f, 0f), (-0f, 0f), (0f, -0f), (-0f, -0f), (0f, -3f), (-0f, -3f), (2f, 0f), (-2f, -0f),
            (float.PositiveInfinity, float.PositiveInfinity), (float.NegativeInfinity, float.NegativeInfinity),
            (float.PositiveInfinity, 5f), (-5f, float.PositiveInfinity), (-0f, float.NegativeInfinity),
        ];

        foreach ((float y, float x) in points)
        {
            Assert.Equal(
                BitConverter.SingleToInt32Bits((float)Math.Atan2(y, x)),
                BitConverter.SingleToInt32Bits(DeterministicMath.Atan2(y, x)));
        }

        Assert.Equal(
            BitConverter.SingleToInt32Bits(float.NaN),
            BitConverter.SingleToInt32Bits(DeterministicMath.Atan2(1f, float.NaN)));
    }

    private static double SineError(float radians) =>
        Math.Abs(DeterministicMath.Sin(radians) - Math.Sin(radians));
}
