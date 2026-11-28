using System.Numerics;
using Capsule.Diagnostics;

namespace Capsule.Scenes;

// The shake: a decaying offset added to the drawn view on top of the game's own offset.
public partial class Camera
{
    // The relative slack a shake held to its end allows its length. It absorbs only the float spelling
    // of a step and of the length, such as 1/60f against 8/60f.
    private const double HeldEndTolerance = 1e-6;

    // The amplitude and length of a shorthand shake at intensity 1.
    private const float ShorthandReach = 8f;
    private const float ShorthandSeconds = 0.5f;

    // The cycles a second of the camera-wide noise phase.
    private const float PhaseFrequency = 15f;

    // The shake on screen: its peak, and the envelope that falls from 1 to 0 across its seconds. The
    // strength holds at 1 while the envelope is at or above _hold, then falls as the square of the
    // envelope's share of _hold.
    private Vector2 _amplitude;
    private float _envelope;
    private float _hold = 1f;
    private float _shakeSeconds = 1f;
    private float _frequency;
    private ShakeWave _wave;
    private long _cell;
    private float _fraction;

    // The seconds a shake held to its end has drawn.
    private double _heldSeconds;

    // The camera-wide noise phase, as a whole lattice cell plus a fraction in [0, 1). It runs between
    // shakes so a noise shake's noise depends only on the step it starts.
    private long _phaseCell;
    private float _phaseFraction;

    /// <summary>
    /// Whether the shake moves <see cref="VisibleRegion"/> as it moves the drawn view, true by default.
    /// </summary>
    /// <remarks>
    /// False shakes only the picture. The region, the visibility notifiers and everything else that reads
    /// it then never depend on a shake or on <see cref="Capsule.Rendering.Display.ShakeStrength"/>. A
    /// player's shake setting then cannot change how a run plays.
    /// </remarks>
    public bool ShakeMovesVisibleRegion { get; set; } = true;

    // The offset the shake adds to the drawn view this step.
    private Vector2 ShakeOffset { get; set; }

    // 1 while the shake holds, then the envelope's share of _hold. The strength is its square.
    private float Fall => _envelope >= _hold ? 1f : _envelope / _hold;

    /// <summary>
    /// Shakes the view for half a second, as <see cref="Shake(float, float)"/> does.
    /// </summary>
    public void Shake(float intensity) => Shake(intensity, ShorthandSeconds);

    /// <summary>
    /// Shakes the view for <paramref name="seconds"/> with a default <see cref="CameraShake"/> reaching 8
    /// world units on each axis times <paramref name="intensity"/>, capped at 1, as
    /// <see cref="Shake(in CameraShake)"/> does.
    /// </summary>
    public void Shake(float intensity, float seconds)
    {
        Guard.Positive(seconds, nameof(seconds));
        Shake(new CameraShake(new Vector2(ShorthandReach * MathF.Min(1f, Guard.NonNegative(intensity, nameof(intensity)))), seconds));
    }

    /// <summary>
    /// Shakes the view with <paramref name="shake"/>. A call whose amplitude is shorter than the shake
    /// on screen reaches now changes nothing.
    /// </summary>
    /// <remarks>
    /// The shake draws nothing from <see cref="Run.Random"/>, and
    /// <see cref="Capsule.Rendering.Display.ShakeStrength"/> scales the offset it draws.
    /// </remarks>
    public void Shake(in CameraShake shake)
    {
        Guard.Finite(shake.Amplitude, nameof(shake));
        Guard.Positive(shake.Seconds, nameof(shake));
        Guard.NonNegative(shake.Frequency, nameof(shake));
        Guard.InUnit(shake.Sustain, nameof(shake));
        if ((uint)shake.Wave > (uint)ShakeWave.Pulse)
        {
            throw new ArgumentOutOfRangeException(nameof(shake), shake.Wave, "Expected a declared ShakeWave member.");
        }

        float fall = Fall;
        if (shake.Amplitude.Length() >= _amplitude.Length() * fall * fall)
        {
            _amplitude = shake.Amplitude;
            _envelope = 1f;
            _hold = 1f - shake.Sustain;
            _shakeSeconds = shake.Seconds;
            _heldSeconds = 0d;
            _frequency = shake.Frequency;
            _wave = shake.Wave;

            // A noise shake picks up the camera-wide phase. A sine starts at its peak.
            (_cell, _fraction) = shake.Wave == ShakeWave.Noise ? (_phaseCell, _phaseFraction) : (0L, 0f);
        }
    }

    private static void Advance(ref long cell, ref float fraction, float cycles)
    {
        fraction += cycles;
        float whole = MathF.Floor(fraction);
        cell += (long)whole;
        fraction -= whole;
    }

    // The offset is taken before the envelope falls, and a spent shake gives exactly zero without
    // sampling. The run's shake strength scales only the offset drawn. The shake's own state runs on
    // under it.
    private void StepShake(float seconds)
    {
        Advance(ref _phaseCell, ref _phaseFraction, PhaseFrequency * seconds);

        if (_envelope == 0f)
        {
            ShakeOffset = Vector2.Zero;
            return;
        }

        Vector2 wave = _wave switch
        {
            ShakeWave.Sine => new Vector2(DeterministicMath.Cos(MathF.Tau * _fraction)),
            ShakeWave.Pulse => new Vector2(0.5f + (0.5f * DeterministicMath.Cos(MathF.Tau * _fraction))),
            _ => new Vector2(ValueNoise.Sample(0, _cell, _fraction), ValueNoise.Sample(1, _cell, _fraction)),
        };
        float fall = Fall;
        float strength = _scene?.RunOrNull?.Display.ShakeStrength ?? 1f;
        ShakeOffset = _amplitude * (fall * fall * strength) * wave;
        Advance(ref _cell, ref _fraction, _frequency * seconds);

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
            _hold = 1f;
        }
    }
}
