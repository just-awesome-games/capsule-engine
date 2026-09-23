namespace Capsule.Tests.Core;

// Signed zeros compare equal and NaN equals nothing, so each case is compared bit for bit against the
// system's double result rounded to float. The one NaN a member may return is float.NaN.
public sealed class DeterministicMathSpecialValuesTests
{
    [Fact]
    public void AZeroAnInfinityANaNOrADomainError_FollowsTheSystemConventionsBitForBit()
    {
        float[] edges = [0f, -0f, float.PositiveInfinity, float.NegativeInfinity, float.NaN];
        float[] bounded = [.. edges, 1f, -1f, 2f, -2f];

        AssertFollows(DeterministicMath.Tan, Math.Tan, edges);
        AssertFollows(DeterministicMath.Atan, Math.Atan, edges);
        AssertFollows(DeterministicMath.Exp, Math.Exp, edges);
        AssertFollows(DeterministicMath.Asin, Math.Asin, bounded);
        AssertFollows(DeterministicMath.Acos, Math.Acos, bounded);
        AssertFollows(DeterministicMath.Log2, Math.Log2, bounded);
        AssertFollows(DeterministicMath.Log, Math.Log, bounded);
        AssertFollows(DeterministicMath.Log10, Math.Log10, bounded);
    }

    // The special cases of C99's Annex F, which Math.Pow follows.
    [Fact]
    public void APowerAtAZeroAnInfinityANaNOrANegativeBase_FollowsTheSystemConventionsBitForBit()
    {
        const float Infinity = float.PositiveInfinity;
        (float Value, float Power)[] cases =
        [
            (float.NaN, 0f), (float.NaN, -0f), (1f, float.NaN), (1f, Infinity), (-1f, Infinity), (-1f, -Infinity),
            (float.NaN, 2f), (2f, float.NaN),
            (0f, -3f), (-0f, -3f), (0f, -2f), (-0f, -2f), (-0f, -0.5f), (-0f, -Infinity), (0f, -Infinity),
            (0f, 3f), (-0f, 3f), (-0f, 2f), (-0f, 0.5f), (0f, Infinity),
            (0.5f, -Infinity), (-0.5f, -Infinity), (2f, -Infinity), (-2f, -Infinity),
            (0.5f, Infinity), (-0.5f, Infinity), (2f, Infinity), (-2f, Infinity),
            (-Infinity, -3f), (-Infinity, -2f), (-Infinity, -0.5f), (-Infinity, 3f), (-Infinity, 2f), (-Infinity, 0.5f),
            (Infinity, -1f), (Infinity, 0.5f),
            (-2f, 0.5f), (-2f, -3f), (-2f, 3f), (-2f, 2f), (-1f, 3f), (-1f, 1e30f),
        ];

        foreach ((float value, float power) in cases)
        {
            Assert.True(
                Bits(Expected(Math.Pow(value, power))) == Bits(DeterministicMath.Pow(value, power)),
                $"Pow({value}, {power}) returned {DeterministicMath.Pow(value, power)}.");
        }
    }

    private static void AssertFollows(Func<float, float> subject, Func<double, double> reference, float[] values)
    {
        foreach (float value in values)
        {
            Assert.True(
                Bits(Expected(reference(value))) == Bits(subject(value)),
                $"{subject.Method.Name}({value}) returned {subject(value)}.");
        }
    }

    private static float Expected(double result) => double.IsNaN(result) ? float.NaN : (float)result;

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
}
