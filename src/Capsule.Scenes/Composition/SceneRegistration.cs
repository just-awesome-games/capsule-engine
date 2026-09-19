using System.ComponentModel;

namespace Capsule.Scenes;

/// <summary>
/// One <see cref="SceneRegistry"/> entry: the scene class, the factory that constructs it, and the document
/// backing it when one does. Build one through <see cref="FromDocument"/> or <see cref="Plain"/>.
/// </summary>
public readonly record struct SceneRegistration
{
    private readonly Func<SceneContent?, Scene> _factory;

    private SceneRegistration(Type sceneType, string? documentName, Func<SceneContent?, Scene> factory)
    {
        SceneType = sceneType;
        DocumentName = documentName;
        _factory = factory;
    }

    /// <summary>The class registered.</summary>
    public Type SceneType { get; }

    /// <summary>The scene document backing it, or null when none does.</summary>
    public string? DocumentName { get; }

    /// <summary>Registers a scene composed from the named scene document.</summary>
    /// <param name="sceneType">The class registered.</param>
    /// <param name="name">The scene document backing it.</param>
    /// <param name="factory">The factory that constructs it. The content is never null for this kind.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SceneRegistration FromDocument(Type sceneType, string name, Func<SceneContent?, Scene> factory)
    {
        ArgumentNullException.ThrowIfNull(sceneType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        return new SceneRegistration(sceneType, name, factory);
    }

    /// <summary>Registers a scene that no document backs.</summary>
    /// <param name="sceneType">The class registered.</param>
    /// <param name="factory">The factory that constructs it. The content is always null for this kind.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SceneRegistration Plain(Type sceneType, Func<SceneContent?, Scene> factory)
    {
        ArgumentNullException.ThrowIfNull(sceneType);
        ArgumentNullException.ThrowIfNull(factory);

        return new SceneRegistration(sceneType, null, factory);
    }

    internal Scene Create(SceneContent? content = null) => _factory(content);
}
