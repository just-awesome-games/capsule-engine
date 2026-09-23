namespace Capsule.Input;

/// <summary>
/// A handle to one rumble pulse, valid on the <see cref="Rumble"/> that issued it and until that
/// pulse ends, is stopped or is evicted.
/// </summary>
/// <remarks>
/// A mixer call taking a handle to a pulse that has ended does nothing. A caller may hold one
/// indefinitely without checking.
/// </remarks>
public readonly record struct RumbleHandle
{
    // Slot in the low bits and generation above it. The generation advances when a slot is freed, so a
    // handle to the pulse that used to live there cannot address the pulse living there now.
    private const int SlotBits = 8;
    private const int SlotMask = (1 << SlotBits) - 1;

    private readonly int _handle;

    private RumbleHandle(int handle) => _handle = handle;

    /// <summary>No pulse. The default value.</summary>
    public static RumbleHandle None => default;

    /// <summary>Whether this is <see cref="None"/> instead of a pulse a mixer issued.</summary>
    public bool IsNone => _handle == 0;

    // Generations start at 1. A live pulse never packs to zero.
    internal static RumbleHandle Of(int slot, int generation) => new((generation << SlotBits) | slot);

    internal int Slot => _handle & SlotMask;

    internal int Generation => _handle >>> SlotBits;
}
