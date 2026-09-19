namespace Capsule;

/// <summary>
/// The transcendental functions a simulation calls instead of <see cref="MathF"/>, whose sine and
/// exponential are correctly rounded by no standard and differ between operating systems. Each
/// function here is a polynomial over operations IEEE 754 specifies exactly, so its result depends
/// only on the bits of its argument and is identical on every platform. None is correctly rounded, so
/// each member states its error bound. Presentation-only arithmetic may call <see cref="MathF"/>.
/// </summary>
public static class DeterministicMath
{
    // The working is double because argument reduction needs headroom a float cannot give, and each result
    // is rounded to float once at the end. No expression here is a fused multiply-add, which would round
    // once where this rounds twice. RyuJIT contracts none on its own.
    private const double TwoPi = 2.0 * Math.PI;
    private const double InverseTwoPi = 1.0 / TwoPi;
    private const double HalfPi = Math.PI * 0.5;
    private const double Ln2 = 0.69314718055994531;

    // What the nearest double to 2 pi leaves out of the real value. The turn is split into a head of 25
    // significant bits, whose product with any whole turn count is exact, and a tail carrying the rest.
    // The split keeps the fold accurate at arguments of millions of turns.
    private const double TwoPiResidue = 2.4492935982947064e-16;

    private static readonly double TwoPiHead =
        BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(TwoPi) & ~0xFFFFFFFL);

    private static readonly double TwoPiTail = (TwoPi - TwoPiHead) + TwoPiResidue;

    // The odd Taylor series of sine. It is accurate past float precision over the quarter turn the
    // argument is folded onto.
    private const double SinTerm3 = -1.0 / 6.0;
    private const double SinTerm5 = 1.0 / 120.0;
    private const double SinTerm7 = -1.0 / 5040.0;
    private const double SinTerm9 = 1.0 / 362880.0;
    private const double SinTerm11 = -1.0 / 39916800.0;
    private const double SinTerm13 = 1.0 / 6227020800.0;

    // The exponential series in x * ln 2, over the fractional part of the exponent.
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
    /// argument of magnitude up to 2^24 (16777216), with no stated bound beyond that. Infinity or NaN
    /// returns <see cref="float.NaN"/>.
    /// </summary>
    /// <param name="radians">The angle in radians. Accurate to a magnitude of 2^24.</param>
    public static float Sin(float radians) =>
        float.IsFinite(radians) ? (float)SineOf(radians) : float.NaN;

    /// <summary>The cosine of <paramref name="radians"/>, with <see cref="Sin"/>'s bound and range.</summary>
    /// <param name="radians">The angle in radians. Accurate to a magnitude of 2^24.</param>
    public static float Cos(float radians) =>
        float.IsFinite(radians) ? (float)SineOf(HalfPi - radians) : float.NaN;

    /// <summary>
    /// Two raised to <paramref name="exponent"/>, within a relative 6e-8 of the true value and exact
    /// at every whole exponent from -149 to 127, subnormals included. An exponent at or above 128
    /// returns <see cref="float.PositiveInfinity"/>, one at or below -150 returns zero, and NaN
    /// returns NaN.
    /// </summary>
    public static float Exp2(float exponent)
    {
        // Checked first. Converting NaN to an integer is the only operation here whose result the platform
        // decides instead of IEEE.
        if (float.IsNaN(exponent))
        {
            return float.NaN;
        }

        double whole = Math.Floor(exponent);

        if (whole >= 128.0)
        {
            return float.PositiveInfinity;
        }

        // Below this the true value is under half the smallest subnormal whatever the fraction.
        if (whole < -150.0)
        {
            return 0f;
        }

        // The series carries only the fraction. The whole part is a power of two, so scaling by it is exact
        // and costs no accuracy.
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

        // A power of two built in double, where every exponent this far down is still normal. The product is
        // exact, so the conversion back to float is the result's only rounding.
        double scale = BitConverter.Int64BitsToDouble((long)((int)whole + 1023) << 52);

        return (float)(series * scale);
    }

    private static double SineOf(double radians)
    {
        // Fold onto one turn about zero, then onto the quarter turn the series is accurate over, which
        // sine's symmetry gives for free. Subtract the head first and the tail second, so the fold's
        // cancellation happens once and against an exact multiple.
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
