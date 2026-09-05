using System.ComponentModel;
using Capsule.Scenes.Documents;

namespace Capsule.Scenes.Spawning;

/// <summary>Constructs one entity from its spawn data.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate Entity EntitySpawner(EntitySpawn spawn);

/// <summary>
/// What each spawn type constructs, fixed once built. A game passes the registry its source
/// generator emits; hand-building one is the test path.
/// </summary>
public sealed class EntityRegistry
{
    private readonly Dictionary<string, EntityRegistration> _entities;

    /// <exception cref="ArgumentException">A spawn type is blank, reserved or repeated, or a spawner is null.</exception>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public EntityRegistry(IEnumerable<EntityRegistration> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        _entities = new Dictionary<string, EntityRegistration>(StringComparer.Ordinal);
        foreach (EntityRegistration entity in entities)
        {
            (string type, EntitySpawner spawner) = (entity.SpawnType, entity.Spawner);
            if (string.IsNullOrWhiteSpace(type))
            {
                throw new ArgumentException("A spawn type cannot be blank.", nameof(entities));
            }

            if (string.Equals(type, SceneDocument.TileMapType, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The spawn type '{SceneDocument.TileMapType}' is reserved for scene-document tile-map entries, "
                    + "which the engine composes itself; give the class a [SpawnType] of its own.",
                    nameof(entities));
            }

            if (spawner is null)
            {
                throw new ArgumentException($"The spawn type '{type}' has no spawner.", nameof(entities));
            }

            if (!_entities.TryAdd(type, entity))
            {
                throw new ArgumentException($"The spawn type '{type}' appears more than once.", nameof(entities));
            }
        }
    }

    // What a scene spawning the type adds to its set, or null when it adds nothing.
    internal TextureSetBuilder? TexturesFor(string spawnType) =>
        _entities.TryGetValue(spawnType, out EntityRegistration entity) ? entity.Textures : null;

    // Throws SpawnException when no class claims the type, or the one that does returned nothing.
    internal Entity Create(EntitySpawn spawn)
    {
        if (!_entities.TryGetValue(spawn.Type, out EntityRegistration registered))
        {
            throw new SpawnException(
                $"spawn type '{spawn.Type}' (entity id {spawn.Id}) is claimed by no entity. A class claims "
                + "a type by being a non-abstract Capsule.Scenes.Entity with a public constructor taking one "
                + "Capsule.Scenes.Spawning.EntitySpawn; the type is the key its namespace names unless "
                + $"[SpawnType] gives one. Claimed: {KnownTypes()}.");
        }

        return registered.Spawner(spawn)
            ?? throw new SpawnException($"the class claiming spawn type '{spawn.Type}' returned no entity.");
    }

    // Sorted so the message reads the same whatever order the registry was built in.
    private string KnownTypes()
    {
        if (_entities.Count == 0)
        {
            return "nothing";
        }

        string[] types = [.. _entities.Keys];
        Array.Sort(types, StringComparer.Ordinal);

        return string.Join(", ", types);
    }
}
