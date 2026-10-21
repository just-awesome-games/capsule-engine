using System.Numerics;

namespace Capsule.Scenes;

/// <summary>
/// Everything one <see cref="Camera.Shake(in CameraShake)"/> takes: how far the view moves on each axis
/// and for how long. Build it with the constructor, which sets the property defaults below.
/// </summary>
/// <remarks>
/// <c>default(CameraShake)</c> skips them, carries no duration, and no camera accepts it.
/// </remarks>
/// <example>
/// A short vertical jolt that alternates every step at 60 steps a second:
/// <code>Scene.Camera.Shake(new CameraShake(new Vector2(0f, 3f), 0.1f) { Wave = ShakeWave.Sine, Frequency = 30f });</code>
/// </example>
/// <param name="Amplitude">The farthest the view moves on each axis at full strength, in world units.</param>
/// <param name="Seconds">How long the shake lasts, greater than zero and finite.</param>
public readonly record struct CameraShake(Vector2 Amplitude, float Seconds)
{
    /// <summary>How fast the shake moves, in hertz, defaulting to 15.</summary>
    /// <remarks>A frequency above half the step rate aliases.</remarks>
    public float Frequency { get; init; } = 15f;

    /// <summary>The motion the shake moves the view with, defaulting to <see cref="ShakeWave.Noise"/>.</summary>
    public ShakeWave Wave { get; init; }

    /// <summary>
    /// The share of <see cref="Seconds"/>, in [0, 1], held at full strength before the shake fades out,
    /// where 0 fades from the first step and 1 holds to the end and stops.
    /// </summary>
    public float Sustain { get; init; }
}
