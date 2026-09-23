using System.Runtime.CompilerServices;

namespace Capsule.Animation;

/// <summary>One point on a <see cref="Curve"/>: <see cref="Value"/> at normalised time <see cref="Time"/>.</summary>
/// <param name="Time">Normalised time, in [0, 1].</param>
/// <param name="Value">The value at that time.</param>
public readonly record struct CurveKey(float Time, float Value);

/// <summary>
/// A value over a normalised time in [0, 1], eased between adjacent keys by one <see cref="Ease"/>.
/// It holds the first key's value before that key and the last key's value after it.
/// </summary>
/// <remarks>
/// A curve stores up to eight keys inline.
/// <para>
/// Allocates nothing.
/// </para>
/// </remarks>
///
public readonly struct Curve
{
    private readonly KeyBuffer _keys;
    private readonly byte _count;
    private readonly Ease _ease;
    private readonly float _max;

    private Curve(ReadOnlySpan<CurveKey> keys, Ease ease)
    {
        _count = (byte)keys.Length;
        _ease = ease;

        float max = keys[0].Value;
        for (int index = 0; index < keys.Length; index++)
        {
            _keys[index] = keys[index];
            if (keys[index].Value > max)
            {
                max = keys[index].Value;
            }
        }

        _max = max;
    }

    /// <summary>A curve holding one constant value.</summary>
    public static Curve Constant(float value) => FromKeys([new CurveKey(0f, value)]);

    // The largest value any key holds. A particle emitter's bounds inflate by this.
    internal float Max => _max;

    /// <summary>A constant curve, as <see cref="Constant"/>.</summary>
    public static implicit operator Curve(float value) => Constant(value);

    /// <summary>A curve rising or falling in a straight line from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static Curve Linear(float from, float to) => FromKeys([new CurveKey(0f, from), new CurveKey(1f, to)]);

    /// <summary>A curve from <paramref name="from"/> to <paramref name="to"/> bent by <paramref name="ease"/>.</summary>
    public static Curve Eased(float from, float to, Ease ease) => FromKeys([new CurveKey(0f, from), new CurveKey(1f, to)], ease);

    /// <summary>A curve through <paramref name="keys"/>, linear between them.</summary>
    /// <param name="keys">One to eight keys, with non-decreasing <see cref="CurveKey.Time"/> each in [0, 1].</param>
    public static Curve FromKeys(params ReadOnlySpan<CurveKey> keys) => FromKeys(keys, Ease.Linear);

    /// <summary>A curve through <paramref name="keys"/>, bent by <paramref name="ease"/> between them.</summary>
    /// <param name="keys">One to eight keys, with non-decreasing <see cref="CurveKey.Time"/> each in [0, 1].</param>
    /// <param name="ease">The bend applied to the fraction between two adjacent keys.</param>
    public static Curve FromKeys(ReadOnlySpan<CurveKey> keys, Ease ease)
    {
        if (keys.Length is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(keys), keys.Length, "A curve holds one to eight keys.");
        }

        float previous = float.NegativeInfinity;
        for (int index = 0; index < keys.Length; index++)
        {
            float time = keys[index].Time;
            Guard.InUnit(time, nameof(keys));

            if (time < previous)
            {
                throw new ArgumentOutOfRangeException(nameof(keys), time, "Key times must be non-decreasing.");
            }

            previous = time;
        }

        return new Curve(keys, ease);
    }

    /// <summary>
    /// The curve's value at <paramref name="t"/>. Progress is clamped to [0, 1] and NaN reads as 0.
    /// </summary>
    /// <remarks>A default curve, with no keys, reads 0 everywhere.</remarks>
    public float Evaluate(float t)
    {
        t = float.IsNaN(t) ? 0f : Math.Clamp(t, 0f, 1f);

        CurveKey from = _keys[0];
        CurveKey to = from;

        for (int index = 1; index < _count; index++)
        {
            to = _keys[index];
            if (t <= to.Time)
            {
                break;
            }

            from = to;
        }

        float span = to.Time - from.Time;
        float fraction = span > 0f ? MathF.Min(MathF.Max((t - from.Time) / span, 0f), 1f) : 0f;

        return from.Value + ((to.Value - from.Value) * Easing.Apply(_ease, fraction));
    }

    [InlineArray(8)]
    private struct KeyBuffer
    {
        private CurveKey _element0;
    }
}
