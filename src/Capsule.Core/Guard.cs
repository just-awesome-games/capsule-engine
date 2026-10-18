using System.Numerics;
using Capsule.Animation;

namespace Capsule;

// The argument checks the engine repeats: a finite number, a positive one, and one inside a range. Each
// check is written as an accept test, because NaN and the infinities pass a naive comparison.
internal static class Guard
{
    internal static float Finite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Expected a finite number.");
        }

        return value;
    }

    internal static Vector2 Finite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Expected finite components. A NaN usually comes from normalizing a zero vector. Check its length first.");
        }

        return value;
    }

    internal static Vector4 Finite(Vector4 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Expected finite components.");
        }

        return value;
    }

    internal static Vector2 NonNegative(Vector2 value, string parameterName)
    {
        if (!(value.X >= 0f) || !(value.Y >= 0f) || float.IsInfinity(value.X) || float.IsInfinity(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Expected finite, non-negative components.");
        }

        return value;
    }

    internal static float NonNegative(float value, string parameterName)
    {
        if (!(value >= 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Expected a finite, non-negative number.");
        }

        return value;
    }

    internal static float Positive(float value, string parameterName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Expected a positive, finite number.");
        }

        return value;
    }

    internal static float InUnit(float value, string parameterName) => InRange(value, 0f, 1f, parameterName);

    internal static float InRange(float value, float low, float high, string parameterName)
    {
        if (!(value >= low && value <= high))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"Expected a number in [{low}, {high}].");
        }

        return value;
    }

    internal static Ease RequireEase(Ease ease, string parameterName)
    {
        if (ease is < Ease.Linear or > Ease.InOutBounce)
        {
            throw new ArgumentOutOfRangeException(parameterName, ease, "No such easing curve.");
        }

        return ease;
    }
}
