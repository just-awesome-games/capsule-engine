namespace Capsule.Scenes;

/// <summary>Constructs one scene from the map that backs it.</summary>
public delegate Scene MapSceneFactory(MapSceneContext context);

/// <summary>Constructs one scene that no map backs.</summary>
public delegate Scene SceneFactory();

/// <summary>
/// One scene as a <see cref="SceneRegistry"/> entry: the class, what constructs it, and the map
/// backing it when one does. Built through <see cref="MapBacked"/> or <see cref="Plain"/>, so the
/// two kinds cannot be mixed.
/// </summary>
public readonly record struct SceneRegistration
{
    private readonly MapSceneFactory? _mapScene;
    private readonly SceneFactory? _scene;

    private SceneRegistration(Type sceneType, string? mapName, MapSceneFactory? mapScene, SceneFactory? scene)
    {
        SceneType = sceneType;
        MapName = mapName;
        _mapScene = mapScene;
        _scene = scene;
    }

    /// <summary>The class registered.</summary>
    public Type SceneType { get; }

    /// <summary>The map backing it, or null when none does.</summary>
    public string? MapName { get; }

    /// <summary>A scene composed from the map named.</summary>
    public static SceneRegistration MapBacked(Type sceneType, string mapName, MapSceneFactory factory)
    {
        ArgumentNullException.ThrowIfNull(sceneType);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        ArgumentNullException.ThrowIfNull(factory);

        return new SceneRegistration(sceneType, mapName, factory, null);
    }

    /// <summary>A scene no map backs.</summary>
    public static SceneRegistration Plain(Type sceneType, SceneFactory factory)
    {
        ArgumentNullException.ThrowIfNull(sceneType);
        ArgumentNullException.ThrowIfNull(factory);

        return new SceneRegistration(sceneType, null, null, factory);
    }

    // Both reached only through SceneRegistry, which has already read MapName to tell the kinds
    // apart, so the factory for that kind is the one that is present.
    internal Scene Create(MapSceneContext context) => _mapScene!(context);

    internal Scene Create() => _scene!();
}
