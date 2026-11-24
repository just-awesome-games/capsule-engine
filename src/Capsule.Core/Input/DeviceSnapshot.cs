using System.Numerics;
using System.Runtime.CompilerServices;

namespace Capsule.Input;

/// <summary>
/// An allocation-free snapshot of window focus, whether a pad is in use, its family and the buttons it has, held
/// keys, pad buttons and mouse buttons, axis positions, the pointer, and the wheel notches turned since
/// the previous sample.
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

    // The axes a snapshot holds, one slot per PadAxis value above None.
    internal const int AxisCount = 6;

    // Bit n of each mask is the key or button of value n. A recording writes and reads these back raw.
    internal UInt128 KeyBits { get; private init; }

    internal uint PadBits { get; private init; }

    internal uint MouseBits { get; private init; }

    // The pad buttons the active pad lacks, inverted so that default reads a full pad.
    internal uint PadLackingBits { get; private init; }

    // Whether the canvas moved under the pointer since the previous sample, as when the window or the
    // screen layer's placement changed. A jump of the pointer here is no motion.
    internal bool PointerRemapped { get; private init; }

    // Inverted so that default reads focused.
    private bool Unfocused { get; init; }

    // Inverted so that default reads a pad in use.
    private bool PadMissing { get; init; }

    private AxisSet Axes { get; init; }

    /// <summary>
    /// A snapshot with window focus, a connected <see cref="PadFamily.Generic"/> pad having every
    /// <see cref="PadButton"/>, nothing held, every axis at rest, the wheel still and the pointer on the
    /// canvas's top-left corner. Equal to <c>default</c>.
    /// </summary>
    public static DeviceSnapshot Empty => default;

    /// <summary>A snapshot holding <paramref name="keys"/> and nothing else.</summary>
    public static DeviceSnapshot Of(params ReadOnlySpan<Key> keys)
    {
        UInt128 down = UInt128.Zero;
        for (int i = 0; i < keys.Length; i++)
        {
            down |= Bit(keys[i]);
        }

        return new DeviceSnapshot { KeyBits = down };
    }

    // Whether the window has focus, the pad is connected, generic and full, nothing is held, every axis is at rest, the wheel is still and the pointer is at the origin.
    internal bool IsEmpty => Equals(Empty);

    // The axis in slot index, which is its PadAxis value less one.
    internal float AxisAt(int index) => Axes[index];

    // A snapshot from the parts a recording read, which a writer took from a valid one.
    internal static DeviceSnapshot FromParts(
        UInt128 keys,
        uint pad,
        uint mouse,
        Vector2 pointer,
        Vector2 scroll,
        ReadOnlySpan<float> axes,
        bool focused,
        bool hasPad,
        PadFamily family,
        uint padLacking,
        bool pointerRemapped)
    {
        AxisSet set = default;
        for (int index = 0; index < AxisCount; index++)
        {
            set[index] = axes[index];
        }

        return new DeviceSnapshot
        {
            KeyBits = keys,
            PadBits = pad,
            MouseBits = mouse,
            Pointer = pointer,
            Scroll = scroll,
            Axes = set,
            Unfocused = !focused,
            PadMissing = !hasPad,
            PadFamily = family,
            PadLackingBits = padLacking,
            PointerRemapped = pointerRemapped,
        };
    }

    /// <summary>
    /// Where the pointer sits at this instant, in canvas pixels from the canvas's top-left corner.
    /// Unclamped.
    /// </summary>
    /// <remarks>A pointer outside the window reads outside the canvas.</remarks>
    public Vector2 Pointer { get; private init; }

    /// <summary>
    /// Wheel notches turned since the previous sample, and zero while the wheel rests. X positive
    /// scrolls right, Y positive scrolls away from the user.
    /// </summary>
    /// <remarks>Unbounded. Latching several samples into one step sums the notches.</remarks>
    public Vector2 Scroll { get; private init; }

    /// <summary>Whether the game's window has input focus at this instant.</summary>
    public bool HasWindowFocus => !Unfocused;

    /// <summary>Whether a pad is in use at this instant.</summary>
    public bool HasPad => !PadMissing;

    /// <summary>The family of the pad the player last used, which a prompt's art follows.</summary>
    /// <remarks>It keeps the last pad's family while no pad is in use, and changing it alone is no use of the pad.</remarks>
    public PadFamily PadFamily { get; private init; }

    /// <summary>
    /// Whether the player's pad has <paramref name="button"/>, as its mapping defines it. Every button
    /// reads present until a pad is found, and <see cref="PadButton.None"/> never does.
    /// </summary>
    /// <remarks>
    /// The answer comes from the pad's mapping and not its hardware, and a button the mapping lacks can
    /// never be pressed. It keeps the last pad's buttons while no pad is in use.
    /// </remarks>
    public bool PadHas(PadButton button) =>
        button != PadButton.None && (uint)button < PadCapacity && (PadLackingBits & (1u << (int)button)) == 0;

    /// <summary>Whether <paramref name="key"/> is held down at this instant.</summary>
    public bool IsDown(Key key) => (uint)key < Capacity && (KeyBits & (UInt128.One << (int)key)) != UInt128.Zero;

    /// <summary>Whether <paramref name="button"/> is held down at this instant.</summary>
    public bool IsDown(PadButton button) => (uint)button < PadCapacity && (PadBits & (1u << (int)button)) != 0;

    /// <summary>Whether <paramref name="button"/> is held down at this instant.</summary>
    public bool IsDown(MouseButton button) => (uint)button < MouseCapacity && (MouseBits & (1u << (int)button)) != 0;

    /// <summary>Position of <paramref name="axis"/>, reading 0 at rest and for <see cref="PadAxis.None"/>.</summary>
    public float Axis(PadAxis axis)
    {
        int index = (int)axis - 1;

        return (uint)index < AxisCount ? Axes[index] : 0f;
    }

    /// <summary>This snapshot with <paramref name="key"/> additionally held.</summary>
    public DeviceSnapshot With(Key key) => this with { KeyBits = KeyBits | Bit(key) };

    /// <summary>This snapshot with <paramref name="button"/> additionally held.</summary>
    public DeviceSnapshot With(PadButton button) => this with { PadBits = PadBits | PadBit(button) };

    /// <summary>This snapshot with <paramref name="button"/> additionally held.</summary>
    public DeviceSnapshot With(MouseButton button) => this with { MouseBits = MouseBits | MouseBit(button) };

    /// <summary>This snapshot with <paramref name="key"/> released.</summary>
    public DeviceSnapshot Without(Key key) => this with { KeyBits = KeyBits & ~Bit(key) };

    /// <summary>This snapshot with <paramref name="button"/> released.</summary>
    public DeviceSnapshot Without(PadButton button) => this with { PadBits = PadBits & ~PadBit(button) };

    /// <summary>This snapshot with <paramref name="button"/> released.</summary>
    public DeviceSnapshot Without(MouseButton button) => this with { MouseBits = MouseBits & ~MouseBit(button) };

    // Whether a pad button is down here that was not down in the older snapshot.
    internal bool AnyPadButtonNewlyDown(in DeviceSnapshot older) => (PadBits & ~older.PadBits) != 0;

    // Whether a key or mouse button is down here that was not down in the older snapshot.
    internal bool AnyKeyOrMouseButtonNewlyDown(in DeviceSnapshot older) =>
        (KeyBits & ~older.KeyBits) != UInt128.Zero || (MouseBits & ~older.MouseBits) != 0;

    // The lowest-valued key newly down here, or null.
    internal Key? NewlyDownKey(in DeviceSnapshot older)
    {
        UInt128 pressed = KeyBits & ~older.KeyBits;
        return pressed == 0 ? null : (Key)(int)UInt128.TrailingZeroCount(pressed);
    }

    internal MouseButton? NewlyDownMouseButton(in DeviceSnapshot older)
    {
        uint pressed = MouseBits & ~older.MouseBits;
        return pressed == 0 ? null : (MouseButton)BitOperations.TrailingZeroCount(pressed);
    }

    internal PadButton? NewlyDownPadButton(in DeviceSnapshot older)
    {
        uint pressed = PadBits & ~older.PadBits;
        return pressed == 0 ? null : (PadButton)BitOperations.TrailingZeroCount(pressed);
    }

    // Whether any pad axis is off centre. Snapshots are deadzone filtered, and any value is a push.
    internal bool AnyPadAxisActive
    {
        get
        {
            for (int i = 0; i < AxisCount; i++)
            {
                if (Axes[i] != 0f)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Mouse buttons as a mask whose bit n is the button of value n.
    internal DeviceSnapshot WithMouseButtons(uint buttons) => this with { MouseBits = MouseBits | buttons };

    /// <summary>
    /// This snapshot with <paramref name="button"/> additionally held. A stick direction is held by
    /// pushing its axis fully in that direction.
    /// </summary>
    /// <param name="button">The key, pad button, mouse button or stick direction to hold.</param>
    public DeviceSnapshot With(InputButton button) => button.AddTo(this);

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

        return this with { Pointer = position };
    }

    /// <summary>This snapshot with the wheel having turned <paramref name="notches"/>.</summary>
    /// <param name="notches">
    /// Wheel notches since the previous sample. X positive scrolls right, Y positive scrolls away
    /// from the user. Unbounded, and fractional on a wheel that reports finer than a notch.
    /// </param>
    public DeviceSnapshot WithScroll(Vector2 notches)
    {
        Guard.Finite(notches, nameof(notches));

        return this with { Scroll = notches };
    }

    /// <summary>This snapshot with the game's window focused or not.</summary>
    public DeviceSnapshot WithWindowFocus(bool focused) => this with { Unfocused = !focused };

    /// <summary>This snapshot with the player's pad connected or not.</summary>
    public DeviceSnapshot WithPad(bool connected) => this with { PadMissing = !connected };

    /// <summary>This snapshot with the player's pad of <paramref name="family"/>.</summary>
    public DeviceSnapshot WithPadFamily(PadFamily family)
    {
        if (family > PadFamily.Nintendo)
        {
            throw new ArgumentOutOfRangeException(nameof(family), family, $"{nameof(PadFamily)} has no such family.");
        }

        return this with { PadFamily = family };
    }

    /// <summary>
    /// This snapshot with the player's pad lacking <paramref name="buttons"/> and having every other
    /// <see cref="PadButton"/>, which replaces the buttons any earlier call set.
    /// </summary>
    /// <param name="buttons">The buttons the pad lacks. None lacked is a full pad.</param>
    /// <example>
    /// A pad with no triggers:
    /// <code>
    /// DeviceSnapshot.Empty.WithPadLacking(PadButton.LeftTrigger, PadButton.RightTrigger)
    /// </code>
    /// </example>
    public DeviceSnapshot WithPadLacking(params ReadOnlySpan<PadButton> buttons)
    {
        uint lacking = 0;
        for (int i = 0; i < buttons.Length; i++)
        {
            lacking |= PadBit(buttons[i]);
        }

        return WithPadLackingBits(lacking);
    }

    // This snapshot with the canvas having moved under the pointer since the previous sample or not.
    internal DeviceSnapshot WithPointerRemapped(bool remapped) => this with { PointerRemapped = remapped };

    // The pad buttons as a mask whose bit n is the button of value n the pad lacks.
    internal DeviceSnapshot WithPadLackingBits(uint lacking) => this with { PadLackingBits = lacking };

    /// <summary>This snapshot with <paramref name="axis"/> at <paramref name="value"/>.</summary>
    /// <param name="axis">The axis to place. Must not be <see cref="PadAxis.None"/>.</param>
    /// <param name="value">In [-1, 1] for a stick, [0, 1] for a trigger.</param>
    public DeviceSnapshot WithAxis(PadAxis axis, float value)
    {
        int index = AxisIndex(axis);
        Guard.InRange(value, Minimum(axis), 1f, nameof(value));

        AxisSet axes = Axes;
        axes[index] = value;

        return this with { Axes = axes };
    }

    // Unions held buttons, window focus loss and a remap of the pointer with a newer sample, sums the
    // wheel notches, and takes the newer axis values, pointer, pad connection, pad family and pad buttons.
    // A click, notch, window focus loss or remap between two fixed steps survives to the next step. A pad
    // gone and back between two steps is the driver finding it again, and no loss.
    internal DeviceSnapshot LatchedWith(in DeviceSnapshot newer) => newer with
    {
        KeyBits = KeyBits | newer.KeyBits,
        PadBits = PadBits | newer.PadBits,
        MouseBits = MouseBits | newer.MouseBits,
        Scroll = Scroll + newer.Scroll,
        Unfocused = Unfocused | newer.Unfocused,
        PointerRemapped = PointerRemapped | newer.PointerRemapped,
    };

    /// <summary>
    /// Whether window focus, the pad's connection, family and buttons are the same, the same keys and buttons
    /// are held, every axis and the wheel read the same, and the pointer is on the same position with the
    /// canvas moved under it in both or neither.
    /// </summary>
    public bool Equals(DeviceSnapshot other)
    {
        if (KeyBits != other.KeyBits || PadBits != other.PadBits || MouseBits != other.MouseBits ||
            Pointer != other.Pointer || Scroll != other.Scroll || Unfocused != other.Unfocused || PadMissing != other.PadMissing ||
            PadFamily != other.PadFamily || PadLackingBits != other.PadLackingBits || PointerRemapped != other.PointerRemapped)
        {
            return false;
        }

        for (int i = 0; i < AxisCount; i++)
        {
            if (Axes[i] != other.Axes[i])
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
        hash.Add(KeyBits);
        hash.Add(PadBits);
        hash.Add(MouseBits);
        hash.Add(Pointer);
        hash.Add(Scroll);
        hash.Add(Unfocused);
        hash.Add(PadMissing);
        hash.Add(PadFamily);
        hash.Add(PadLackingBits);
        hash.Add(PointerRemapped);
        for (int i = 0; i < AxisCount; i++)
        {
            hash.Add(Axes[i]);
        }

        return hash.ToHashCode();
    }

    /// <summary>Whether the two snapshots read the same in window focus, the pad's connection, family and buttons, everything held, every axis, the wheel, the pointer and whether the canvas moved under it.</summary>
    public static bool operator ==(DeviceSnapshot left, DeviceSnapshot right) => left.Equals(right);

    /// <summary>Whether the two snapshots differ in window focus, the pad's connection, family or buttons, anything held, any axis, the wheel, the pointer or whether the canvas moved under it.</summary>
    public static bool operator !=(DeviceSnapshot left, DeviceSnapshot right) => !left.Equals(right);

    // A None value is the empty set. Bit 0 is unused in all three masks.
    private static UInt128 Bit(Key key) => key == Key.None ? UInt128.Zero : UInt128.One << Index((int)key, Capacity, nameof(key));

    private static uint PadBit(PadButton button) => button == PadButton.None ? 0u : 1u << Index((int)button, PadCapacity, nameof(button));

    private static uint MouseBit(MouseButton button) => button == MouseButton.None ? 0u : 1u << Index((int)button, MouseCapacity, nameof(button));

    private static int Index(int value, int capacity, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, capacity, parameterName);

        return value;
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
