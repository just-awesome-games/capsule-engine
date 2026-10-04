using System.ComponentModel;
using System.Globalization;
using Capsule.Tiles;

namespace Capsule.Scenes.Spawning;

/// <summary>Constructs one entity from its spawn data.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate Entity EntitySpawner(EntitySpawn spawn);

/// <summary>Sets the authorable members a placement authors.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate void EntityApplier(Entity entity, AuthoredMembers members);

/// <summary>
/// Maps each type key to the delegate that constructs its entity, fixed once built.
/// </summary>
/// <remarks>
/// A game passes the registry its source generator emits. Build one by hand only in tests.
/// </remarks>
public sealed class EntityRegistry
{
    private readonly Dictionary<string, EntityRegistration> _entities;

    // The tile map of the assembly owning the document, whose asset lookup and tile types its maps read.
    private readonly EntityRegistration? _ownedTileMap;

    /// <summary>A registry over <paramref name="entities"/>.</summary>
    /// <exception cref="ArgumentException">A type key is blank or repeated, or a spawner is null.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public EntityRegistry(IEnumerable<EntityRegistration> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        _entities = new Dictionary<string, EntityRegistration>(StringComparer.Ordinal);
        foreach (EntityRegistration entity in entities)
        {
            (string type, EntitySpawner spawner) = (entity.Type, entity.Spawner);
            if (string.IsNullOrWhiteSpace(type))
            {
                throw new ArgumentException("A type key is blank. Give every registration a type.", nameof(entities));
            }

            if (spawner is null)
            {
                throw new ArgumentException($"The type key '{type}' has no spawner. Supply one.", nameof(entities));
            }

            // Every logic assembly registers the engine's tile map. The first serves a document no assembly owns.
            if (!_entities.TryAdd(type, entity) && !string.Equals(type, TileMap.Key, StringComparison.Ordinal))
            {
                throw new ArgumentException($"The type key '{type}' appears more than once. Register it once.", nameof(entities));
            }
        }
    }

    private EntityRegistry(Dictionary<string, EntityRegistration> entities, EntityRegistration? ownedTileMap)
    {
        _entities = entities;
        _ownedTileMap = ownedTileMap;
    }

    /// <summary>This registry with the tile map that <paramref name="owner"/>, the registrations of the assembly owning a document, declares.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public EntityRegistry OwnedBy(IEnumerable<EntityRegistration> owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        foreach (EntityRegistration entity in owner)
        {
            if (string.Equals(entity.Type, TileMap.Key, StringComparison.Ordinal))
            {
                return new EntityRegistry(_entities, entity);
            }
        }

        return this;
    }

    // Throws SpawnException when no class claims the type, or the claiming class returned null.
    internal Entity Create(EntitySpawn spawn, AuthoredMembers members)
    {
        EntityRegistration registered;
        if (_ownedTileMap is { } owned && string.Equals(spawn.Type, TileMap.Key, StringComparison.Ordinal))
        {
            registered = owned;
        }
        else if (!_entities.TryGetValue(spawn.Type!, out registered))
        {
            throw new SpawnException(
                string.Create(CultureInfo.InvariantCulture, $"type key '{spawn.Type}' at ({spawn.Position.X}, {spawn.Position.Y}) is claimed by no entity. A class claims ")
                + "a type by being a non-abstract Capsule.Scenes.Entity with a public constructor taking one "
                + "Capsule.Scenes.Spawning.EntitySpawn. The type is the key its namespace names unless "
                + "[TypeKey] gives one. A class with a C# required member other than an entity reference is placed in code only. "
                + $"Claimed: {Registered.Names(_entities.Keys)}.");
        }

        // The spawn carries the entry to the base constructor, which applies it before the derived body.
        if (registered.Apply is { } apply)
        {
            spawn = spawn with { Members = members, Apply = apply };
        }

        return registered.Spawner(spawn)
            ?? throw new SpawnException($"the class claiming type key '{spawn.Type}' returned no entity.");
    }
}
