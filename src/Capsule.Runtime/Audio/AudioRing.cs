using System.Numerics;

namespace Capsule.Runtime.Audio;

// A fixed-capacity queue between exactly one producing thread and one consuming thread, with no lock
// and no allocation after construction. The game thread and the mix thread talk through two of these.
//
// Each side owns one counter and only reads the other's. An item is written before its counter is
// published and read before the slot is handed back. A slot is cleared as it is read, which lets
// go of any reference the item held, such as a clip's samples.
internal sealed class AudioRing<T>
    where T : struct
{
    private readonly T[] _items;
    private readonly int _mask;

    // Items written, advanced by the producer alone.
    private long _written;

    // Items read, advanced by the consumer alone.
    private long _read;

    private volatile bool _overflowed;

    // capacity is rounded up to a power of two.
    internal AudioRing(int capacity)
    {
        _items = new T[(int)BitOperations.RoundUpToPowerOf2((uint)capacity)];
        _mask = _items.Length - 1;
    }

    internal int Capacity => _items.Length;

    // Whether a write found the ring full, which loses that item. Sticky, and read by the consumer to
    // fail loudly on a ring sized too small.
    internal bool Overflowed => _overflowed;

    // On the producer. False when the ring is full, and then nothing was written.
    internal bool TryWrite(in T item)
    {
        long written = _written;
        if (written - Volatile.Read(ref _read) >= _items.Length)
        {
            return false;
        }

        _items[(int)written & _mask] = item;
        Volatile.Write(ref _written, written + 1);

        return true;
    }

    // On the producer, for a producer that must never wait. A full ring marks it overflowed.
    internal void Write(in T item)
    {
        if (!TryWrite(item))
        {
            _overflowed = true;
        }
    }

    // On the consumer.
    internal bool TryRead(out T item)
    {
        long read = _read;
        if (read == Volatile.Read(ref _written))
        {
            item = default;

            return false;
        }

        ref T slot = ref _items[(int)read & _mask];
        item = slot;
        slot = default;
        Volatile.Write(ref _read, read + 1);

        return true;
    }
}
