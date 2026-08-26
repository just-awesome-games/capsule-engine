using Capsule.Maps;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes;

/// <summary>
/// The scenes one assembly declares, indexed both by class and by the map backing one, fixed once
/// built. A game passes the registry its source generator emits; hand-building one is the test
/// path.
/// </summary>
public sealed class SceneRegistry
{
    private readonly Dictionary<Type, SceneRegistration> _byType = [];
    private readonly Dictionary<string, SceneRegistration> _byMapName = new(StringComparer.Ordinal);
    private readonly EntityRegistry _entities;

    /// <exception cref="ArgumentException">A registration names no class, or a class or a map is registered twice.</exception>
    public SceneRegistry(EntityRegistry entities, IEnumerable<SceneRegistration> scenes)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(scenes);

        _entities = entities;
        foreach (SceneRegistration registration in scenes)
        {
            if (registration.SceneType is null)
            {
                throw new ArgumentException("A scene registration must name the class it registers.", nameof(scenes));
            }

            if (!_byType.TryAdd(registration.SceneType, registration))
            {
                throw new ArgumentException($"The scene '{registration.SceneType}' is registered more than once.", nameof(scenes));
            }

            if (registration.MapName is { } mapName && !_byMapName.TryAdd(mapName, registration))
            {
                throw new ArgumentException($"The map '{mapName}' backs more than one scene.", nameof(scenes));
            }
        }
    }

    /// <summary>The map backing <paramref name="sceneType"/>, or null when no map does.</summary>
    /// <exception cref="InvalidOperationException">Nothing registers that class.</exception>
    public string? MapNameOf(Type sceneType) => Registered(sceneType).MapName;

    /// <summary>Builds the scene registered for <paramref name="sceneType"/>.</summary>
    /// <exception cref="InvalidOperationException">Nothing registers that class, or a map backs it.</exception>
    public Scene Create(Type sceneType)
    {
        SceneRegistration registration = Registered(sceneType);
        if (registration.MapName is { } mapName)
        {
            throw new InvalidOperationException(
                $"The scene '{sceneType}' is composed from map '{mapName}'; it can only be built with that map loaded.");
        }

        return registration.Create();
    }

    /// <summary>
    /// Builds the scene <paramref name="mapName"/> is composed into: the class claiming that name,
    /// or a plain <see cref="MapScene"/> when no class claims it.
    /// </summary>
    /// <exception cref="SpawnException">A map object's spawn type is claimed by no entity.</exception>
    public Scene CreateForMap(string mapName, Map map)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        ArgumentNullException.ThrowIfNull(map);

        MapSceneContext context = new(map, _entities);

        return _byMapName.TryGetValue(mapName, out SceneRegistration claimed)
            ? claimed.Create(context)
            : new MapScene(context);
    }

    private SceneRegistration Registered(Type sceneType)
    {
        ArgumentNullException.ThrowIfNull(sceneType);

        if (!_byType.TryGetValue(sceneType, out SceneRegistration registration))
        {
            throw new InvalidOperationException(
                $"No scene is registered for '{sceneType}'. A class is registered by being a non-abstract "
                + "Capsule.Scenes.Scene with a public constructor taking one Capsule.Scenes.MapSceneContext — "
                + "which composes it from the map named after the class — or one taking nothing. "
                + $"Registered: {RegisteredTypes()}.");
        }

        return registration;
    }

    // Sorted so the message reads the same whatever order the registry was built in.
    private string RegisteredTypes()
    {
        if (_byType.Count == 0)
        {
            return "nothing";
        }

        string[] names = new string[_byType.Count];
        int next = 0;
        foreach (Type sceneType in _byType.Keys)
        {
            names[next++] = sceneType.ToString();
        }

        Array.Sort(names, StringComparer.Ordinal);

        return string.Join(", ", names);
    }
}
