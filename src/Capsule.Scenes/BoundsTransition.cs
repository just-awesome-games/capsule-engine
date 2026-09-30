using Capsule.Animation;
using Capsule.Diagnostics;

namespace Capsule.Scenes;

/// <summary>How a <see cref="Camera"/> carries a change of its <see cref="Camera.Bounds"/> into the view.</summary>
/// <remarks>
/// Pick <see cref="Snap"/> for a cut, <see cref="Smooth"/> for a soft settle, <see cref="AtSpeed"/> for
/// the fixed-rate scroll of a retro scroller and <see cref="Eased"/> to land in step with a tween. A
/// change keeps the transition it was made with until it lands or another change replaces it. An edge
/// opening to infinity, null bounds, <see cref="Camera.Teleport"/> and the camera's first step in a
/// scene take effect at once whatever the transition.
/// </remarks>
/// <example>
/// <code>
/// Scene.Camera.BoundsTransition = BoundsTransition.Smooth(0.35f);
/// Scene.Camera.SetBounds(arena, BoundsTransition.Eased(0.5f, Ease.InOutSine));
/// </code>
/// </example>
public readonly record struct BoundsTransition
{
    private enum Kind : byte
    {
        Snap,
        Chase,
        Eased,
    }

    private readonly Kind _kind;

    // The chase's smoothing or the eased move's length.
    internal float Seconds { get; }

    // The chase's cap in world units per second, where zero leaves it uncapped.
    internal float MaxSpeed { get; }

    internal Ease Ease { get; }

    private BoundsTransition(Kind kind, float seconds, float maxSpeed, Ease ease)
    {
        _kind = kind;
        Seconds = seconds;
        MaxSpeed = maxSpeed;
        Ease = ease;
    }

    /// <summary>Moves the view into changed bounds at once. It is the default.</summary>
    /// <example>
    /// <code>Scene.Camera.SetBounds(nextRoom, BoundsTransition.Snap);</code>
    /// </example>
    public static BoundsTransition Snap => default;

    internal bool IsChase => _kind == Kind.Chase;

    internal bool IsEased => _kind == Kind.Eased;

    /// <summary>
    /// Chases the view into changed bounds over about <paramref name="seconds"/>, no faster than
    /// <paramref name="maxSpeed"/> world units per second where zero, the default, leaves it uncapped.
    /// </summary>
    /// <remarks>
    /// An edge closing on the view starts at the view's own edge and pushes it. An opening edge keeps
    /// pace with the follow's own move and then gains on it. The cap limits the chase beyond that move.
    /// Zero seconds with a cap is <see cref="AtSpeed"/>. Zero seconds with no cap is <see cref="Snap"/>.
    /// </remarks>
    /// <example>
    /// <code>BoundsTransition = BoundsTransition.Smooth(0.35f);</code>
    /// </example>
    public static BoundsTransition Smooth(float seconds, float maxSpeed = 0f)
    {
        Guard.RequireSeconds(seconds, nameof(seconds));
        Guard.NonNegative(maxSpeed, nameof(maxSpeed));

        return seconds > 0f || maxSpeed > 0f ? new(Kind.Chase, seconds, maxSpeed, default) : Snap;
    }

    /// <summary>
    /// Pushes the view into changed bounds at <paramref name="unitsPerSecond"/> world units per second and
    /// lands once it is within one step.
    /// </summary>
    /// <remarks>
    /// An opening edge keeps pace with the follow's own move and gains on it at this speed. Zero is
    /// <see cref="Snap"/>.
    /// </remarks>
    /// <example>
    /// A scroll into each new screen at 2 units a step at 60 steps a second:
    /// <code>BoundsTransition = BoundsTransition.AtSpeed(120f);</code>
    /// </example>
    public static BoundsTransition AtSpeed(float unitsPerSecond)
    {
        Guard.NonNegative(unitsPerSecond, nameof(unitsPerSecond));

        return unitsPerSecond > 0f ? new(Kind.Chase, 0f, unitsPerSecond, default) : Snap;
    }

    /// <summary>
    /// Moves the view into changed bounds over <paramref name="seconds"/> along <paramref name="ease"/>
    /// and lands on the last step.
    /// </summary>
    /// <remarks>
    /// The move starts from the view where it stands and does not keep pace with the follow. Zero seconds
    /// is <see cref="Snap"/>.
    /// </remarks>
    /// <example>
    /// A boss arena framed on the same curve as a zoom tween that widens to show it, 30 ticks being half
    /// a second at 60 steps a second:
    /// <code>
    /// Scene.Camera.SetBounds(arena, BoundsTransition.Eased(0.5f, Ease.InOutSine));
    /// _zoom.Start(30, Ease.InOutSine);
    /// </code>
    /// </example>
    public static BoundsTransition Eased(float seconds, Ease ease)
    {
        Guard.RequireSeconds(seconds, nameof(seconds));
        Guard.RequireEase(ease, nameof(ease));

        return seconds > 0f ? new(Kind.Eased, seconds, 0f, ease) : Snap;
    }
}
