using System.Runtime.CompilerServices;

namespace Capsule.Input;

/// <summary>An allocation-free snapshot of held keys, pad buttons, and axis positions.</summary>
public readonly struct DeviceSnapshot : IEquatable<DeviceSnapshot>
{
    /// <summary>Keys whose <see cref="Key"/> value must remain below this to be representable.</summary>
    public const int Capacity = 128;

    /// <summary>Buttons whose <see cref="PadButton"/> value must remain below this to be representable.</summary>
    public const int PadCapacity = 32;

    private const int AxisCount = 6;

    private readonly UInt128 _down;
    private readonly uint _padDown;
    private readonly AxisSet _axes;

    private DeviceSnapshot(UInt128 down, uint padDown, AxisSet axes)
    {
        _down = down;
        _padDown = padDown;
        _axes = axes;
    }

    /// <summary>A snapshot with nothing held and every axis at rest; equal to <c>default</c>.</summary>
    public static DeviceSnapshot Empty => default;

    /// <exception cref="ArgumentOutOfRangeException">Some key is not representable.</exception>
    public static DeviceSnapshot Of(params ReadOnlySpan<Key> keys)
    {
        UInt128 down = UInt128.Zero;
        for (int i = 0; i < keys.Length; i++)
        {
            down |= Bit(keys[i]);
        }

        return new DeviceSnapshot(down, 0, default);
    }

    /// <summary>Whether nothing is held and every axis is at rest.</summary>
    public bool IsEmpty => Equals(Empty);

    /// <summary>Whether <paramref name="key"/> is held down at this instant.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The key is not representable.</exception>
    public bool IsDown(Key key) => (_down & Bit(key)) != UInt128.Zero;

    /// <summary>Whether <paramref name="button"/> is held down at this instant.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The button is not representable.</exception>
    public bool IsDown(PadButton button) => (_padDown & PadBit(button)) != 0;

    /// <summary>Position of <paramref name="axis"/>, past deadzone filtering; 0 at rest.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The axis is <see cref="PadAxis.None"/> or not representable.</exception>
    public float Axis(PadAxis axis) => _axes[AxisIndex(axis)];

    /// <summary>This snapshot with <paramref name="key"/> additionally held.</summary>
    public DeviceSnapshot With(Key key) => new(_down | Bit(key), _padDown, _axes);

    /// <summary>This snapshot with <paramref name="button"/> additionally held.</summary>
    public DeviceSnapshot With(PadButton button) => new(_down, _padDown | PadBit(button), _axes);

    /// <summary>This snapshot with <paramref name="key"/> released.</summary>
    public DeviceSnapshot Without(Key key) => new(_down & ~Bit(key), _padDown, _axes);

    /// <summary>This snapshot with <paramref name="button"/> released.</summary>
    public DeviceSnapshot Without(PadButton button) => new(_down, _padDown & ~PadBit(button), _axes);

    /// <summary>This snapshot with <paramref name="axis"/> at <paramref name="value"/>.</summary>
    /// <param name="axis">The axis to place; never <see cref="PadAxis.None"/>.</param>
    /// <param name="value">In [-1, 1] for a stick, [0, 1] for a trigger.</param>
    /// <exception cref="ArgumentOutOfRangeException">The axis names none, or the value is outside its range.</exception>
    public DeviceSnapshot WithAxis(PadAxis axis, float value)
    {
        int index = AxisIndex(axis);
        float minimum = axis is PadAxis.LeftTrigger or PadAxis.RightTrigger ? 0f : -1f;

        // Negated so that NaN, which compares false either way, is rejected with the rest.
        if (!(value >= minimum && value <= 1f))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(PadAxis)}.{axis} is in [{minimum}, 1].");
        }

        AxisSet axes = _axes;
        axes[index] = value;

        return new DeviceSnapshot(_down, _padDown, axes);
    }

    /// <summary>Unions held buttons with a newer sample and takes its axis values.</summary>
    public DeviceSnapshot LatchedWith(in DeviceSnapshot newer) =>
        new(_down | newer._down, _padDown | newer._padDown, newer._axes);

    /// <summary>Whether the same keys and buttons are held and every axis reads the same.</summary>
    public bool Equals(DeviceSnapshot other)
    {
        if (_down != other._down || _padDown != other._padDown)
        {
            return false;
        }

        for (int i = 0; i < AxisCount; i++)
        {
            if (_axes[i] != other._axes[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DeviceSnapshot other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(_down);
        hash.Add(_padDown);
        for (int i = 0; i < AxisCount; i++)
        {
            hash.Add(_axes[i]);
        }

        return hash.ToHashCode();
    }

    /// <summary>Whether the two snapshots capture the same instant.</summary>
    public static bool operator ==(DeviceSnapshot left, DeviceSnapshot right) => left.Equals(right);

    /// <summary>Whether the two snapshots differ in anything held or any axis.</summary>
    public static bool operator !=(DeviceSnapshot left, DeviceSnapshot right) => !left.Equals(right);

    private static UInt128 Bit(Key key)
    {
        int index = (int)key;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(key));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Capacity, nameof(key));

        // Key.None is the empty set, never a member, so bit 0 is deliberately unused.
        return key == Key.None ? UInt128.Zero : UInt128.One << index;
    }

    private static uint PadBit(PadButton button)
    {
        int index = (int)button;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(button));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, PadCapacity, nameof(button));

        // PadButton.None is the empty set, never a member, so bit 0 is deliberately unused.
        return button == PadButton.None ? 0u : 1u << index;
    }

    private static int AxisIndex(PadAxis axis)
    {
        if (axis == PadAxis.None)
        {
            throw new ArgumentOutOfRangeException(nameof(axis), axis, $"{nameof(PadAxis)}.{nameof(PadAxis.None)} names no axis.");
        }

        int index = (int)axis - 1;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(axis));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, AxisCount, nameof(axis));

        return index;
    }

    [InlineArray(AxisCount)]
    private struct AxisSet
    {
        private float _element0;
    }
}
