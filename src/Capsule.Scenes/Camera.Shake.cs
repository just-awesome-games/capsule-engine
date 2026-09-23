using System.Numerics;
using Capsule.Diagnostics;

namespace Capsule.Scenes;

// The shake: a decaying noise offset added to the drawn view on top of the game's own offset.
public partial class Camera
{
    // The shake on screen: its peak, and the envelope that falls from 1 to 0 across its seconds. The
    // offset scales by the envelope squared.
    private float _intensity;
    private float _envelope;
    private float _shakeSeconds = 1f;

    // The shake's noise phase, as a whole lattice cell plus a fraction in [0, 1).
    private long _shakeCell;
    private float _shakeFraction;

    /// <summary>
    /// How far a shake of intensity 1 reaches, in world units on each axis, defaulting to (8, 8).
    /// </summary>
    public Vector2 ShakeAmplitude
    {
        get;

        set
        {
            Guard.NonNegative(value, nameof(value));
            field = value;
        }
    } = new(8f, 8f);

    /// <summary>How fast the shake moves, defaulting to 15 hertz.</summary>
    /// <remarks>A frequency above half the step rate aliases.</remarks>
    public float ShakeFrequency
    {
        get;

        set
        {
            Guard.RequireSeconds(value, nameof(value));
            field = value;
        }
    } = 15f;

    /// <summary>
    /// How long a <see cref="Shake(float)"/> lasts whatever its intensity, defaulting to 0.5 seconds.
    /// </summary>
    public float ShakeDuration
    {
        get;

        set
        {
            Guard.Positive(value, nameof(value));
            field = value;
        }
    } = 0.5f;

    // The offset the shake adds to the drawn view this step.
    private Vector2 ShakeOffset { get; set; }

    /// <summary>
    /// Shakes the view for <see cref="ShakeDuration"/> at <see cref="ShakeAmplitude"/> times
    /// <paramref name="intensity"/>, as <see cref="Shake(float, float)"/> does.
    /// </summary>
    public void Shake(float intensity) => Shake(intensity, ShakeDuration);

    /// <summary>
    /// Shakes the view for <paramref name="seconds"/> at <see cref="ShakeAmplitude"/> times
    /// <paramref name="intensity"/>, which holds at 1. A call weaker than the shake on screen changes
    /// nothing.
    /// </summary>
    /// <remarks>The shake draws nothing from <see cref="Run.Random"/>.</remarks>
    public void Shake(float intensity, float seconds)
    {
        Guard.RequireSeconds(intensity, nameof(intensity));
        Guard.Positive(seconds, nameof(seconds));
        intensity = MathF.Min(1f, intensity);

        if (intensity >= _intensity * _envelope * _envelope)
        {
            _intensity = intensity;
            _envelope = 1f;
            _shakeSeconds = seconds;
        }
    }

    // Two independent noise channels at a phase that advances ShakeFrequency cells a second. The phase
    // runs between shakes too, and a shake's noise depends only on the step it starts. The offset is
    // taken before the envelope falls, and a spent shake gives exactly zero without sampling.
    private void StepShake(float seconds)
    {
        _shakeFraction += ShakeFrequency * seconds;
        float whole = MathF.Floor(_shakeFraction);
        _shakeCell += (long)whole;
        _shakeFraction -= whole;

        if (_envelope == 0f)
        {
            ShakeOffset = Vector2.Zero;
            return;
        }

        Vector2 noise = new(
            ValueNoise.Sample(0, _shakeCell, _shakeFraction),
            ValueNoise.Sample(1, _shakeCell, _shakeFraction));
        ShakeOffset = ShakeAmplitude * (_intensity * _envelope * _envelope) * noise;
        _envelope = MathF.Max(0f, _envelope - (seconds / _shakeSeconds));
        if (_envelope == 0f)
        {
            _intensity = 0f;
        }
    }
}
