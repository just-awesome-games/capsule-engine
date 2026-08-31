using Capsule.Scenes.Documents;

namespace Capsule.Scenes.Spawning;

/// <summary>Constructs one entity from its spawn data.</summary>
public delegate Entity EntitySpawner(EntitySpawn spawn);

/// <summary>
/// What each spawn type constructs, fixed once built. A game passes the registry its source
/// generator emits from the assembly's spawnable entities; hand-building one is the test path.
/// </summary>
public sealed class EntityRegistry
{
    private readonly Dictionary<string, EntitySpawner> _spawners;

    /// <exception cref="ArgumentException">A spawn type is blank, reserved or repeated, or a spawner is null.</exception>
    public EntityRegistry(IEnumerable<KeyValuePair<string, EntitySpawner>> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        _spawners = new Dictionary<string, EntitySpawner>(StringComparer.Ordinal);
        foreach ((string type, EntitySpawner spawner) in entities)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                throw new ArgumentException("A spawn type cannot be blank.", nameof(entities));
            }

            if (string.Equals(type, SceneDocument.TileMapType, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The spawn type '{SceneDocument.TileMapType}' is reserved for a scene document's terrain entry, "
                    + "which the engine composes itself; give the class a [SpawnType] of its own.",
                    nameof(entities));
            }

            if (spawner is null)
            {
                throw new ArgumentException($"The spawn type '{type}' has no spawner.", nameof(entities));
            }

            if (!_spawners.TryAdd(type, spawner))
            {
                throw new ArgumentException($"The spawn type '{type}' appears more than once.", nameof(entities));
            }
        }
    }

    /// <exception cref="SpawnException">No class claims the type, or the one that does returned nothing.</exception>
    internal Entity Create(EntitySpawn spawn)
    {
        if (!_spawners.TryGetValue(spawn.Type, out EntitySpawner? spawner))
        {
            throw new SpawnException(
                $"spawn type '{spawn.Type}' (entity id {spawn.Id}) is claimed by no entity. A class claims "
                + "a type by being a non-abstract Capsule.Scenes.Entity with a public constructor taking one "
                + "Capsule.Scenes.Spawning.EntitySpawn; the type is its kebab-cased class name unless "
                + $"[SpawnType] gives one. Claimed: {KnownTypes()}.");
        }

        return spawner(spawn)
            ?? throw new SpawnException($"the class claiming spawn type '{spawn.Type}' returned no entity.");
    }

    // Sorted so the message reads the same whatever order the registry was built in.
    private string KnownTypes()
    {
        if (_spawners.Count == 0)
        {
            return "nothing";
        }

        string[] types = [.. _spawners.Keys];
        Array.Sort(types, StringComparer.Ordinal);

        return string.Join(", ", types);
    }
}
