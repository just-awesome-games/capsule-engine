using System.Numerics;
using Capsule.Diagnostics;

namespace Capsule.Scenes;

// The shake: a decaying offset added to the drawn view on top of the game's own offset.
public partial class Camera
{
    // The relative slack a shake held to its end allows its length. It absorbs only the float spelling
    // of a step and of the length, such as 1/60f against 8/60f.
    private const double HeldEndTolerance = 1e-6;

    // The shake on screen: its peak, and the envelope that falls from 1 to 0 across its seconds. The
    // strength holds at 1 while the envelope is at or above _hold, then falls as the square of the
    // envelope's share of _hold. A shorthand shake holds from 1. Its strength is the envelope squared.
    private float _intensity;
    private float _envelope;
    private float _hold = 1f;
    private float _shakeSeconds = 1f;

    // The seconds a shake held to its end has drawn.
    private double _heldSeconds;

    // A profile shake carries its own amplitude, frequency and phase. A shorthand shake reads the
    // camera-wide amplitude and noise phase live.
    private bool _isProfile;
    private Vector2 _profileAmplitude;
    private float _profileFrequency;
    private ShakeWave _wave;
    private long _profileCell;
    private float _profileFraction;

    // The camera-wide noise phase, as a whole lattice cell plus a fraction in [0, 1).
    private long _shakeCell;
    private float _shakeFraction;

    /// <summary>
    /// How far a shake of intensity 1 reaches, in world units on each axis, defaulting to (8, 8).
    /// </summary>
    public Vector2 ShakeAmplitude
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    } = new(8f, 8f);

    /// <summary>How fast the shake moves, defaulting to 15 hertz.</summary>
    /// <remarks>A frequency above half the step rate aliases.</remarks>
    public float ShakeFrequency
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    } = 15f;

    /// <summary>
    /// How long a <see cref="Shake(float)"/> lasts whatever its intensity, defaulting to 0.5 seconds.
    /// </summary>
    public float ShakeDuration
    {
        get;
        set => field = Guard.Positive(value, nameof(value));
    } = 0.5f;

    // The offset the shake adds to the drawn view this step.
    private Vector2 ShakeOffset { get; set; }

    // 1 while the shake holds, then the envelope's share of _hold. The strength is its square.
    private float Fall => _envelope >= _hold ? 1f : _envelope / _hold;

    /// <summary>
    /// Shakes the view for <see cref="ShakeDuration"/> at <see cref="ShakeAmplitude"/> times
    /// <paramref name="intensity"/>, as <see cref="Shake(float, float)"/> does.
    /// </summary>
    public void Shake(float intensity) => Shake(intensity, ShakeDuration);

    /// <summary>
    /// Shakes the view for <paramref name="seconds"/> at <see cref="ShakeAmplitude"/> times
    /// <paramref name="intensity"/>, capped at 1. A call weaker than the shake on screen changes
    /// nothing.
    /// </summary>
    /// <remarks>
    /// The shake moves as <see cref="ShakeWave.Noise"/> at <see cref="ShakeFrequency"/> and fades from
    /// its first step. It draws nothing from <see cref="Run.Random"/>.
    /// </remarks>
    public void Shake(float intensity, float seconds)
    {
        Guard.NonNegative(intensity, nameof(intensity));
        Guard.Positive(seconds, nameof(seconds));
        intensity = MathF.Min(1f, intensity);

        float fall = Fall;
        bool stronger = _isProfile
            ? ShakeAmplitude.Length() * intensity >= _profileAmplitude.Length() * fall * fall
            : intensity >= _intensity * fall * fall;
        if (stronger)
        {
            Begin(intensity, seconds, 1f);
            _isProfile = false;
            _wave = ShakeWave.Noise;
        }
    }

    /// <summary>
    /// Shakes the view with <paramref name="shake"/>. A call whose amplitude is shorter than the shake
    /// on screen reaches now changes nothing.
    /// </summary>
    /// <remarks>The shake draws nothing from <see cref="Run.Random"/>.</remarks>
    public void Shake(in CameraShake shake)
    {
        Guard.NonNegative(shake.Amplitude, nameof(shake));
        Guard.Positive(shake.Seconds, nameof(shake));
        Guard.NonNegative(shake.Frequency, nameof(shake));
        Guard.InUnit(shake.Sustain, nameof(shake));
        if (shake.Wave is not (ShakeWave.Noise or ShakeWave.Sine))
        {
            throw new ArgumentOutOfRangeException(nameof(shake), shake.Wave, "Expected a ShakeWave member. Use Noise or Sine.");
        }

        float fall = Fall;
        float peak = _isProfile ? _profileAmplitude.Length() : ShakeAmplitude.Length() * _intensity;
        if (shake.Amplitude.Length() >= peak * fall * fall)
        {
            Begin(1f, shake.Seconds, 1f - shake.Sustain);
            _isProfile = true;
            _profileAmplitude = shake.Amplitude;
            _profileFrequency = shake.Frequency;
            _wave = shake.Wave;

            // A noise profile picks up the camera-wide phase. Its noise depends only on the step it
            // starts. A sine starts at its peak.
            (_profileCell, _profileFraction) = shake.Wave == ShakeWave.Noise ? (_shakeCell, _shakeFraction) : (0L, 0f);
        }
    }

    private static void Advance(ref long cell, ref float fraction, float cycles)
    {
        fraction += cycles;
        float whole = MathF.Floor(fraction);
        cell += (long)whole;
        fraction -= whole;
    }

    private void Begin(float intensity, float seconds, float hold)
    {
        _intensity = intensity;
        _envelope = 1f;
        _hold = hold;
        _shakeSeconds = seconds;
        _heldSeconds = 0d;
    }

    // The camera-wide phase advances ShakeFrequency cells a second and runs between shakes too. The
    // offset is taken before the envelope falls, and a spent shake gives exactly zero without sampling.
    private void StepShake(float seconds)
    {
        Advance(ref _shakeCell, ref _shakeFraction, ShakeFrequency * seconds);

        if (_envelope == 0f)
        {
            ShakeOffset = Vector2.Zero;
            return;
        }

        long cell = _isProfile ? _profileCell : _shakeCell;
        float fraction = _isProfile ? _profileFraction : _shakeFraction;
        Vector2 wave = _wave == ShakeWave.Sine
            ? new Vector2(DeterministicMath.Cos(MathF.Tau * fraction))
            : new Vector2(ValueNoise.Sample(0, cell, fraction), ValueNoise.Sample(1, cell, fraction));
        Vector2 amplitude = _isProfile ? _profileAmplitude : ShakeAmplitude;
        float fall = Fall;
        ShakeOffset = amplitude * (_intensity * fall * fall) * wave;

        if (_isProfile)
        {
            Advance(ref _profileCell, ref _profileFraction, _profileFrequency * seconds);
        }

        // A shake held to its end stops by its elapsed time. The subtracted envelope's error grows with
        // the square of its length in steps and would end it early or late. A shake with a fade ends on
        // the envelope, whose remainder draws nothing visible.
        if (_hold == 0f)
        {
            _heldSeconds += seconds;
            _envelope = _heldSeconds >= _shakeSeconds * (1d - HeldEndTolerance) ? 0f : 1f;
        }
        else
        {
            _envelope = MathF.Max(0f, _envelope - (seconds / _shakeSeconds));
        }

        if (_envelope == 0f)
        {
            _intensity = 0f;
            _hold = 1f;
            _isProfile = false;
        }
    }
}
