using System.ComponentModel;

namespace Capsule.Scenes;

/// <summary>
/// One <see cref="SceneRegistry"/> entry: a scene class, the document backing a scene, or both, with the
/// factory that constructs the scene. Build one through <see cref="FromDocument"/>, <see cref="Plain"/>
/// or <see cref="DocumentOnly"/>.
/// </summary>
public readonly record struct SceneRegistration
{
    private readonly Func<SceneContent?, Scene> _factory;

    private SceneRegistration(Type? sceneType, string? documentName, Func<SceneContent?, Scene> factory)
    {
        SceneType = sceneType;
        DocumentName = documentName;
        _factory = factory;
    }

    /// <summary>The class registered, or null for a registration identified by its document alone.</summary>
    public Type? SceneType { get; }

    /// <summary>The scene document backing it, or null when none does.</summary>
    public string? DocumentName { get; }

    // The label a reader sees: the class name, or the document name when no class claims it.
    internal string Name => SceneType?.Name ?? DocumentName!;

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

    /// <summary>Registers a scene composed from a shipped document that no class claims.</summary>
    /// <param name="name">The document's key.</param>
    /// <param name="factory">The factory that constructs it. The content is never null for this kind.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SceneRegistration DocumentOnly(string name, Func<SceneContent?, Scene> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        return new SceneRegistration(null, name, factory);
    }

    internal Scene Create(SceneContent? content = null) => _factory(content);
}
