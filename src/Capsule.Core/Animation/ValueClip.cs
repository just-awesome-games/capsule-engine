using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Animation;

/// <summary>
/// One animation of values: an ordered run of keys, each a value held for a whole number of fixed
/// steps, played once or on a loop.
/// </summary>
/// <remarks>
/// A clip is immutable and identified by instance. Every animator playing it shares the one instance
/// and keeps its own <see cref="AnimationPlayback"/> cursor. A clip built by its constructor steps
/// from key to key. A clip built by <see cref="ValueClip"/>'s <c>Linear</c> blends from each key's value
/// toward the next across that key's ticks.
/// </remarks>
/// <example>
/// <code>
/// ValueClip&lt;float&gt; pulse = new([(1f, 8), (2f, 8), (3f, 7)]);
/// </code>
/// </example>
/// <typeparam name="T">The type of value each key holds.</typeparam>
public sealed class ValueClip<T>
{
    private readonly T[] _values;
    private readonly int[] _keyTicks;

    // The value a fraction of the way from one key's value to the next. Null steps instead. Every
    // blend is static, and evaluating a clip allocates nothing.
    private readonly Func<T, T, float, T>? _blend;

    // Whether two blended values are equal, set exactly when _blend is. Every blending type compares
    // without boxing.
    private readonly Func<T, T, bool>? _equals;

    // Each key's value class: keys with equal values share one id, assigned once here. A stepping
    // animator compares ids, never values, and so never boxes a T at run time.
    private readonly int[] _valueIds;

    /// <summary>Whether the clip wraps from its last key back to its first.</summary>
    public bool Loop { get; }

    /// <summary>The fixed steps one pass of the clip takes: the sum of <see cref="KeyTicks"/>.</summary>
    public int Duration { get; }

    /// <summary>Creates a clip stepping through copies of <paramref name="keys"/>, each value shown whole for its ticks.</summary>
    /// <param name="keys">The keys in play order, each a value and the fixed steps it is held for. At least one, and every hold positive.</param>
    /// <param name="loop">Whether the last key wraps back to the first instead of finishing.</param>
    public ValueClip(ReadOnlySpan<(T Value, int Ticks)> keys, bool loop = false)
        : this(keys, loop, blend: null, equals: null)
    {
    }

    internal ValueClip(ReadOnlySpan<(T Value, int Ticks)> keys, bool loop, Func<T, T, float, T>? blend, Func<T, T, bool>? equals)
    {
        if (keys.IsEmpty)
        {
            throw new ArgumentException("Expected at least one key. Pass a value and the ticks it is held for.", nameof(keys));
        }

        _values = new T[keys.Length];
        _keyTicks = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            (_values[i], _keyTicks[i]) = keys[i];
        }

        long total = 0;
        for (int i = 0; i < _keyTicks.Length; i++)
        {
            if (_keyTicks[i] <= 0)
            {
                throw new ArgumentException(
                    $"Key {i} is held for {_keyTicks[i]} ticks. Hold every key for at least one fixed step.",
                    nameof(keys));
            }

            total += _keyTicks[i];
        }

        if (total > int.MaxValue)
        {
            throw new ArgumentException(
                $"The keys are held for {total} ticks in all, past the {int.MaxValue} a clip's Duration holds. Shorten the holds or split the clip.",
                nameof(keys));
        }

        Duration = (int)total;
        Loop = loop;
        _blend = blend;
        _equals = equals;

