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

    // Callers pair Add with Remove exactly — a registration never repeats without its removal in
    // between — so this appends without checking for a duplicate and stays O(1).
    internal void Add(T item) => _items.Add(item);

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
