using System.Numerics;
using System.Runtime.CompilerServices;

namespace Capsule.Input;

/// <summary>
/// An allocation-free snapshot of held keys, pad buttons and mouse buttons, axis positions, the
/// pointer, and the wheel notches turned since the previous sample.
/// </summary>
/// <remarks>
/// A key or button outside the capacity its device declares cannot be held. The builders throw on
/// one, and a read reports it as not down.
/// </remarks>
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
    /// canvas's top-left corner. Equal to <c>default</c>.
    /// </summary>
    public static DeviceSnapshot Empty => default;

    /// <summary>A snapshot holding <paramref name="keys"/> and nothing else.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A key is outside <see cref="Capacity"/>.</exception>
    public static DeviceSnapshot Of(params ReadOnlySpan<Key> keys)
    {
        UInt128 down = UInt128.Zero;
        for (int i = 0; i < keys.Length; i++)
        {
            down |= Bit(keys[i]);
        }

        return new DeviceSnapshot(down, 0, 0, Vector2.Zero, Vector2.Zero, default);
    }

    /// <summary>Whether nothing is held, every axis is at rest, the wheel is still and the pointer is at the origin.</summary>
    public bool IsEmpty => Equals(Empty);

    /// <summary>
    /// Where the pointer sits at this instant, in canvas pixels from the canvas's top-left corner.
    /// Unclamped.
    /// </summary>
    /// <remarks>A pointer outside the window reads outside the canvas.</remarks>
    public Vector2 Pointer => _pointer;

    /// <summary>
    /// Wheel notches turned since the previous sample, and zero while the wheel rests. X positive
    /// scrolls right, Y positive scrolls away from the user.
    /// </summary>
    /// <remarks>Unbounded. Latching several samples into one step sums the notches.</remarks>
    public Vector2 Scroll => _scroll;

    /// <summary>Whether <paramref name="key"/> is held down at this instant.</summary>
    public bool IsDown(Key key) => (uint)key < Capacity && (_down & (UInt128.One << (int)key)) != UInt128.Zero;

    /// <summary>Whether <paramref name="button"/> is held down at this instant.</summary>
    public bool IsDown(PadButton button) => (uint)button < PadCapacity && (_padDown & (1u << (int)button)) != 0;

    /// <summary>Whether <paramref name="button"/> is held down at this instant.</summary>
    public bool IsDown(MouseButton button) => (uint)button < MouseCapacity && (_mouseDown & (1u << (int)button)) != 0;

    /// <summary>Position of <paramref name="axis"/>, reading 0 at rest and for <see cref="PadAxis.None"/>.</summary>
    public float Axis(PadAxis axis)
    {
        int index = (int)axis - 1;

        return (uint)index < AxisCount ? _axes[index] : 0f;
    }

    /// <summary>This snapshot with <paramref name="key"/> additionally held.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The key is outside <see cref="Capacity"/>.</exception>
    public DeviceSnapshot With(Key key) => new(_down | Bit(key), _padDown, _mouseDown, _pointer, _scroll, _axes);

    /// <summary>This snapshot with <paramref name="button"/> additionally held.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The button is outside <see cref="PadCapacity"/>.</exception>
    public DeviceSnapshot With(PadButton button) => new(_down, _padDown | PadBit(button), _mouseDown, _pointer, _scroll, _axes);

    /// <summary>This snapshot with <paramref name="button"/> additionally held.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The button is outside <see cref="MouseCapacity"/>.</exception>
    public DeviceSnapshot With(MouseButton button) => new(_down, _padDown, _mouseDown | MouseBit(button), _pointer, _scroll, _axes);

    /// <summary>This snapshot with <paramref name="key"/> released.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The key is outside <see cref="Capacity"/>.</exception>
    public DeviceSnapshot Without(Key key) => new(_down & ~Bit(key), _padDown, _mouseDown, _pointer, _scroll, _axes);

    /// <summary>This snapshot with <paramref name="button"/> released.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The button is outside <see cref="PadCapacity"/>.</exception>
    public DeviceSnapshot Without(PadButton button) => new(_down, _padDown & ~PadBit(button), _mouseDown, _pointer, _scroll, _axes);

    /// <summary>This snapshot with <paramref name="button"/> released.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The button is outside <see cref="MouseCapacity"/>.</exception>
    public DeviceSnapshot Without(MouseButton button) => new(_down, _padDown, _mouseDown & ~MouseBit(button), _pointer, _scroll, _axes);

    // A sampler walks a device's held buttons once and folds them into one mask. A sample costs one
    // snapshot instead of one per button.
    internal static UInt128 MaskOf(Key key) => Bit(key);

    internal static uint MaskOf(PadButton button) => PadBit(button);

    internal static uint MaskOf(MouseButton button) => MouseBit(button);

    // Whether a pad button is down here that was not down in the older snapshot.
    internal bool AnyPadButtonNewlyDown(in DeviceSnapshot older) => (_padDown & ~older._padDown) != 0;

    // Whether a key or mouse button is down here that was not down in the older snapshot.
    internal bool AnyKeyOrMouseButtonNewlyDown(in DeviceSnapshot older) =>
        (_down & ~older._down) != UInt128.Zero || (_mouseDown & ~older._mouseDown) != 0;

    // The lowest-valued key, pad button or mouse button newly down here, or null. The trailing zero
    // count of the newly-pressed mask is that lowest value's index, so no loop is needed.
    internal Key? NewlyDownKey(in DeviceSnapshot older)
    {
        UInt128 pressed = _down & ~older._down;
        return pressed == 0 ? null : (Key)(int)UInt128.TrailingZeroCount(pressed);
    }

    internal MouseButton? NewlyDownMouseButton(in DeviceSnapshot older)
    {
        uint pressed = _mouseDown & ~older._mouseDown;
        return pressed == 0 ? null : (MouseButton)BitOperations.TrailingZeroCount(pressed);
    }

    internal PadButton? NewlyDownPadButton(in DeviceSnapshot older)
    {
        uint pressed = _padDown & ~older._padDown;
        return pressed == 0 ? null : (PadButton)BitOperations.TrailingZeroCount(pressed);
    }

    // Whether any pad axis is off centre. Snapshots are deadzone filtered, so any value is a push.
    internal bool AnyPadAxisActive
    {
        get
        {
            for (int i = 0; i < AxisCount; i++)
            {
                if (_axes[i] != 0f)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal DeviceSnapshot WithKeys(UInt128 keys) => new(_down | keys, _padDown, _mouseDown, _pointer, _scroll, _axes);

    internal DeviceSnapshot WithPadButtons(uint buttons) => new(_down, _padDown | buttons, _mouseDown, _pointer, _scroll, _axes);

    internal DeviceSnapshot WithMouseButtons(uint buttons) => new(_down, _padDown, _mouseDown | buttons, _pointer, _scroll, _axes);

    internal DeviceSnapshot WithoutKeys(UInt128 keys) => new(_down & ~keys, _padDown, _mouseDown, _pointer, _scroll, _axes);

    internal DeviceSnapshot WithoutPadButtons(uint buttons) => new(_down, _padDown & ~buttons, _mouseDown, _pointer, _scroll, _axes);

    internal DeviceSnapshot WithoutMouseButtons(uint buttons) => new(_down, _padDown, _mouseDown & ~buttons, _pointer, _scroll, _axes);

    /// <summary>
    /// This snapshot with <paramref name="button"/> released. A stick direction is removed by
    /// moving its axis just inside the direction's press point.
    /// </summary>
    /// <param name="button">The key, pad button, mouse button or stick direction to release.</param>
    public DeviceSnapshot Without(InputButton button) => button.RemoveFrom(this);

    /// <summary>This snapshot with the pointer at <paramref name="position"/>.</summary>
    /// <param name="position">
    /// Canvas pixels from the canvas's top-left corner, unclamped. A position outside the canvas puts
    /// the pointer outside it.
    /// </param>
    public DeviceSnapshot WithPointer(Vector2 position)
    {
        Guard.Finite(position, nameof(position));

        return new DeviceSnapshot(_down, _padDown, _mouseDown, position, _scroll, _axes);
    }

    /// <summary>This snapshot with the wheel having turned <paramref name="notches"/>.</summary>
    /// <param name="notches">
    /// Wheel notches since the previous sample. X positive scrolls right, Y positive scrolls away
    /// from the user. Unbounded, and fractional on a wheel that reports finer than a notch.
    /// </param>
    public DeviceSnapshot WithScroll(Vector2 notches)
    {
        Guard.Finite(notches, nameof(notches));

        return new DeviceSnapshot(_down, _padDown, _mouseDown, _pointer, notches, _axes);
    }

    /// <summary>This snapshot with <paramref name="axis"/> at <paramref name="value"/>.</summary>
    /// <param name="axis">The axis to place. Must not be <see cref="PadAxis.None"/>.</param>
    /// <param name="value">In [-1, 1] for a stick, [0, 1] for a trigger.</param>
    /// <exception cref="ArgumentOutOfRangeException">The axis names none, or the value is outside its range.</exception>
    public DeviceSnapshot WithAxis(PadAxis axis, float value)
    {
        int index = AxisIndex(axis);
        Guard.InRange(value, Minimum(axis), 1f, nameof(value));

        AxisSet axes = _axes;
        axes[index] = value;

        return new DeviceSnapshot(_down, _padDown, _mouseDown, _pointer, _scroll, axes);
    }

    // Unions held buttons with a newer sample, sums the wheel notches, and takes the newer axis values
    // and pointer. A click or notch between two fixed steps survives to the next step.
    internal DeviceSnapshot LatchedWith(in DeviceSnapshot newer) =>
        new(
            _down | newer._down,
            _padDown | newer._padDown,
            _mouseDown | newer._mouseDown,
            newer._pointer,
            _scroll + newer._scroll,
            newer._axes);

    /// <summary>
    /// Whether the same keys and buttons are held, every axis and the wheel read the same, and the
    /// pointer is on the same position.
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

    /// <summary>Whether the two snapshots read the same in everything held, every axis, the wheel and the pointer.</summary>
    public static bool operator ==(DeviceSnapshot left, DeviceSnapshot right) => left.Equals(right);

    /// <summary>Whether the two snapshots differ in anything held, any axis, the wheel or the pointer.</summary>
    public static bool operator !=(DeviceSnapshot left, DeviceSnapshot right) => !left.Equals(right);

    // A None value is the empty set, so bit 0 is unused in all three masks.
    private static UInt128 Bit(Key key)
    {
        int index = (int)key;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(key));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Capacity, nameof(key));

        return key == Key.None ? UInt128.Zero : UInt128.One << index;
    }

    private static uint PadBit(PadButton button)
    {
        int index = (int)button;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(button));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, PadCapacity, nameof(button));

        return button == PadButton.None ? 0u : 1u << index;
    }

    private static uint MouseBit(MouseButton button)
    {
        int index = (int)button;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(button));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MouseCapacity, nameof(button));

        return button == MouseButton.None ? 0u : 1u << index;
    }

    private static float Minimum(PadAxis axis) => axis is PadAxis.LeftTrigger or PadAxis.RightTrigger ? 0f : -1f;

    private static int AxisIndex(PadAxis axis)
    {
        int index = (int)axis - 1;
        if ((uint)index >= AxisCount)
        {
            throw new ArgumentOutOfRangeException(nameof(axis), axis, $"{nameof(PadAxis)} has no such axis.");
        }

        return index;
    }

    [InlineArray(AxisCount)]
    private struct AxisSet
    {
        private float _element0;
    }
}
