namespace Capsule.Scenes;

/// <summary>Constructs one scene from the scene document that backs it.</summary>
public delegate Scene DocumentSceneFactory(SceneContent content);

/// <summary>Constructs one scene that no document backs.</summary>
public delegate Scene SceneFactory();

/// <summary>
/// One scene as a <see cref="SceneRegistry"/> entry: the class, what constructs it, and the
/// document backing it when one does. Built through <see cref="FromDocument"/> or
/// <see cref="Plain"/>, so the two kinds cannot be mixed.
/// </summary>
public readonly record struct SceneRegistration
{
    private readonly DocumentSceneFactory? _fromDocument;
    private readonly SceneFactory? _plain;

    private SceneRegistration(
        Type sceneType,
        string? documentName,
        DocumentSceneFactory? fromDocument,
        SceneFactory? plain)
    {
        SceneType = sceneType;
        DocumentName = documentName;
        _fromDocument = fromDocument;
        _plain = plain;
    }

    /// <summary>The class registered.</summary>
    public Type SceneType { get; }

    /// <summary>The scene document backing it, or null when none does.</summary>
    public string? DocumentName { get; }

    /// <summary>A scene composed from the scene document named.</summary>
    /// <exception cref="ArgumentNullException">The class or the factory is null.</exception>
    /// <exception cref="ArgumentException">The document name is blank.</exception>
    public static SceneRegistration FromDocument(Type sceneType, string name, DocumentSceneFactory factory)
    {
        ArgumentNullException.ThrowIfNull(sceneType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        return new SceneRegistration(sceneType, name, factory, null);
    }

    /// <summary>A scene no document backs.</summary>
    /// <exception cref="ArgumentNullException">The class or the factory is null.</exception>
    public static SceneRegistration Plain(Type sceneType, SceneFactory factory)
    {
        ArgumentNullException.ThrowIfNull(sceneType);
        ArgumentNullException.ThrowIfNull(factory);

        return new SceneRegistration(sceneType, null, null, factory);
    }

    // Both reached only through SceneRegistry, which has already read DocumentName to tell the
    // kinds apart, so the factory for that kind is the one that is present.
    internal Scene Create(SceneContent content) => _fromDocument!(content);

    internal Scene Create() => _plain!();
}
