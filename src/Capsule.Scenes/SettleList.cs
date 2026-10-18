using System.Diagnostics.CodeAnalysis;

namespace Capsule.Scenes;

// A list that can be walked while its callbacks modify it. Entries added during a walk are skipped
// until the next one.
internal sealed class SettleList<T>
    where T : class
{
    private readonly List<T> _items = [];
    private int _next;
    private int _end;

    internal int Count => _items.Count;

    // Callers pair each Add with a Remove. Duplicates cannot build up.
    internal void Add(T item) => _items.Add(item);

    internal void Reserve(int capacity) => _items.EnsureCapacity(capacity);

    internal void Remove(T item)
    {
        for (int index = 0; index < _items.Count; index++)
        {
            if (!ReferenceEquals(_items[index], item))
            {
                continue;
            }

            _items.RemoveAt(index);
            if (index < _next)
            {
                _next--;
                _end--;
            }
            else if (index < _end)
            {
                _end--;
            }

            return;
        }
    }

    internal void Begin()
    {
        _next = 0;
        _end = _items.Count;
    }

    internal bool TryNext([NotNullWhen(true)] out T? item)
    {
        // The list may have been cleared by a callback.
        if (_next >= _end || _next >= _items.Count)
        {
            item = null;
            return false;
        }

        item = _items[_next++];
        return true;
    }

    internal void End()
    {
        _next = 0;
        _end = 0;
    }

    internal void Clear()
    {
        _items.Clear();
        End();
    }
}
