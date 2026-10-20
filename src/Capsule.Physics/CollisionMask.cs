namespace Capsule.Physics;

/// <summary>
/// A set of layers named once and usable with any collision world.
/// </summary>
/// <remarks>
/// A mask is immutable and safe to share across threads, and one with no names matches nothing.
/// <see cref="With"/> and <see cref="Without"/> return a new mask rather than change this one. Each
/// world interns its names the first time the mask reaches it. Declare a mask once, as a
/// <c>static readonly</c> field. A mask a query uses keeps a slot in every world it reaches, and a
/// mask built per query grows that table without bound. A mask only colliders and bodies use takes no
/// slot.
/// </remarks>
/// <example>
/// A probe that sweeps an entity's collider sideways against door layers only:
/// <code>
/// private static readonly CollisionMask Doors = new(CollisionLayers.Doors);
///
/// if (_collider.Cast(new Vector2(reach, 0f), Doors, out ShapeCastHit2D hit))
/// {
///     // A door is within reach, hit.Fraction of the way along.
/// }
/// </code>
/// </example>
public sealed class CollisionMask
{
    // Query masks are numbered from zero in the order they first reach a query. The number indexes
    // each world's table of resolved masks. The lock covers masks first queried on several threads.
    private static readonly Lock Numbering = new();
    private static int QueryMasks;

    // What a collider detects and a body is blocked or moved by until the game says otherwise.
    internal static readonly CollisionMask Empty = new();

    private readonly string[] _names;

    private int _id = -1;

    // This mask's slot in each world's table of resolved masks, taken the first time a query needs it.
    internal int Id
    {
        get
        {
            int id = Volatile.Read(ref _id);

            return id >= 0 ? id : TakeId();
        }
    }

    /// <summary>A mask matching every layer in <paramref name="names"/>.</summary>
    /// <param name="names">The layer names to hit. An empty list hits nothing.</param>
    public CollisionMask(params ReadOnlySpan<string> names)
    {
        Validate(names);
        _names = names.ToArray();
    }

    // Takes ownership of names, which the caller has validated.
    private CollisionMask(string[] names) => _names = names;

    // The layer names, in the order given.
    internal ReadOnlySpan<string> Names => _names;

    /// <summary>A mask matching this mask's layers and every layer in <paramref name="names"/>.</summary>
    /// <remarks>
    /// It returns this mask when it already holds every name and allocates a new one otherwise.
    /// Re-applying a layer allocates nothing after the first time. Compose at setup, never in a
    /// per-step query, where each new mask takes a slot in every world it reaches.
    /// </remarks>
    /// <example>
    /// A component that adds a layer to a collider its owner configured, keeping what the owner set:
    /// <code>
    /// collider.Detects = collider.Detects.With(CollisionLayers.Wind);
    /// </code>
    /// </example>
    /// <param name="names">The layer names to add.</param>
    public CollisionMask With(params ReadOnlySpan<string> names)
    {
        Validate(names);

        int added = 0;
        for (int index = 0; index < names.Length; index++)
        {
            if (IsNew(names, index))
            {
                added++;
            }
        }

        if (added == 0)
        {
            return this;
        }

        string[] combined = new string[_names.Length + added];
        _names.CopyTo(combined, 0);
        int next = _names.Length;
        for (int index = 0; index < names.Length; index++)
        {
            if (IsNew(names, index))
            {
                combined[next++] = names[index];
            }
        }

        return new CollisionMask(combined);
    }

    /// <summary>A mask matching this mask's layers except every layer in <paramref name="names"/>.</summary>
    /// <remarks>
    /// It returns this mask when it holds none of the names and allocates a new one otherwise. Compose
    /// at setup, never in a per-step query.
    /// </remarks>
    /// <param name="names">The layer names to remove.</param>
    public CollisionMask Without(params ReadOnlySpan<string> names)
    {
        Validate(names);

        int kept = 0;
        foreach (string name in _names)
        {
            if (!names.Contains(name))
            {
                kept++;
            }
        }

        if (kept == _names.Length)
        {
            return this;
        }

        string[] remaining = new string[kept];
        int next = 0;
        foreach (string name in _names)
        {
            if (!names.Contains(name))
            {
                remaining[next++] = name;
            }
        }

        return new CollisionMask(remaining);
    }

    /// <summary>Whether this mask matches the layer <paramref name="name"/>.</summary>
    public bool Contains(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _names.AsSpan().Contains(name);
    }

    private static void Validate(ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(names));
        }
    }

    // Whether names[index] is in neither this mask nor earlier in names. With adds each name once.
    private bool IsNew(ReadOnlySpan<string> names, int index) =>
        !_names.AsSpan().Contains(names[index]) && !names[..index].Contains(names[index]);

    private int TakeId()
    {
        lock (Numbering)
        {
            if (_id < 0)
            {
                Volatile.Write(ref _id, QueryMasks++);
            }

            return _id;
        }
    }
}
