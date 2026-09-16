namespace Capsule;

/// <summary>
/// The transcendental functions a simulation calls instead of <see cref="MathF"/>, whose sine and
/// exponential are correctly rounded by no standard and differ between operating systems. Each is a
/// polynomial over operations IEEE 754 specifies exactly, so a result is a function of the bits of
/// its argument alone and identical on every platform.
/// <para>
/// None is correctly rounded or accurate everywhere: each member states its bound and the range it
/// holds over. Presentation-only arithmetic, which no other machine has to agree with, is free to
/// call <see cref="MathF"/> and be quicker about it.
/// </para>
/// </summary>
public static class DeterministicMath
{
    // The working is double because the argument reduction needs headroom a float cannot give it;
    // each result is rounded to float once, at the end. A fused multiply-add would round once where
    // this rounds twice, so no expression here is written as one and MathF.FusedMultiplyAdd is never
    // called — RyuJIT contracts none on its own.
    private const double TwoPi = 2.0 * Math.PI;
    private const double InverseTwoPi = 1.0 / TwoPi;
    private const double HalfPi = Math.PI * 0.5;
    private const double Ln2 = 0.69314718055994531;

    // What the nearest double to 2π leaves out of the real one. The turn is split into a head of 25
    // significant bits, whose product with any whole turn count is exact, and a tail carrying the
    // rest, which is what keeps the fold accurate at an argument of millions of turns.
    private const double TwoPiResidue = 2.4492935982947064e-16;

    private static readonly double TwoPiHead =
        BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(TwoPi) & ~0xFFFFFFFL);

    private static readonly double TwoPiTail = (TwoPi - TwoPiHead) + TwoPiResidue;

    // The odd Taylor series of sine, accurate past what a float holds over the quarter turn the
    // argument is folded onto.
    private const double SinTerm3 = -1.0 / 6.0;
    private const double SinTerm5 = 1.0 / 120.0;
    private const double SinTerm7 = -1.0 / 5040.0;
    private const double SinTerm9 = 1.0 / 362880.0;
    private const double SinTerm11 = -1.0 / 39916800.0;
    private const double SinTerm13 = 1.0 / 6227020800.0;

    // The exponential series in x * ln 2, over the fraction of the exponent left once its whole part
    // is taken out.
    private const double ExpTerm2 = 1.0 / 2.0;
    private const double ExpTerm3 = 1.0 / 6.0;
    private const double ExpTerm4 = 1.0 / 24.0;
    private const double ExpTerm5 = 1.0 / 120.0;
    private const double ExpTerm6 = 1.0 / 720.0;
    private const double ExpTerm7 = 1.0 / 5040.0;
    private const double ExpTerm8 = 1.0 / 40320.0;
    private const double ExpTerm9 = 1.0 / 362880.0;
    private const double ExpTerm10 = 1.0 / 3628800.0;

    /// <summary>
    /// The sine of <paramref name="radians"/>, within 6e-8 of the true sine in absolute terms for any
    /// argument of magnitude up to 2^24 (16777216) and with no stated bound beyond that. Infinity or
    /// a value that is not a number gives back <see cref="float.NaN"/> itself, bit for bit.
    /// </summary>
    /// <param name="radians">The angle, in radians; accurate to a magnitude of 2^24.</param>
    public static float Sin(float radians) =>
        float.IsFinite(radians) ? (float)SineOf(radians) : float.NaN;

    /// <summary>
    /// The cosine of <paramref name="radians"/>, on <see cref="Sin"/>'s terms, bound and range.
    /// </summary>
    /// <param name="radians">The angle, in radians; accurate to a magnitude of 2^24.</param>
    public static float Cos(float radians) =>
        float.IsFinite(radians) ? (float)SineOf(HalfPi - radians) : float.NaN;

    /// <summary>
    /// Two raised to <paramref name="exponent"/>, within 6e-8 of the true value relative to it, and
    /// exactly the power of two at every whole exponent from -149 to 127, subnormals included. A
    /// result under the normal range is rounded onto the subnormal grid rather than flushed away.
    /// <para>
    /// An exponent at or above 128 gives <see cref="float.PositiveInfinity"/>; at -150 the true value
    /// is exactly half the smallest subnormal and rounds to zero, as does every exponent below it;
    /// one that is not a number gives one back.
    /// </para>
    /// </summary>
    /// <param name="exponent">The power to raise two to.</param>
    public static float Exp2(float exponent)
    {
        // Ahead of everything: converting a value that is not a number to an integer is the one
        // operation here whose result the platform, not IEEE, decides.
        if (float.IsNaN(exponent))
        {
            return float.NaN;
        }

        double whole = Math.Floor(exponent);

        if (whole >= 128.0)
        {
            return float.PositiveInfinity;
        }

        // Below this the true value is under half the smallest subnormal however large the fraction
        // is, so the result is zero and the scale below has nothing to carry.
        if (whole < -150.0)
        {
            return 0f;
        }

        // The series carries the fraction alone, and the whole part is a power of two: scaling by it
        // is exact and costs the result no accuracy at all.
        double u = (exponent - whole) * Ln2;
        double series = u * ExpTerm10;
        series = u * (ExpTerm9 + series);
        series = u * (ExpTerm8 + series);
        series = u * (ExpTerm7 + series);
        series = u * (ExpTerm6 + series);
        series = u * (ExpTerm5 + series);
        series = u * (ExpTerm4 + series);
        series = u * (ExpTerm3 + series);
        series = u * (ExpTerm2 + series);
        series = u * (1.0 + series);
        series += 1.0;

        // A power of two built in double, where every exponent this far down is still normal, so the
        // product is exact and the conversion back to float is the one rounding the result costs —
        // onto the subnormal grid as readily as the normal one, and to infinity past what it holds.
        double scale = BitConverter.Int64BitsToDouble((long)((int)whole + 1023) << 52);

        return (float)(series * scale);
    }

    private static double SineOf(double radians)
    {
        // Folded onto one turn about zero, then onto the quarter turn the series is accurate over,
        // which sine's own symmetry gives for nothing. Head first and tail second, so the
        // cancellation the fold lives on happens once, against a multiple that was exact.
        double turns = Math.Floor((radians * InverseTwoPi) + 0.5);
        double x = (radians - (turns * TwoPiHead)) - (turns * TwoPiTail);

        if (x > HalfPi)
        {
            x = Math.PI - x;
        }
        else if (x < -HalfPi)
        {
            x = -Math.PI - x;
        }

        double squared = x * x;
        double series = squared * SinTerm13;
        series = squared * (SinTerm11 + series);
        series = squared * (SinTerm9 + series);
        series = squared * (SinTerm7 + series);
        series = squared * (SinTerm5 + series);
        series = squared * (SinTerm3 + series);

        return x * (1.0 + series);
    }
}
