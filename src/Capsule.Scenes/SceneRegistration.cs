using System.ComponentModel;

namespace Capsule.Scenes;

/// <summary>Constructs one scene from the scene document that backs it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate Scene DocumentSceneFactory(SceneContent content);

/// <summary>Constructs one scene that no document backs.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate Scene SceneFactory();

/// <summary>
/// One scene as a <see cref="SceneRegistry"/> entry: the class, what constructs it, and the
/// document backing it when one does. Built through <see cref="FromDocument"/> or
/// <see cref="Plain"/>.
/// </summary>
public readonly record struct SceneRegistration
{
    private readonly DocumentSceneFactory? _fromDocument;
    private readonly SceneFactory? _plain;

    private SceneRegistration(
        Type sceneType,
        string? documentName,
        DocumentSceneFactory? fromDocument,
        SceneFactory? plain,
        TextureSetBuilder? textures)
    {
        SceneType = sceneType;
        DocumentName = documentName;
        Textures = textures;
        _fromDocument = fromDocument;
        _plain = plain;
    }

    /// <summary>The class registered.</summary>
    public Type SceneType { get; }

    /// <summary>The scene document backing it, or null when none does.</summary>
    public string? DocumentName { get; }

    /// <summary>
    /// The residency groups the build derived from the code this class reaches, or null when it
    /// reaches none. What its document places is added on top, at composition.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TextureSetBuilder? Textures { get; }

    /// <summary>A scene composed from the scene document named.</summary>
    /// <param name="sceneType">The class registered.</param>
    /// <param name="name">The scene document backing it.</param>
    /// <param name="factory">What constructs it from that document's content.</param>
    /// <param name="textures">The residency groups its own code reaches; null when it reaches none.</param>
    /// <exception cref="ArgumentNullException">The class or the factory is null.</exception>
    /// <exception cref="ArgumentException">The document name is blank.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SceneRegistration FromDocument(
        Type sceneType,
        string name,
        DocumentSceneFactory factory,
        TextureSetBuilder? textures = null)
    {
        ArgumentNullException.ThrowIfNull(sceneType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        return new SceneRegistration(sceneType, name, factory, null, textures);
    }

    /// <summary>A scene no document backs.</summary>
    /// <param name="sceneType">The class registered.</param>
    /// <param name="factory">What constructs it.</param>
    /// <param name="textures">The residency groups its own code reaches; null when it reaches none.</param>
    /// <exception cref="ArgumentNullException">The class or the factory is null.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SceneRegistration Plain(Type sceneType, SceneFactory factory, TextureSetBuilder? textures = null)
    {
        ArgumentNullException.ThrowIfNull(sceneType);
        ArgumentNullException.ThrowIfNull(factory);

        return new SceneRegistration(sceneType, null, null, factory, textures);
    }

    // Reached only through SceneRegistry, which has already read DocumentName to tell the kinds
    // apart, so the factory for that kind is the one present.
    internal Scene Create(SceneContent content) => _fromDocument!(content);

    internal Scene Create() => _plain!();
}
