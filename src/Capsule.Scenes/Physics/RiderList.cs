namespace Capsule.Physics;

// The bodies riding a collider or a map's cells, grown once and never shrunk. While its owner carries,
// a removed rider's slot empties to null, and Compact closes the gaps once the carry is done.
internal struct RiderList
{
    // How many riders the list holds from construction on. A larger crowd doubles it.
    private const int StartingRiders = 4;

    private KinematicBody2D?[] _riders;
    private int _count;
    private bool _emptied;

    public RiderList() => _riders = new KinematicBody2D?[StartingRiders];

    // How many slots are in use, emptied ones included.
    internal readonly int Count => _count;

    // The rider in a slot, or null where one left during a carry.
    internal readonly KinematicBody2D? this[int index] => _riders[index];

    internal void Add(KinematicBody2D body)
    {
        if (_count == _riders.Length)
        {
            Array.Resize(ref _riders, _riders.Length * 2);
        }

        _riders[_count++] = body;
    }

    // A body that leaves while its owner is carrying leaves its slot empty. The carry's loop then
    // keeps its place.
    internal void Remove(KinematicBody2D body, bool carrying)
    {
        for (int index = 0; index < _count; index++)
        {
            if (!ReferenceEquals(_riders[index], body))
            {
                continue;
            }

            if (carrying)
            {
                _riders[index] = null;
                _emptied = true;
                return;
            }

            _count--;
            _riders[index] = _riders[_count];
            _riders[_count] = null;
            return;
        }
    }

    // Closes the slots a carry emptied, keeping the riders in order.
    internal void Compact()
    {
        if (!_emptied)
        {
            return;
        }

        _emptied = false;
        int kept = 0;
        for (int index = 0; index < _count; index++)
        {
            if (_riders[index] is { } rider)
            {
                _riders[kept++] = rider;
            }
        }

        Array.Clear(_riders, kept, _count - kept);
        _count = kept;
    }

    // Empties a slot and returns the rider it held, or null. The owner lets each rider go this way as
    // it leaves the world, before the rider hears of it.
    internal KinematicBody2D? Take(int index)
    {
        KinematicBody2D? rider = _riders[index];
        _riders[index] = null;

        return rider;
    }

    internal void Clear()
    {
        _count = 0;
        _emptied = false;
    }
}
