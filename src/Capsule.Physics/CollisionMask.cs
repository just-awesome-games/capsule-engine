namespace Capsule.Physics;

/// <summary>
/// A set of layers named once and usable with any collision world.
/// </summary>
/// <remarks>
/// A mask is immutable and safe to share across threads, and one with no names matches nothing. Each
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
        foreach (string name in names)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(names));
        }

        _names = names.ToArray();
    }

    // The layer names, in the order given.
    internal ReadOnlySpan<string> Names => _names;

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
