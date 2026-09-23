using System.Numerics;
using Capsule.Animation;

namespace Capsule;

// The argument checks the engine repeats: a finite number, a positive one, and one inside a range. NaN
// and the infinities pass a naive comparison, so each check is written as an accept test.
internal static class Guard
{
    internal static void Finite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Expected a finite number.");
        }
    }

    internal static void Finite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Expected finite components.");
        }
    }

    internal static void NonNegative(Vector2 value, string parameterName)
    {
        if (!(value.X >= 0f) || !(value.Y >= 0f) || float.IsInfinity(value.X) || float.IsInfinity(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Expected finite, non-negative components.");
        }
    }

    internal static void Positive(float value, string parameterName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Expected a positive, finite number.");
        }
    }

    internal static void InUnit(float value, string parameterName) => InRange(value, 0f, 1f, parameterName);

    internal static void InRange(float value, float low, float high, string parameterName)
    {
        if (!(value >= low && value <= high))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"Expected a number in [{low}, {high}].");
        }
    }

    internal static void RequireSeconds(float seconds, string parameterName)
    {
        if (!(seconds >= 0f) || float.IsInfinity(seconds))
        {
            throw new ArgumentOutOfRangeException(parameterName, seconds, "Expected a finite, non-negative number.");
        }
    }

    internal static void RequireEase(Ease ease, string parameterName)
    {
        if (ease is < Ease.Linear or > Ease.InOutBounce)
        {
            throw new ArgumentOutOfRangeException(parameterName, ease, "No such easing curve.");
        }
    }
}
