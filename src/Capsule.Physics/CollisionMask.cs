namespace Capsule.Physics;

/// <summary>
/// Which layers a query may hit, held by name and usable with any collision world.
/// </summary>
/// <remarks>
/// A game builds a mask once, typically as a <c>static readonly</c> field, and passes it wherever a
/// query takes a <see cref="CollisionFilter"/>. Each world resolves the mask to one of its own
/// filters the first time the mask reaches it and reuses that filter afterwards. Resolving interns
/// a name the world has not seen, as <c>Collider2D.SetFilter</c> does. A mask with no names hits
/// nothing.
/// <para>
/// A mask is immutable and safe to share across threads. Each mask keeps a slot in every world it
/// reaches, so a game builds its masks once rather than per query.
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
    // Masks are numbered from zero. The number indexes each world's table of resolved masks. The
    // interlock covers masks built on several threads, such as static initializers in test hosts.
    private static int MasksCreated;

    private readonly string[] _names;

    // This mask's slot in each world's table of resolved masks.
    internal int Id { get; }

    /// <summary>A mask matching every layer in <paramref name="names"/>.</summary>
    /// <param name="names">The layer names to hit. An empty list hits nothing.</param>
    public CollisionMask(params ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(names));
        }

        _names = names.ToArray();
        Id = Interlocked.Increment(ref MasksCreated) - 1;
    }

    // The layer names, in the order given.
    internal ReadOnlySpan<string> Names => _names;
}
