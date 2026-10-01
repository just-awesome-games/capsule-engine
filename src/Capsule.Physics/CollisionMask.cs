namespace Capsule.Physics;

/// <summary>
/// A set of layers named once and usable with any collision world.
/// </summary>
/// <remarks>
/// A game declares its masks as <c>static readonly</c> fields beside its layer names. A mask sets
/// what a collider detects and what blocks and moves a body, and it filters any query. Each world
/// resolves a mask the first time it reaches the world, interning names the world has not seen. A
/// mask with no names matches nothing.
/// <para>
/// A mask is immutable and safe to share across threads. A query keeps a slot for its mask in the
/// world, so a mask built per query grows the world's table without bound. A mask only colliders and
/// bodies use takes no slot.
/// </para>
/// </remarks>
/// <example>
/// A wall probe that sweeps the player's collider against climbable layers only:
/// <code>
/// private static readonly CollisionMask Climbable = new(CollisionLayers.Climbable);
///
/// if (_collider.Cast(new Vector2(reach, 0f), Climbable, out ShapeCastHit2D hit))
/// {
///     // Kick off hit.Normal.
/// }
/// </code>
/// </example>
public sealed class CollisionMask
{
    // Query masks are numbered from zero in the order they first reach a query. The number indexes
    // each world's table of resolved masks, so a mask only colliders and bodies use takes no slot.
    // The lock covers masks first queried on several threads.
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
