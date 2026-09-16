namespace Capsule.Animation;

/// <summary>
/// The shape a normalised progress is bent into on its way from 0 to 1: <c>In</c> eases at the
/// start, <c>Out</c> at the end, <c>InOut</c> at both. Every member passes through 0 at 0 and 1 at
/// 1; only the back and elastic families leave the unit range in between.
/// </summary>
public enum Ease
{
    /// <summary>No bend: progress is its own curve.</summary>
    Linear,

    /// <summary>A quarter of a cosine, the gentlest start of the families.</summary>
    InSine,

    /// <summary>A quarter of a sine, the gentlest stop of the families.</summary>
    OutSine,

    /// <summary>A half cosine: a gentle start and stop.</summary>
    InOutSine,

    /// <summary>Second-power acceleration from rest.</summary>
    InQuad,

    /// <summary>Second-power deceleration to rest.</summary>
    OutQuad,

    /// <summary>Second-power acceleration then deceleration.</summary>
    InOutQuad,

    /// <summary>Third-power acceleration from rest.</summary>
    InCubic,

    /// <summary>Third-power deceleration to rest.</summary>
    OutCubic,

    /// <summary>Third-power acceleration then deceleration.</summary>
    InOutCubic,

    /// <summary>Fourth-power acceleration from rest.</summary>
    InQuart,

    /// <summary>Fourth-power deceleration to rest.</summary>
    OutQuart,

    /// <summary>Fourth-power acceleration then deceleration.</summary>
    InOutQuart,

    /// <summary>Fifth-power acceleration from rest.</summary>
    InQuint,

    /// <summary>Fifth-power deceleration to rest.</summary>
    OutQuint,

    /// <summary>Fifth-power acceleration then deceleration.</summary>
    InOutQuint,

    /// <summary>Doubling every tenth of the way: near-motionless, then abrupt.</summary>
    InExpo,

    /// <summary>Halving every tenth of the way: abrupt, then near-motionless.</summary>
    OutExpo,

    /// <summary>Exponential acceleration then deceleration.</summary>
    InOutExpo,

    /// <summary>A quarter circle: slow, then steepening to vertical.</summary>
    InCirc,

    /// <summary>A quarter circle: vertical, then flattening out.</summary>
    OutCirc,

    /// <summary>A half circle: vertical in the middle, flat at both ends.</summary>
    InOutCirc,

    /// <summary>Draws back below 0 before setting off.</summary>
    InBack,

    /// <summary>Overshoots past 1 before settling on it.</summary>
    OutBack,

    /// <summary>Draws back below 0, then overshoots past 1.</summary>
    InOutBack,

    /// <summary>A decaying oscillation swelling out of 0.</summary>
    InElastic,

    /// <summary>A decaying oscillation settling onto 1.</summary>
    OutElastic,

    /// <summary>A decaying oscillation at both ends.</summary>
    InOutElastic,

    /// <summary>Successive smaller rebounds gathering into the start.</summary>
    InBounce,

    /// <summary>Successive smaller rebounds settling onto the end.</summary>
    OutBounce,

    /// <summary>Rebounds at both ends.</summary>
    InOutBounce,
}
