namespace Capsule.Input;

/// <summary>
/// The set of keys held down at one instant, and the determinism seam: the runtime
/// derives one per frame from hardware, a harness fabricates them, and nothing downstream
/// distinguishes the two. A value type over a fixed bitset, so producing, copying and
/// comparing snapshots never touches the heap.
/// </summary>
public readonly struct DeviceSnapshot : IEquatable<DeviceSnapshot>
{
    /// <summary>Keys whose <see cref="Key"/> value must remain below this to be representable.</summary>
    public const int Capacity = 128;

    private readonly UInt128 _down;

    private DeviceSnapshot(UInt128 down) => _down = down;

    /// <summary>A snapshot with nothing held; equal to <c>default</c>.</summary>
    public static DeviceSnapshot Empty => default;

    /// <exception cref="ArgumentOutOfRangeException">Some key is not representable.</exception>
    public static DeviceSnapshot Of(params ReadOnlySpan<Key> keys)
    {
        UInt128 down = UInt128.Zero;
        for (int i = 0; i < keys.Length; i++)
        {
            down |= Bit(keys[i]);
        }

        return new DeviceSnapshot(down);
    }

    public bool IsEmpty => _down == UInt128.Zero;

    public bool IsDown(Key key) => (_down & Bit(key)) != UInt128.Zero;

    /// <summary>This snapshot with <paramref name="key"/> additionally held.</summary>
    public DeviceSnapshot With(Key key) => new(_down | Bit(key));

    /// <summary>This snapshot with <paramref name="key"/> released.</summary>
    public DeviceSnapshot Without(Key key) => new(_down & ~Bit(key));

    /// <summary>This snapshot with every key held in <paramref name="other"/> additionally held.</summary>
    public DeviceSnapshot Union(in DeviceSnapshot other) => new(_down | other._down);

    public bool Equals(DeviceSnapshot other) => _down == other._down;

    public override bool Equals(object? obj) => obj is DeviceSnapshot other && Equals(other);

    public override int GetHashCode() => _down.GetHashCode();

    public static bool operator ==(DeviceSnapshot left, DeviceSnapshot right) => left.Equals(right);

    public static bool operator !=(DeviceSnapshot left, DeviceSnapshot right) => !left.Equals(right);

    private static UInt128 Bit(Key key)
    {
        int index = (int)key;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(key));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Capacity, nameof(key));

        // Key.None is the empty set, never a member, so bit 0 is deliberately unused.
        return key == Key.None ? UInt128.Zero : UInt128.One << index;
    }
}
