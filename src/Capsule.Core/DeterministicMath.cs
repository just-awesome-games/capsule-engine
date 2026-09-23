namespace Capsule;

/// <summary>
/// The transcendental functions a simulation calls instead of <see cref="MathF"/>, whose results
/// differ between operating systems. Each function is built from operations IEEE 754 specifies
/// exactly.
/// </summary>
/// <remarks>
/// Its result depends only on the bits of its arguments and is identical on every platform. No
/// function is correctly rounded, and each member states its error bound. Presentation-only
/// arithmetic may call <see cref="MathF"/>.
/// <para>
/// NaN, the infinities, the signed zeros, a domain error and the quadrant of <see cref="Atan2"/>
/// follow the <see cref="Math"/> function's double result rounded to float, and every NaN returned
/// is <see cref="float.NaN"/>.
/// </para>
/// </remarks>
///
public static class DeterministicMath
{
    // The working is double because argument reduction needs headroom a float cannot give, and each result
    // is rounded to float once at the end. No expression here is a fused multiply-add, which would round
    // once where this rounds twice. RyuJIT contracts none on its own.
    private const double TwoPi = 2.0 * Math.PI;
    private const double InverseTwoPi = 1.0 / TwoPi;
    private const double HalfPi = Math.PI * 0.5;
    private const double Ln2 = 0.69314718055994531;
    private const double Log2E = 1.4426950408889634;
    private const double Log10Of2 = 0.30102999566398120;
    private const double Sqrt2 = 1.4142135623730951;
    private const double TwoOverPi = 2.0 / Math.PI;

    // What the nearest double to 2 pi leaves out of the real value. The turn is split into a head of 25
    // significant bits, whose product with any whole turn count is exact, and a tail carrying the rest.
    // The split keeps the fold accurate at arguments of millions of turns.
    private const double TwoPiResidue = 2.4492935982947064e-16;

