namespace Capsule.Input;

/// <summary>
/// Which keys stand for which actions. Written once at configuration time and read
/// every step thereafter, so reads allocate nothing.
/// </summary>
public sealed class ActionBindings
{
    private readonly Dictionary<InputAction, Key[]> _keysByAction = [];

    /// <summary>
    /// Adds <paramref name="keys"/> to <paramref name="action"/>; any of them then
    /// stands for it. Binding an action twice unions the keys rather than replacing them.
    /// </summary>
    /// <exception cref="ArgumentException">The action is unnamed, or some key is <see cref="Key.None"/>.</exception>
    public ActionBindings Bind(InputAction action, params ReadOnlySpan<Key> keys)
    {
        if (string.IsNullOrWhiteSpace(action.Name))
        {
            throw new ArgumentException("An action must be named.", nameof(action));
        }

        if (keys.IsEmpty)
        {
            throw new ArgumentException("An action must be bound to at least one key.", nameof(keys));
        }

        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == Key.None)
            {
                throw new ArgumentException($"'{action.Name}' cannot be bound to {nameof(Key)}.{nameof(Key.None)}.", nameof(keys));
            }
        }

        List<Key> merged = _keysByAction.TryGetValue(action, out Key[]? existing) ? [.. existing] : [];
        for (int i = 0; i < keys.Length; i++)
        {
            if (!merged.Contains(keys[i]))
            {
                merged.Add(keys[i]);
            }
        }

        _keysByAction[action] = [.. merged];

        return this;
    }

    /// <summary>Keys bound to <paramref name="action"/>; empty when it is unbound.</summary>
    public ReadOnlySpan<Key> KeysFor(InputAction action) =>
        _keysByAction.TryGetValue(action, out Key[]? keys) ? keys : ReadOnlySpan<Key>.Empty;

    /// <summary>Whether any key bound to <paramref name="action"/> is held in <paramref name="snapshot"/>.</summary>
    public bool IsAnyDown(InputAction action, in DeviceSnapshot snapshot)
    {
        if (!_keysByAction.TryGetValue(action, out Key[]? keys))
        {
            return false;
        }

        for (int i = 0; i < keys.Length; i++)
        {
            if (snapshot.IsDown(keys[i]))
            {
                return true;
            }
        }

        return false;
    }
}
