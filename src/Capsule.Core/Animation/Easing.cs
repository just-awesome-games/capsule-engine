namespace Capsule.Animation;

/// <summary>
/// Evaluates the curves <see cref="Ease"/> names: pure arithmetic over one normalised progress,
/// holding no state, allocating nothing and safe from any thread.
/// </summary>
public static class Easing
{
    // Robert Penner's constants as published on easings.net: 1.70158 overshoots back by about a
    // tenth of the span and 1.525 scales that for the in-out form, the elastic periods are 2π/3 and
    // 2π/4.5, and bounce's 7.5625 and 2.75 are the parabola gain and segment divisor of its four.
    private const float BackOvershoot = 1.70158f;
    private const float BackInOutOvershoot = BackOvershoot * 1.525f;
    private const float ElasticPeriod = 2f * MathF.PI / 3f;
    private const float ElasticInOutPeriod = 2f * MathF.PI / 4.5f;
    private const float BounceAmplitude = 7.5625f;
    private const float BounceSegment = 2.75f;

    /// <summary>
    /// The eased position at normalised progress <paramref name="t"/>, which is clamped to
    /// <c>[0, 1]</c> and read as 0 where it is not a number. Every curve returns the literal
    /// <c>0</c> and <c>1</c> at the endpoints and only the overshooting ones leave that range
    /// between them.
    /// </summary>
    /// <param name="ease">The curve to evaluate.</param>
    /// <param name="t">Progress from 0 to 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">The curve is not a declared <see cref="Ease"/>.</exception>
    /// <remarks>
    /// Every family is exact IEEE arithmetic, evaluated through <see cref="DeterministicMath"/>
    /// rather than the platform's transcendental functions, so a curve answers a given progress with
    /// the same bits on every operating system.
    /// </remarks>
    public static float Apply(Ease ease, float t)
    {
        // Ahead of the clamp, so an undeclared curve is refused at every progress rather than only
        // between the endpoints. The members are contiguous from Linear.
        if (ease is < Ease.Linear or > Ease.InOutBounce)
        {
            throw new ArgumentOutOfRangeException(nameof(ease), ease, "No such easing curve.");
        }

        if (t <= 0f || float.IsNaN(t))
        {
            return 0f;
        }

        if (t >= 1f)
        {
            return 1f;
        }

        return ease switch
        {
            Ease.Linear => t,

            Ease.InSine => InSine(t),
            Ease.OutSine => 1f - InSine(1f - t),
            Ease.InOutSine => t < 0.5f ? InSine(t + t) * 0.5f : 1f - (InSine(2f - t - t) * 0.5f),

            Ease.InQuad => InQuad(t),
            Ease.OutQuad => 1f - InQuad(1f - t),
            Ease.InOutQuad => t < 0.5f ? InQuad(t + t) * 0.5f : 1f - (InQuad(2f - t - t) * 0.5f),

            Ease.InCubic => InCubic(t),
            Ease.OutCubic => 1f - InCubic(1f - t),
            Ease.InOutCubic => t < 0.5f ? InCubic(t + t) * 0.5f : 1f - (InCubic(2f - t - t) * 0.5f),

            Ease.InQuart => InQuart(t),
            Ease.OutQuart => 1f - InQuart(1f - t),
            Ease.InOutQuart => t < 0.5f ? InQuart(t + t) * 0.5f : 1f - (InQuart(2f - t - t) * 0.5f),

            Ease.InQuint => InQuint(t),
            Ease.OutQuint => 1f - InQuint(1f - t),
            Ease.InOutQuint => t < 0.5f ? InQuint(t + t) * 0.5f : 1f - (InQuint(2f - t - t) * 0.5f),

            Ease.InExpo => InExpo(t),
            Ease.OutExpo => 1f - InExpo(1f - t),
            Ease.InOutExpo => t < 0.5f ? InExpo(t + t) * 0.5f : 1f - (InExpo(2f - t - t) * 0.5f),

            Ease.InCirc => InCirc(t),
            Ease.OutCirc => 1f - InCirc(1f - t),
            Ease.InOutCirc => t < 0.5f ? InCirc(t + t) * 0.5f : 1f - (InCirc(2f - t - t) * 0.5f),

            Ease.InBack => InBack(t),
            Ease.OutBack => 1f - InBack(1f - t),
            Ease.InOutBack => InOutBack(t),

            Ease.InElastic => InElastic(t),
            Ease.OutElastic => 1f - InElastic(1f - t),
            Ease.InOutElastic => InOutElastic(t),

            Ease.InBounce => 1f - OutBounce(1f - t),
            Ease.OutBounce => OutBounce(t),
            Ease.InOutBounce => t < 0.5f
                ? (1f - OutBounce(1f - t - t)) * 0.5f
                : (1f + OutBounce(t + t - 1f)) * 0.5f,

            // Unreachable: the range check above admits only the members the arms enumerate, and the
            // compiler still wants the arm.
            _ => throw new ArgumentOutOfRangeException(nameof(ease), ease, "No such easing curve."),
        };
    }

    private static float InSine(float t) => 1f - DeterministicMath.Cos(t * MathF.PI * 0.5f);

    private static float InQuad(float t) => t * t;

    private static float InCubic(float t) => t * t * t;

    private static float InQuart(float t) => t * t * t * t;

    private static float InQuint(float t) => t * t * t * t * t;

    private static float InExpo(float t) => DeterministicMath.Exp2((10f * t) - 10f);

    private static float InCirc(float t) => 1f - MathF.Sqrt(1f - (t * t));

    private static float InBack(float t) => ((BackOvershoot + 1f) * t * t * t) - (BackOvershoot * t * t);

    // Its own overshoot rather than the halved InBack curve, so each half pulls back as far as the
    // one-sided curves do.
    private static float InOutBack(float t)
    {
        float scaled = t + t;
        float settled = scaled - 2f;

        return t < 0.5f
            ? scaled * scaled * (((BackInOutOvershoot + 1f) * scaled) - BackInOutOvershoot) * 0.5f
            : ((settled * settled * (((BackInOutOvershoot + 1f) * settled) + BackInOutOvershoot)) + 2f) * 0.5f;
    }

    private static float InElastic(float t) =>
        -DeterministicMath.Exp2((10f * t) - 10f) * DeterministicMath.Sin(((10f * t) - 10.75f) * ElasticPeriod);

    // Its own period, so the two halves oscillate at the rate the one-sided curves do over half the
    // span rather than at half of it.
    private static float InOutElastic(float t)
    {
        float phase = DeterministicMath.Sin(((20f * t) - 11.125f) * ElasticInOutPeriod);

        return t < 0.5f
            ? -(DeterministicMath.Exp2((20f * t) - 10f) * phase) * 0.5f
            : (DeterministicMath.Exp2(10f - (20f * t)) * phase * 0.5f) + 1f;
    }

    // Bounce is defined settling onto the end; the other two directions are reflections of it.
    private static float OutBounce(float t)
    {
        if (t < 1f / BounceSegment)
        {
            return BounceAmplitude * t * t;
        }

        if (t < 2f / BounceSegment)
        {
            float offset = t - (1.5f / BounceSegment);

            return (BounceAmplitude * offset * offset) + 0.75f;
        }

        if (t < 2.5f / BounceSegment)
        {
            float offset = t - (2.25f / BounceSegment);

            return (BounceAmplitude * offset * offset) + 0.9375f;
        }

        float last = t - (2.625f / BounceSegment);

        return (BounceAmplitude * last * last) + 0.984375f;
    }
}
