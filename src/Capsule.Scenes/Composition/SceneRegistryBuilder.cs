using System.ComponentModel;
using System.Reflection;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes;

/// <summary>Gathers what each logic assembly contributes into one <see cref="SceneRegistry"/>.</summary>
/// <remarks>
/// A shell's <c>CapsuleBoot</c> and a logic assembly's <c>CapsuleScenes.Registry</c> both build through it. A test
/// composing through <c>CapsuleScenes.Registry</c> then composes what a run does.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SceneRegistryBuilder
{
    private readonly List<EntityRegistration> _entities = [];
    private readonly Dictionary<Assembly, EntityRegistration[]> _owners = [];
    private readonly List<SceneRegistration> _scenes = [];
    private readonly List<KeyValuePair<Type, SceneApplier>> _appliers = [];

    /// <summary>Adds the entity registrations of <paramref name="owner"/>, whose scene classes compose with its tile map.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddEntities(Assembly owner, IEnumerable<EntityRegistration> entities)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(entities);
        EntityRegistration[] registered = [.. entities];
        _owners[owner] = registered;
        _entities.AddRange(registered);
    }

    /// <summary>Adds one assembly's scene registrations.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddScenes(IEnumerable<SceneRegistration> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        _scenes.AddRange(scenes);
    }

    /// <summary>Adds the applier of each scene class one assembly declares with authorable members.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddAppliers(IEnumerable<KeyValuePair<Type, SceneApplier>> appliers)
    {
        ArgumentNullException.ThrowIfNull(appliers);
        _appliers.AddRange(appliers);
    }

    /// <summary>The registry over everything added.</summary>
    /// <exception cref="ArgumentException">Two assemblies register the same spawn type, scene class, scene document or applier.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SceneRegistry Build()
    {
        EntityRegistry entities = new(_entities);

        return new(entities, _scenes, _appliers)
        {
            Owned = _owners.ToDictionary(static owner => owner.Key, owner => entities.OwnedBy(owner.Value)),
        };
    }
}
