using System.Diagnostics.CodeAnalysis;

namespace Capsule.Scenes.Lifecycle;

// A reference-identity list whose callbacks may remove or append entries during traversal.
// Only surviving entries in the captured [_next, _end) slice receive a turn; additions wait.
internal sealed class SettledList<T>
    where T : class
{
    private readonly List<T> _items = [];
    private int _next;
    private int _end;

    internal void Add(T item)
    {
        foreach (T held in _items)
        {
            if (ReferenceEquals(held, item))
            {
                return;
            }
        }

        _items.Add(item);
    }

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

    internal bool TryTake([NotNullWhen(true)] out T? item)
    {
        // A callback may have cleared the list outright.
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

    // Retain an empty slice at the end while deferred arrivals attach. Removals move this
    // mark too; Resume then visits only the entries added since Park.
    internal void Park() => _next = _end = _items.Count;

    internal void Resume() => _end = _items.Count;

    internal void Clear()
    {
        _items.Clear();
        End();
    }
}
