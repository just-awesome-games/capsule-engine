using System.Collections.Immutable;

namespace Capsule.Generators;

/// <summary>An array compared by its elements, so a model holding one caches between generator runs.</summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>
{
    private readonly ImmutableArray<T> _items = items;

    internal ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public bool Equals(EquatableArray<T> other)
    {
        ImmutableArray<T> left = Items;
        ImmutableArray<T> right = other.Items;
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode() => Items.Length;
}
