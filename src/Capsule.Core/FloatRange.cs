namespace Capsule;

/// <summary>
/// A span of floats a lever draws from, as <c>[Min, Max]</c>. A constant is written as a bare number
/// through the implicit conversion.
/// </summary>
/// <param name="Min">The low end, inclusive.</param>
/// <param name="Max">The high end, inclusive, at or above <paramref name="Min"/>.</param>
public readonly record struct FloatRange(float Min, float Max)
{
    /// <summary>The low end, inclusive.</summary>
    public float Min { get; } = Min;

    /// <summary>The high end, inclusive, at or above <see cref="Min"/>.</summary>
    public float Max { get; } = Validated(Min, Max);

    /// <summary>A range holding one constant value.</summary>
    public static implicit operator FloatRange(float value) => new(value, value);

    /// <summary>A range from a tuple, as <c>(0.2f, 0.4f)</c>.</summary>
    public static implicit operator FloatRange((float Min, float Max) range) => new(range.Min, range.Max);

    private static float Validated(float min, float max)
    {
        Guard.Finite(min, nameof(min));
        Guard.Finite(max, nameof(max));
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);

        return max;
    }
}
