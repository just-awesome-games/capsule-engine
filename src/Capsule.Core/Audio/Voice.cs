namespace Capsule.Audio;

/// <summary>
/// A handle to one playing sound, valid only on the <see cref="AudioMixer"/> that issued it and
/// only until that voice stops, expires or is stolen. A handle to a voice that has ended reads as
/// neither playing nor paused, and every mixer call taking it does nothing — a caller may hold one
/// indefinitely without checking.
/// </summary>
public readonly record struct Voice
{
    // Slot in the low bits, generation above it: the generation advances whenever a slot is freed,
    // so a handle to the voice that used to live there never addresses the one that lives there now.
    private const int SlotBits = 8;
    private const int SlotMask = (1 << SlotBits) - 1;

    private readonly int _handle;

    private Voice(int handle) => _handle = handle;

    /// <summary>No voice: what a mixer with nothing to steal answers with, and the default value.</summary>
    public static Voice None => default;

    /// <summary>Whether this is <see cref="None"/> rather than a voice a mixer once issued.</summary>
    public bool IsNone => _handle == 0;

    // Generations start at 1, so no live voice is ever packed as zero.
    internal static Voice Of(int slot, int generation) => new((generation << SlotBits) | slot);

    internal int Slot => _handle & SlotMask;

    internal int Generation => _handle >>> SlotBits;
}
