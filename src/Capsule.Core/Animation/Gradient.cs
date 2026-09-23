using System.Runtime.CompilerServices;
using Capsule.Rendering;

namespace Capsule.Animation;

/// <summary>One stop on a <see cref="Gradient"/>: <see cref="Color"/> at normalised time <see cref="Time"/>.</summary>
/// <param name="Time">Normalised time, in [0, 1].</param>
/// <param name="Color">The colour at that time.</param>
public readonly record struct GradientStop(float Time, ColorRgba Color);

/// <summary>
/// A colour over a normalised time in [0, 1], lerped between adjacent stops through
/// <see cref="ColorRgba.Lerp"/>. It holds the first stop's colour before that stop and the last
/// stop's colour after it.
/// </summary>
/// <remarks>
/// A gradient stores up to eight stops inline.
/// <para>
/// Allocates nothing.
/// </para>
/// </remarks>
///
public readonly struct Gradient
{
    private readonly StopBuffer _stops;
    private readonly byte _count;

    private Gradient(ReadOnlySpan<GradientStop> stops)
    {
        _count = (byte)stops.Length;
        for (int index = 0; index < stops.Length; index++)
        {
            _stops[index] = stops[index];
        }
    }

    /// <summary>A gradient holding one constant colour.</summary>
    public static Gradient Constant(ColorRgba color) => FromKeys([new GradientStop(0f, color)]);

    /// <summary>A constant gradient, as <see cref="Constant"/>.</summary>
    public static implicit operator Gradient(ColorRgba color) => Constant(color);

    /// <summary>A gradient lerping from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static Gradient Linear(ColorRgba a, ColorRgba b) => FromKeys([new GradientStop(0f, a), new GradientStop(1f, b)]);

    /// <summary>A gradient through <paramref name="stops"/>, lerped between them.</summary>
    /// <param name="stops">One to eight stops, with non-decreasing <see cref="GradientStop.Time"/> each in [0, 1].</param>
    public static Gradient FromKeys(params ReadOnlySpan<GradientStop> stops)
    {
        if (stops.Length is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(stops), stops.Length, "A gradient holds one to eight stops.");
        }

        float previous = float.NegativeInfinity;
        for (int index = 0; index < stops.Length; index++)
        {
            float time = stops[index].Time;
            Guard.InUnit(time, nameof(stops));

            if (time < previous)
            {
                throw new ArgumentOutOfRangeException(nameof(stops), time, "Stop times must be non-decreasing.");
            }

            previous = time;
        }

        return new Gradient(stops);
    }

    /// <summary>
    /// The gradient's colour at <paramref name="t"/>. Progress is clamped to [0, 1] and NaN reads
    /// as 0.
    /// </summary>
    /// <remarks>
    /// A default gradient, with no stops, reads <see cref="ColorRgba.White"/> everywhere.
    /// </remarks>
    public ColorRgba Evaluate(float t)
    {
        // Not handled by the loop: the backing array's default stop is transparent black, not white.
        if (_count == 0)
        {
            return ColorRgba.White;
        }

        t = float.IsNaN(t) ? 0f : Math.Clamp(t, 0f, 1f);

        GradientStop from = _stops[0];
        GradientStop to = from;

        for (int index = 1; index < _count; index++)
        {
            to = _stops[index];
            if (t <= to.Time)
            {
                break;
            }

            from = to;
        }

        float span = to.Time - from.Time;
        float fraction = span > 0f ? MathF.Min(MathF.Max((t - from.Time) / span, 0f), 1f) : 0f;

        return ColorRgba.Lerp(from.Color, to.Color, fraction);
    }

    [InlineArray(8)]
    private struct StopBuffer
    {
        private GradientStop _element0;
    }
}
