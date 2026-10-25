namespace Capsule.Runtime.Input;

// Turns trigger motion into presses: a trigger's raw reading crossing the press threshold from below.
// One instance per feed, shared by both of its records, and remembering each pad's triggers across
// frames. Used under the feed's lock. The game thread publishes the threshold, and the watch reads it.
internal sealed class TriggerEdges
{
    // Pads past this many with a trigger held at once reuse the oldest slot.
    private const int Slots = InputEvents.MaxPressingPads;

    private readonly (uint Pad, bool Left, bool Right)[] _above = new (uint, bool, bool)[Slots];
    private int _next;

    // The lowest raw reading that reads pressed. int.MaxValue, above every reading, until one is set.
    private int _threshold = int.MaxValue;

    internal int Threshold
    {
        get => Volatile.Read(ref _threshold);
        set => Volatile.Write(ref _threshold, value);
    }

    // Whether this reading of pad's left or right trigger crosses the threshold upward. A pad first
    // seen above it rose from rest.
    internal bool Rose(uint pad, bool left, short value)
    {
        bool above = value >= Threshold;
        int slot = SlotOf(pad);
        if (slot < 0)
        {
            if (!above)
            {
                return false;
            }

            slot = SlotOf(0);
            if (slot < 0)
            {
                slot = _next;
                _next = (_next + 1) % Slots;
            }

            _above[slot] = (pad, false, false);
        }

        ref (uint Pad, bool Left, bool Right) entry = ref _above[slot];
        bool was = left ? entry.Left : entry.Right;
        if (left)
        {
            entry.Left = above;
        }
        else
        {
            entry.Right = above;
        }

        return above && !was;
    }

    // A removed pad's triggers are at rest when its id is next seen.
    internal void Forget(uint pad)
    {
        int slot = SlotOf(pad);
        if (slot >= 0)
        {
            _above[slot] = default;
        }
    }

    // Allocates nothing: the watch calls it.
    private int SlotOf(uint pad)
    {
        for (int slot = 0; slot < Slots; slot++)
        {
            if (_above[slot].Pad == pad)
            {
                return slot;
            }
        }

        return -1;
    }
}
