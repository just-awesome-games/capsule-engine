namespace Capsule.Audio;

/// <summary>
/// A handle to one playing sound, valid on the <see cref="AudioMixer"/> that issued it and until that
/// voice stops, expires or is stolen. A mixer call taking a handle to a voice that has ended does
/// nothing. A caller may hold one indefinitely without checking.
/// </summary>
public readonly record struct Voice
{
    // Slot in the low bits and generation above it. The generation advances when a slot is freed, so a
    // handle to the voice that used to live there cannot address the voice living there now.
    private const int SlotBits = 8;
    private const int SlotMask = (1 << SlotBits) - 1;

    private readonly int _handle;

    private Voice(int handle) => _handle = handle;

    /// <summary>No voice. A mixer with nothing to steal returns this, and it is the default value.</summary>
    public static Voice None => default;

    /// <summary>Whether this is <see cref="None"/> instead of a voice a mixer issued.</summary>
    public bool IsNone => _handle == 0;

    // Generations start at 1. A live voice never packs to zero.
    internal static Voice Of(int slot, int generation) => new((generation << SlotBits) | slot);

    internal int Slot => _handle & SlotMask;

    internal int Generation => _handle >>> SlotBits;
}
