using System.ComponentModel;
using Capsule.Scenes.Documents;

namespace Capsule.Scenes.Spawning;

/// <summary>Constructs one entity from its spawn data.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate Entity EntitySpawner(EntitySpawn spawn);

/// <summary>
/// Maps each spawn type to what it constructs, and is fixed once built. A game passes the registry its source
/// generator emits, and hand-building one is for tests.
/// </summary>
public sealed class EntityRegistry
{
    private readonly Dictionary<string, EntityRegistration> _entities;

    /// <exception cref="ArgumentException">A spawn type is blank, reserved or repeated, or a spawner is null.</exception>
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
                throw new ArgumentException("A spawn type is blank; give every registration a type.", nameof(entities));
            }

            if (string.Equals(type, SceneDocument.TileMapType, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The spawn type '{SceneDocument.TileMapType}' is reserved for scene-document tile-map entries, "
                    + "which the engine composes itself; give the class its own [SpawnType].",
                    nameof(entities));
            }

            if (spawner is null)
            {
                throw new ArgumentException($"The spawn type '{type}' has no spawner; supply one.", nameof(entities));
            }

            if (!_entities.TryAdd(type, entity))
            {
                throw new ArgumentException($"The spawn type '{type}' appears more than once; register it once.", nameof(entities));
            }
        }
    }

    // Throws SpawnException when no class claims the type, or the claiming class returned null.
    internal Entity Create(EntitySpawn spawn)
    {
        if (!_entities.TryGetValue(spawn.Type, out EntityRegistration registered))
        {
            throw new SpawnException(
                $"spawn type '{spawn.Type}' (entity id {spawn.Id}) is claimed by no entity. A class claims "
                + "a type by being a non-abstract Capsule.Scenes.Entity with a public constructor taking one "
                + "Capsule.Scenes.Spawning.EntitySpawn. The type is the key its namespace names unless "
                + $"[SpawnType] gives one. Claimed: {KnownTypes()}.");
        }

        return registered.Spawner(spawn)
            ?? throw new SpawnException($"the class claiming spawn type '{spawn.Type}' returned no entity.");
    }

    private string KnownTypes() => Registered.Names(_entities.Keys);
}
