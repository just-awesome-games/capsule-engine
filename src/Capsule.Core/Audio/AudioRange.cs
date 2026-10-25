using Capsule.Animation;

namespace Capsule.Audio;

/// <summary>
/// How far a voice is heard from its position in the world, and how its gain falls off on the way.
/// </summary>
/// <remarks>
/// The listener is the centre of the scene camera's <c>VisibleRegion</c>. A voice at distance d
/// plays at <c>1 - Falloff(d / MaxDistance)</c> of its gain, and is silent at
/// <see cref="MaxDistance"/> and beyond while keeping its time.
/// </remarks>
/// <example>
/// <code>
/// Add(new AudioSource(CapsuleAssets.Audio.HumSound)
/// {
///     Loop = true,
///     AutoPlay = true,
///     Range = new AudioRange(200f) { Falloff = Ease.InQuad },
/// });
/// </code>
/// </example>
public readonly record struct AudioRange
{
    /// <summary>The distance in world units the voice falls silent at. 0, the default, means the voice is not positional.</summary>
    public float MaxDistance { get; }

    /// <summary>The curve the gain falls along from the listener to <see cref="MaxDistance"/>, <see cref="Ease.Linear"/> by default.</summary>
    /// <remarks>A curve that overshoots is clamped to a gain in [0, 1].</remarks>
    public Ease Falloff
    {
        get;
        init => field = Guard.RequireEase(value, nameof(value));
    }

    /// <summary>A range that silences its voice at <paramref name="maxDistance"/> world units from the listener.</summary>
    /// <param name="maxDistance">The distance the voice falls silent at, finite and non-negative. 0 makes it not positional.</param>
    public AudioRange(float maxDistance) => MaxDistance = Guard.NonNegative(maxDistance, nameof(maxDistance));
}