    private static readonly double TwoPiHead =
        BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(TwoPi) & ~0xFFFFFFFL);

    private static readonly double TwoPiTail = (TwoPi - TwoPiHead) + TwoPiResidue;

    // The tangent folds by quarter turns in three parts. A whole quarter count times the 25-bit head or the
    // 28-bit middle is exact, and so is each subtraction, which leaves one rounding against the residue.
    private const double HalfPiResidue = 6.123233995736766e-17;

    private static readonly double HalfPiHead =
        BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(HalfPi) & ~0xFFFFFFFL);

    private static readonly double HalfPiMiddle = HalfPi - HalfPiHead;

    // The odd Taylor series of sine. It is accurate past float precision over the quarter turn the
    // argument is folded onto.
    private const double SinTerm3 = -1.0 / 6.0;
    private const double SinTerm5 = 1.0 / 120.0;
    private const double SinTerm7 = -1.0 / 5040.0;
    private const double SinTerm9 = 1.0 / 362880.0;
    private const double SinTerm11 = -1.0 / 39916800.0;
    private const double SinTerm13 = 1.0 / 6227020800.0;

    // The even Taylor series of cosine, accurate past float precision over the eighth turn the tangent
    // folds onto.
    private const double CosTerm2 = -1.0 / 2.0;
    private const double CosTerm4 = 1.0 / 24.0;
    private const double CosTerm6 = -1.0 / 720.0;
    private const double CosTerm8 = 1.0 / 40320.0;
    private const double CosTerm10 = -1.0 / 3628800.0;
    private const double CosTerm12 = 1.0 / 479001600.0;
    private const double CosTerm14 = -1.0 / 87178291200.0;

    // The odd series of the inverse hyperbolic tangent. Twice it at (m - 1) / (m + 1) is the natural
    // logarithm of a mantissa m centred on one, where the ratio stays under 0.172 in magnitude.
    private const double LogTerm3 = 1.0 / 3.0;
    private const double LogTerm5 = 1.0 / 5.0;
    private const double LogTerm7 = 1.0 / 7.0;
    private const double LogTerm9 = 1.0 / 9.0;
    private const double LogTerm11 = 1.0 / 11.0;
    private const double LogTerm13 = 1.0 / 13.0;
    private const double LogTerm15 = 1.0 / 15.0;
    private const double LogTerm17 = 1.0 / 17.0;
    private const double LogTerm19 = 1.0 / 19.0;

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

    // The odd Taylor series of the arctangent, accurate past float precision over the pi/12 the ratio is
    // folded onto.
    private const double AtanTerm3 = -1.0 / 3.0;
    private const double AtanTerm5 = 1.0 / 5.0;
    private const double AtanTerm7 = -1.0 / 7.0;
    private const double AtanTerm9 = 1.0 / 9.0;
    private const double AtanTerm11 = -1.0 / 11.0;
    private const double AtanTerm13 = 1.0 / 13.0;
    private const double AtanTerm15 = -1.0 / 15.0;
    private const double AtanTerm17 = 1.0 / 17.0;

    // The tangent of pi/12, the ratio above which the fold shifts by pi/6.
    private const double TanTwelfthPi = 0.26794919243112270;
    private const double InverseSqrt3 = 0.57735026918962576;
    private const double SixthPi = Math.PI / 6.0;

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
    /// The tangent of <paramref name="radians"/>, within a relative 6e-8 of the true tangent for any
    /// argument of magnitude up to 2^24 (16777216), with no stated bound beyond that.
    /// </summary>
    /// <param name="radians">The angle in radians. Accurate to a magnitude of 2^24.</param>
    public static float Tan(float radians)
    {
        if (!float.IsFinite(radians))
        {
            return float.NaN;
        }

        double quarters = Math.Floor((radians * TwoOverPi) + 0.5);
        double x = ((radians - (quarters * HalfPiHead)) - (quarters * HalfPiMiddle)) - (quarters * HalfPiResidue);
        double sine = SineSeries(x);
        double cosine = CosineSeries(x * x);
        double tangent = Math.Floor(quarters * 0.5) == quarters * 0.5 ? sine / cosine : -cosine / sine;

        // Past the stated range the fold is not exact, and an overflowing series can divide infinity by
        // infinity. The platform would decide that NaN's bits.
        return double.IsNaN(tangent) ? float.NaN : (float)tangent;
    }

    /// <summary>
    /// Two raised to <paramref name="exponent"/>, within a relative 6e-8 of the true value and exact
    /// at every whole exponent from -149 to 127, subnormals included. An exponent at or above 128
    /// returns <see cref="float.PositiveInfinity"/>, one at or below -150 returns zero, and NaN
    /// returns NaN.
    /// </summary>
    public static float Exp2(float exponent) => Exp2Of(exponent);

    /// <summary>
    /// The natural exponential of <paramref name="exponent"/>, within a relative 6e-8 of the true value
    /// wherever that is a normal float.
    /// </summary>
    public static float Exp(float exponent) => Exp2Of(exponent * Log2E);

    /// <summary>
    /// The base-two logarithm of <paramref name="value"/>, within a relative 6e-8 of the true logarithm.
    /// </summary>
    public static float Log2(float value) =>
        float.IsNaN(value) || value < 0f ? float.NaN : (float)Log2Of(value);

    /// <summary>
    /// The natural logarithm of <paramref name="value"/>, within a relative 6e-8 of the true logarithm.
    /// </summary>
    public static float Log(float value) =>
        float.IsNaN(value) || value < 0f ? float.NaN : (float)(Log2Of(value) * Ln2);

    /// <summary>
    /// The base-ten logarithm of <paramref name="value"/>, within a relative 6e-8 of the true logarithm.
    /// </summary>
    public static float Log10(float value) =>
        float.IsNaN(value) || value < 0f ? float.NaN : (float)(Log2Of(value) * Log10Of2);

    /// <summary>
    /// <paramref name="value"/> raised to <paramref name="power"/>, within a relative 6e-8 of the true value
    /// wherever that is a normal float.
    /// </summary>
    public static float Pow(float value, float power)
    {
        if (power == 0f || value == 1f)
        {
            return 1f;
        }

        if (float.IsNaN(value) || float.IsNaN(power))
        {
            return float.NaN;
        }

        double exponent = power;
        bool whole = Math.Floor(exponent) == exponent;
        if (!whole && value < 0f && float.IsFinite(value))
        {
            return float.NaN;
        }

        // Every float at or past 2^24 is even, and an infinity is neither odd nor even.
        bool odd = whole && float.IsFinite(power) && Math.Floor(exponent * 0.5) != exponent * 0.5;

        // A magnitude of one is settled here, so an infinite power never multiplies a zero logarithm.
        double magnitude = Math.Abs((double)value);
        float result = magnitude == 1.0 ? 1f : Exp2Of(exponent * Log2Of(magnitude));

        return odd && BitConverter.SingleToInt32Bits(value) < 0 ? -result : result;
    }

    private static float Exp2Of(double exponent)
    {
        // Checked first. Converting NaN to an integer is the only operation here whose result the platform
        // decides instead of IEEE.
        if (double.IsNaN(exponent))
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

    /// <summary>
    /// The angle in radians from the positive X axis to the point (<paramref name="x"/>,
    /// <paramref name="y"/>), in [-pi, pi] and within 1.2e-7 of the true angle in absolute terms.
    /// </summary>
    /// <param name="y">The point's Y. World Y runs down, and a positive angle turns clockwise on screen.</param>
    /// <param name="x">The point's X.</param>
    public static float Atan2(float y, float x)
    {
        if (float.IsNaN(y) || float.IsNaN(x))
        {
            return float.NaN;
        }

        // An infinite argument dominates a finite one. Mapping each infinity to a signed one and each
        // finite value to a signed zero gives the quarter and eighth turns the infinities stand for.
        if (float.IsInfinity(y) || float.IsInfinity(x))
        {
            y = float.IsInfinity(y) ? MathF.CopySign(1f, y) : MathF.CopySign(0f, y);
            x = float.IsInfinity(x) ? MathF.CopySign(1f, x) : MathF.CopySign(0f, x);
        }

        double angle = QuadrantAngle(Math.Abs((double)y), Math.Abs((double)x));

        // The sign bit and not a comparison, so a negative zero X looks back along the axis.
        if (BitConverter.SingleToInt32Bits(x) < 0)
        {
            angle = Math.PI - angle;
        }

        return MathF.CopySign((float)angle, y);
    }

    /// <summary>
    /// The arctangent of <paramref name="value"/> in radians, in [-pi/2, pi/2] and within a relative 6e-8
    /// of the true angle.
    /// </summary>
    public static float Atan(float value) =>
        float.IsNaN(value) ? float.NaN : MathF.CopySign((float)QuadrantAngle(Math.Abs((double)value), 1.0), value);

    /// <summary>
    /// The arcsine of <paramref name="value"/> in radians, in [-pi/2, pi/2] and within a relative 6e-8 of
    /// the true angle.
    /// </summary>
    public static float Asin(float value)
    {
        if (float.IsNaN(value) || value > 1f || value < -1f)
        {
            return float.NaN;
        }

        // Squaring a float is exact in double, and so is the subtraction wherever the root is small.
        double across = Math.Sqrt(1.0 - ((double)value * value));
        return MathF.CopySign((float)QuadrantAngle(Math.Abs((double)value), across), value);
    }

    /// <summary>
    /// The arccosine of <paramref name="value"/> in radians, in [0, pi] and within a relative 6e-8 of the
    /// true angle.
    /// </summary>
    public static float Acos(float value)
    {
        if (float.IsNaN(value) || value > 1f || value < -1f)
        {
            return float.NaN;
        }

        double up = Math.Sqrt(1.0 - ((double)value * value));
        double angle = QuadrantAngle(up, Math.Abs((double)value));
        return (float)(value < 0f ? Math.PI - angle : angle);
    }

    // The angle to a point in the first quadrant, folded about the diagonal so the arctangent's ratio
    // stays in [0, 1].
    private static double QuadrantAngle(double up, double across)
    {
        if (up == 0.0 && across == 0.0)
        {
            return 0.0;
        }

        return up > across ? HalfPi - ArctangentOf(across / up) : ArctangentOf(up / across);
    }

    // The arctangent of a ratio in [0, 1]. Above tan(pi/12) it is folded by the addition formula about
    // pi/6, which leaves the series a ratio of at most that tangent in magnitude.
    private static double ArctangentOf(double ratio)
    {
        double offset = 0.0;
        if (ratio > TanTwelfthPi)
        {
            ratio = (ratio - InverseSqrt3) / (1.0 + (ratio * InverseSqrt3));
            offset = SixthPi;
        }

        double squared = ratio * ratio;
        double series = squared * AtanTerm17;
        series = squared * (AtanTerm15 + series);
        series = squared * (AtanTerm13 + series);
        series = squared * (AtanTerm11 + series);
        series = squared * (AtanTerm9 + series);
        series = squared * (AtanTerm7 + series);
        series = squared * (AtanTerm5 + series);
        series = squared * (AtanTerm3 + series);

        return offset + (ratio * (1.0 + series));
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

        return SineSeries(x);
    }

    // Accurate past float precision over a quarter turn about zero.
    private static double SineSeries(double x)
    {
        double squared = x * x;
        double series = squared * SinTerm13;
        series = squared * (SinTerm11 + series);
        series = squared * (SinTerm9 + series);
        series = squared * (SinTerm7 + series);
        series = squared * (SinTerm5 + series);
        series = squared * (SinTerm3 + series);

        return x * (1.0 + series);
    }

    private static double CosineSeries(double squared)
    {
        double series = squared * CosTerm14;
        series = squared * (CosTerm12 + series);
        series = squared * (CosTerm10 + series);
        series = squared * (CosTerm8 + series);
        series = squared * (CosTerm6 + series);
        series = squared * (CosTerm4 + series);
        series = squared * (CosTerm2 + series);

        return 1.0 + series;
    }

    // The base-two logarithm of a positive float widened to double. The exponent comes off the bits
    // exactly, and the mantissa is centred on one for the series to converge fast.
    private static double Log2Of(double value)
    {
        if (value == 0.0)
        {
            return double.NegativeInfinity;
        }

        if (double.IsPositiveInfinity(value))
        {
            return double.PositiveInfinity;
        }

        long bits = BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)(bits >> 52) - 1023;
        double mantissa = BitConverter.Int64BitsToDouble((bits & 0xFFFFFFFFFFFFFL) | 0x3FF0000000000000L);
        if (mantissa > Sqrt2)
        {
            mantissa *= 0.5;
            exponent++;
        }

        // Both sides of the ratio are exact for a mantissa that came from a float.
        double ratio = (mantissa - 1.0) / (mantissa + 1.0);
        double squared = ratio * ratio;
        double series = squared * LogTerm19;
        series = squared * (LogTerm17 + series);
        series = squared * (LogTerm15 + series);
        series = squared * (LogTerm13 + series);
        series = squared * (LogTerm11 + series);
        series = squared * (LogTerm9 + series);
        series = squared * (LogTerm7 + series);
        series = squared * (LogTerm5 + series);
        series = squared * (LogTerm3 + series);

        return exponent + ((2.0 * ratio) * (1.0 + series) * Log2E);
    }
}
