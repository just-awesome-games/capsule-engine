using System.Diagnostics.CodeAnalysis;

namespace Capsule.Scenes;

// An entry of one SettleList at a time, which keeps its place there.
internal interface ISettleListEntry
{
    // Its index in the list holding it. Only the list writes it.
    int SettleSlot { get; set; }
}

// A list that can be walked while its callbacks modify it. Entries added during a walk are skipped
// until the next one. A removal leaves a hole that the next walk's start closes, as a scene's entity
// slots do. A step that removes many entries pays for each in constant time, and the rest keep their
// order.
internal sealed class SettleList<T>
    where T : class, ISettleListEntry
{
    private readonly List<T?> _items = [];
    private int _holes;
    private int _next;
    private int _end;
    private bool _walking;

    internal int Count => _items.Count - _holes;

    // Callers pair each Add with a Remove. Duplicates cannot build up. Holes past half the list close
    // here outside a walk. Removals between walks then cannot grow it without bound.
    internal void Add(T item)
    {
        if (!_walking && _holes > _items.Count / 2)
        {
            Compact();
        }

        item.SettleSlot = _items.Count;
        _items.Add(item);
    }

    internal void Reserve(int capacity) => _items.EnsureCapacity(capacity);

    // An item this list does not hold is ignored.
    internal void Remove(T item)
    {
        int slot = item.SettleSlot;
        if ((uint)slot >= (uint)_items.Count || !ReferenceEquals(_items[slot], item))
        {
            return;
        }

        _items[slot] = null;
        _holes++;
    }

    internal void Begin()
    {
        if (_holes > 0)
        {
            Compact();
        }

        _walking = true;
        _next = 0;
        _end = _items.Count;
    }

    internal bool TryNext([NotNullWhen(true)] out T? item)
    {
        // The list may have been cleared by a callback.
        while (_next < _end && _next < _items.Count)
        {
            if (_items[_next++] is { } held)
            {
                item = held;
                return true;
            }
        }

        item = null;
        return false;
    }

    internal void End()
    {
        _walking = false;
        _next = 0;
        _end = 0;
    }

    internal void Clear()
    {
        _items.Clear();
        _holes = 0;
        End();
    }

    // Closes every hole, keeping the entries in order.
    private void Compact()
    {
        int kept = 0;
        for (int index = 0; index < _items.Count; index++)
        {
            if (_items[index] is { } item)
            {
                item.SettleSlot = kept;
                _items[kept++] = item;
            }
        }

        _items.RemoveRange(kept, _items.Count - kept);
        _holes = 0;
    }
}
