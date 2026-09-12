using System.Numerics;
using System.Runtime.CompilerServices;

namespace Capsule.Input;

/// <summary>
/// An allocation-free snapshot of held keys, pad buttons and mouse buttons, axis positions, the
/// pointer, and the wheel notches turned since the previous sample.
/// </summary>
public readonly struct DeviceSnapshot : IEquatable<DeviceSnapshot>
{
    /// <summary>Keys whose <see cref="Key"/> value must remain below this to be representable.</summary>
    public const int Capacity = 128;

    /// <summary>Buttons whose <see cref="PadButton"/> value must remain below this to be representable.</summary>
    public const int PadCapacity = 32;

    /// <summary>Buttons whose <see cref="MouseButton"/> value must remain below this to be representable.</summary>
    public const int MouseCapacity = 8;

    private const int AxisCount = 6;

    private readonly UInt128 _down;
    private readonly uint _padDown;
    private readonly uint _mouseDown;
    private readonly Vector2 _pointer;
    private readonly Vector2 _scroll;
    private readonly AxisSet _axes;

    private DeviceSnapshot(UInt128 down, uint padDown, uint mouseDown, Vector2 pointer, Vector2 scroll, AxisSet axes)
    {
        _down = down;
        _padDown = padDown;
        _mouseDown = mouseDown;
        _pointer = pointer;
        _scroll = scroll;
        _axes = axes;
    }

    /// <summary>
    /// A snapshot with nothing held, every axis at rest, the wheel still and the pointer on the
    /// canvas's top-left corner; equal to <c>default</c>.
    /// </summary>
    public static DeviceSnapshot Empty => default;

    /// <exception cref="ArgumentOutOfRangeException">Some key is not representable.</exception>
    public static DeviceSnapshot Of(params ReadOnlySpan<Key> keys)
    {
        UInt128 down = UInt128.Zero;
        for (int i = 0; i < keys.Length; i++)
        {
            down |= Bit(keys[i]);
        }

        return new DeviceSnapshot(down, 0, 0, Vector2.Zero, Vector2.Zero, default);
    }

    /// <summary>
    /// Whether nothing is held, every axis is at rest, the wheel is still and the pointer is at the
    /// origin.
    /// </summary>
    public bool IsEmpty => Equals(Empty);

    /// <summary>
    /// Where the pointer sits at this instant, in canvas pixels from the canvas's top-left corner.
    /// Never clamped: a pointer in a presentation bar, or outside the window altogether, reads outside
    /// the canvas and so falls outside everything on the screen layer. There is no presence flag — a
    /// run with no mouse is a pointer that never moves.
    /// </summary>
    public Vector2 Pointer => _pointer;

    /// <summary>
    /// Wheel notches turned since the previous sample, zero while the wheel rests: X positive scrolls
    /// right, Y positive scrolls away from the user. A displacement rather than a position, so it is
    /// summed when samples latch into one step, and it is bounded by nothing — a fast flick reads
    /// several notches in one step.
    /// </summary>
    public Vector2 Scroll => _scroll;

    /// <summary>Whether <paramref name="key"/> is held down at this instant.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The key is not representable.</exception>
    public bool IsDown(Key key) => (_down & Bit(key)) != UInt128.Zero;

    /// <summary>Whether <paramref name="button"/> is held down at this instant.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The button is not representable.</exception>
    public bool IsDown(PadButton button) => (_padDown & PadBit(button)) != 0;

    /// <summary>Whether <paramref name="button"/> is held down at this instant.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The button is not representable.</exception>
    public bool IsDown(MouseButton button) => (_mouseDown & MouseBit(button)) != 0;

    /// <summary>Position of <paramref name="axis"/>, past deadzone filtering; 0 at rest.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The axis is <see cref="PadAxis.None"/> or not representable.</exception>
    public float Axis(PadAxis axis) => _axes[AxisIndex(axis)];

    /// <summary>This snapshot with <paramref name="key"/> additionally held.</summary>
    public DeviceSnapshot With(Key key) => new(_down | Bit(key), _padDown, _mouseDown, _pointer, _scroll, _axes);

    /// <summary>This snapshot with <paramref name="button"/> additionally held.</summary>
    public DeviceSnapshot With(PadButton button) => new(_down, _padDown | PadBit(button), _mouseDown, _pointer, _scroll, _axes);

    /// <summary>This snapshot with <paramref name="button"/> additionally held.</summary>
    public DeviceSnapshot With(MouseButton button) => new(_down, _padDown, _mouseDown | MouseBit(button), _pointer, _scroll, _axes);

    /// <summary>This snapshot with <paramref name="key"/> released.</summary>
    public DeviceSnapshot Without(Key key) => new(_down & ~Bit(key), _padDown, _mouseDown, _pointer, _scroll, _axes);

    /// <summary>This snapshot with <paramref name="button"/> released.</summary>
    public DeviceSnapshot Without(PadButton button) => new(_down, _padDown & ~PadBit(button), _mouseDown, _pointer, _scroll, _axes);

    /// <summary>This snapshot with <paramref name="button"/> released.</summary>
    public DeviceSnapshot Without(MouseButton button) => new(_down, _padDown, _mouseDown & ~MouseBit(button), _pointer, _scroll, _axes);

    /// <summary>This snapshot with the pointer at <paramref name="position"/>.</summary>
    /// <param name="position">Canvas pixels from the canvas's top-left corner; unclamped, so a position outside the canvas is a pointer outside it.</param>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
    public DeviceSnapshot WithPointer(Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "A pointer position must be finite; a NaN or infinite one spreads to everything that reads it.");
        }

        return new DeviceSnapshot(_down, _padDown, _mouseDown, position, _scroll, _axes);
    }

    /// <summary>This snapshot with the wheel having turned <paramref name="notches"/>.</summary>
    /// <param name="notches">Wheel notches since the previous sample: X positive scrolls right, Y positive scrolls away from the user. Unbounded, and fractional on a wheel that reports finer than a notch.</param>
    /// <exception cref="ArgumentOutOfRangeException">The notches are not finite.</exception>
    public DeviceSnapshot WithScroll(Vector2 notches)
    {
        if (!float.IsFinite(notches.X) || !float.IsFinite(notches.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(notches),
                notches,
                "A scroll amount must be finite; a NaN or infinite one spreads to everything that reads it.");
        }

        return new DeviceSnapshot(_down, _padDown, _mouseDown, _pointer, notches, _axes);
    }

    /// <summary>This snapshot with <paramref name="axis"/> at <paramref name="value"/>.</summary>
    /// <param name="axis">The axis to place; never <see cref="PadAxis.None"/>.</param>
    /// <param name="value">In [-1, 1] for a stick, [0, 1] for a trigger.</param>
    /// <exception cref="ArgumentOutOfRangeException">The axis names none, or the value is outside its range.</exception>
    public DeviceSnapshot WithAxis(PadAxis axis, float value)
    {
        int index = AxisIndex(axis);
        if (!IsInRange(axis, value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(PadAxis)}.{axis} is in [{Minimum(axis)}, 1].");
        }

        AxisSet axes = _axes;
        axes[index] = value;

        return new DeviceSnapshot(_down, _padDown, _mouseDown, _pointer, _scroll, axes);
    }

    /// <summary>
    /// Unions held buttons with a newer sample, sums the wheel notches, and takes its axis values and
    /// its pointer, so a click or a notch between two fixed steps survives to the next one while the
    /// pointer is wherever it last was.
    /// </summary>
    public DeviceSnapshot LatchedWith(in DeviceSnapshot newer) =>
        new(
            _down | newer._down,
            _padDown | newer._padDown,
            _mouseDown | newer._mouseDown,
            newer._pointer,
            _scroll + newer._scroll,
            newer._axes);

    /// <summary>
    /// Whether the same keys and buttons are held, every axis reads the same, the wheel turned the
    /// same, and the pointer is on the same position.
    /// </summary>
    public bool Equals(DeviceSnapshot other)
    {
        if (_down != other._down || _padDown != other._padDown || _mouseDown != other._mouseDown ||
            _pointer != other._pointer || _scroll != other._scroll)
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
        hash.Add(_mouseDown);
        hash.Add(_pointer);
        hash.Add(_scroll);
        for (int i = 0; i < AxisCount; i++)
        {
            hash.Add(_axes[i]);
        }

        return hash.ToHashCode();
    }

    /// <summary>Whether the two snapshots capture the same instant.</summary>
    public static bool operator ==(DeviceSnapshot left, DeviceSnapshot right) => left.Equals(right);

    /// <summary>Whether the two snapshots differ in anything held, any axis, the wheel or the pointer.</summary>
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

    private static uint MouseBit(MouseButton button)
    {
        int index = (int)button;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(button));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MouseCapacity, nameof(button));

        // MouseButton.None is the empty set, never a member, so bit 0 is deliberately unused.
        return button == MouseButton.None ? 0u : 1u << index;
    }

    private static float Minimum(PadAxis axis) => axis is PadAxis.LeftTrigger or PadAxis.RightTrigger ? 0f : -1f;

    // Written as an accept, not a reject, so that NaN — false against either bound — falls out.
    private static bool IsInRange(PadAxis axis, float value) => value >= Minimum(axis) && value <= 1f;

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
