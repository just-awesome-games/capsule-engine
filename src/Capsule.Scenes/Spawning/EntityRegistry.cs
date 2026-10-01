using System.ComponentModel;
using Capsule.Scenes.Documents;

namespace Capsule.Scenes.Spawning;

/// <summary>Constructs one entity from its spawn data.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate Entity EntitySpawner(EntitySpawn spawn);

/// <summary>Sets the authorable members a placement authors.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate void EntityApplier(Entity entity, AuthoredProperties properties);

/// <summary>
/// Maps each spawn type to the delegate that constructs its entity, fixed once built.
/// </summary>
/// <remarks>
/// A game passes the registry its source generator emits. Build one by hand only in tests.
/// </remarks>
public sealed class EntityRegistry
{
    private readonly Dictionary<string, EntityRegistration> _entities;

    /// <summary>A registry over <paramref name="entities"/>.</summary>
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
                throw new ArgumentException("A spawn type is blank. Give every registration a type.", nameof(entities));
            }

            if (string.Equals(type, SceneDocument.TileMapType, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The spawn type '{SceneDocument.TileMapType}' is reserved for scene-document tile-map entries, "
                    + "which the engine composes itself. Give the class its own [SpawnType].",
                    nameof(entities));
            }

            if (spawner is null)
            {
                throw new ArgumentException($"The spawn type '{type}' has no spawner. Supply one.", nameof(entities));
            }

            if (!_entities.TryAdd(type, entity))
            {
                throw new ArgumentException($"The spawn type '{type}' appears more than once. Register it once.", nameof(entities));
            }
        }
    }

    // Throws SpawnException when no class claims the type, or the claiming class returned null.
    internal Entity Create(EntitySpawn spawn, AuthoredProperties properties)
    {
        if (!_entities.TryGetValue(spawn.Type!, out EntityRegistration registered))
        {
            throw new SpawnException(
                $"spawn type '{spawn.Type}' (entity id {spawn.Id}) is claimed by no entity. A class claims "
                + "a type by being a non-abstract Capsule.Scenes.Entity with a public constructor taking one "
                + "Capsule.Scenes.Spawning.EntitySpawn. The type is the key its namespace names unless "
                + "[SpawnType] gives one. A class with a C# required member other than an entity reference is placed in code only. "
                + $"Claimed: {Registered.Names(_entities.Keys)}.");
        }

        // The spawn carries the entry to the base constructor, which applies it before the derived body.
        if (registered.Apply is { } apply)
        {
            spawn = spawn with { Properties = properties, Apply = apply };
        }

        return registered.Spawner(spawn)
            ?? throw new SpawnException($"the class claiming spawn type '{spawn.Type}' returned no entity.");
    }

    // The delegate setting the entity references of a class claiming the type, or null when it holds none.
    internal EntityApplier? Link(string type) =>
        _entities.TryGetValue(type, out EntityRegistration registered) ? registered.Link : null;
}