        _valueIds = new int[_values.Length];
        Dictionary<Keyed, int> ids = [];
        for (int i = 0; i < _values.Length; i++)
        {
            if (!ids.TryGetValue(new Keyed(_values[i]), out _valueIds[i]))
            {
                _valueIds[i] = ids.Count;
                ids.Add(new Keyed(_values[i]), ids.Count);
            }
        }
    }

    // Whether the clip blends between keys rather than stepping from one to the next.
    internal bool IsLinear => _blend is not null;

    // The id two keys share exactly when their values are equal.
    internal int ValueId(int keyIndex) => _valueIds[keyIndex];

    // Whether two values of a blending clip are equal.
    internal bool Same(T a, T b) => _equals!(a, b);

    /// <summary>Each key's value, in play order.</summary>
    public ReadOnlySpan<T> Values => _values;

    /// <summary>How many fixed steps each key is held for, in play order.</summary>
    public ReadOnlySpan<int> KeyTicks => _keyTicks;

    // The value ticksElapsed steps into key keyIndex. A blending clip moves toward the next key, and a
    // looping one's last key moves back toward the first. A non-looping clip's last key holds.
    internal T ValueAt(int keyIndex, int ticksElapsed)
    {
        T from = _values[keyIndex];
        if (_blend is not { } blend)
        {
            return from;
        }

        int next = keyIndex + 1;
        if (next == _values.Length)
        {
            if (!Loop)
            {
                return from;
            }

            next = 0;
        }

        return ticksElapsed == 0 ? from : blend(from, _values[next], ticksElapsed / (float)_keyTicks[keyIndex]);
    }

    // A key's value as a dictionary key. Its equality is the type's default, and a null value is one key.
    private readonly record struct Keyed(T Value);
}

/// <summary>Creates the <see cref="ValueClip{T}"/> kinds that only some value types support.</summary>
/// <remarks>
/// A blend weighs both keys' values, as <c>from * (1 - t) + to * t</c>, and never overflows between finite
/// keys. The arithmetic is IEEE-exact on every platform. Two equal keys give their value exactly.
/// A linear clip moves from each key's value toward the next across that key's ticks. It shows a key's
/// own value on that key's first tick. A non-looping clip's last key holds its value, and a looping
/// clip's last key moves back toward the first key's.
/// </remarks>
public static class ValueClip
{
    /// <summary>Creates a clip of numbers blending linearly between copies of <paramref name="keys"/>.</summary>
    /// <param name="keys">The keys in play order, each a finite value and the fixed steps it moves over. At least one, and every hold positive.</param>
    /// <param name="loop">Whether the last key blends back to the first instead of holding.</param>
    public static ValueClip<float> Linear(ReadOnlySpan<(float Value, int Ticks)> keys, bool loop = false)
    {
        foreach ((float value, _) in keys)
        {
            Guard.Finite(value, nameof(keys));
        }

        return new ValueClip<float>(keys, loop, static (from, to, t) => from == to ? from : (from * (1f - t)) + (to * t), static (a, b) => a == b);
    }

    /// <summary>Creates a clip of vectors blending linearly between copies of <paramref name="keys"/>.</summary>
    /// <param name="keys">The keys in play order, each a finite value and the fixed steps it moves over. At least one, and every hold positive.</param>
    /// <param name="loop">Whether the last key blends back to the first instead of holding.</param>
    public static ValueClip<Vector2> Linear(ReadOnlySpan<(Vector2 Value, int Ticks)> keys, bool loop = false)
    {
        foreach ((Vector2 value, _) in keys)
        {
            Guard.Finite(value, nameof(keys));
        }

        return new ValueClip<Vector2>(keys, loop, static (from, to, t) => from == to ? from : (from * (1f - t)) + (to * t), static (a, b) => a == b);
    }

    /// <summary>Creates a clip of four-component vectors blending linearly between copies of <paramref name="keys"/>.</summary>
    /// <param name="keys">The keys in play order, each a finite value and the fixed steps it moves over. At least one, and every hold positive.</param>
    /// <param name="loop">Whether the last key blends back to the first instead of holding.</param>
    public static ValueClip<Vector4> Linear(ReadOnlySpan<(Vector4 Value, int Ticks)> keys, bool loop = false)
    {
        foreach ((Vector4 value, _) in keys)
        {
            Guard.Finite(value, nameof(keys));
        }

        return new ValueClip<Vector4>(keys, loop, static (from, to, t) => from == to ? from : (from * (1f - t)) + (to * t), static (a, b) => a == b);
    }

    /// <summary>Creates a clip of colours blending between copies of <paramref name="keys"/> through <see cref="ColorRgba.Lerp"/>.</summary>
    /// <param name="keys">The keys in play order, each a colour and the fixed steps it moves over. At least one, and every hold positive.</param>
    /// <param name="loop">Whether the last key blends back to the first instead of holding.</param>
    public static ValueClip<ColorRgba> Linear(ReadOnlySpan<(ColorRgba Value, int Ticks)> keys, bool loop = false) =>
        new(keys, loop, ColorRgba.Lerp, static (a, b) => a == b);
}
