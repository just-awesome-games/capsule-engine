namespace Capsule.Scenes.Spawning;

/// <summary>Constructs one entity from its level data.</summary>
public delegate Entity EntitySpawner(EntitySpawn spawn);

/// <summary>
/// What each level entity <c>type</c> constructs, fixed once built. A game passes the registry
/// its source generator emits from the assembly's <c>[LevelType]</c> classes; hand-building one
/// is the test path.
/// </summary>
public sealed class LevelTypeRegistry
{
    private readonly Dictionary<string, EntitySpawner> _spawners;

    /// <exception cref="ArgumentException">A level type is blank or repeated, or a spawner is null.</exception>
    public LevelTypeRegistry(IEnumerable<KeyValuePair<string, EntitySpawner>> levelTypes)
    {
        ArgumentNullException.ThrowIfNull(levelTypes);

        _spawners = new Dictionary<string, EntitySpawner>(StringComparer.Ordinal);
        foreach ((string id, EntitySpawner spawner) in levelTypes)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A level type cannot be blank.", nameof(levelTypes));
            }

            if (spawner is null)
            {
                throw new ArgumentException($"The level type '{id}' has no spawner.", nameof(levelTypes));
            }

            if (!_spawners.TryAdd(id, spawner))
            {
                throw new ArgumentException($"The level type '{id}' appears more than once.", nameof(levelTypes));
            }
        }
    }

    /// <exception cref="SpawnException">No class claims the type, or the one that does returned nothing.</exception>
    internal Entity Create(EntitySpawn spawn)
    {
        if (!_spawners.TryGetValue(spawn.Type, out EntitySpawner? spawner))
        {
            throw new SpawnException(
                $"level entity type '{spawn.Type}' matches no [LevelType] class (level entity id {spawn.Id}). Declared: {KnownIds()}.");
        }

        return spawner(spawn)
            ?? throw new SpawnException($"the [LevelType] class for '{spawn.Type}' returned no entity.");
    }

    // Sorted so the message reads the same whatever order the registry was built in.
    private string KnownIds()
    {
        if (_spawners.Count == 0)
        {
            return "nothing";
        }

        string[] ids = [.. _spawners.Keys];
        Array.Sort(ids, StringComparer.Ordinal);

        return string.Join(", ", ids);
    }
}
