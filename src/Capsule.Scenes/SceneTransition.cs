namespace Capsule.Scenes;

/// <summary>The operation a scene asks its host to perform after the current step.</summary>
public enum SceneTransitionKind
{
    /// <summary>Shut the host down once the current step finishes.</summary>
    Exit,

    /// <summary>Reconstruct the current scene from the target that opened it.</summary>
    Restart,

    /// <summary>Replace the current scene with one named by class.</summary>
    Scene,

    /// <summary>Replace the current scene with the one a named scene document composes into.</summary>
    Named,
}

/// <summary>
/// A deferred scene operation, exposed to a host by
/// <see cref="SceneSimulation.TryTakeTransition"/> after the step that requested it has finished.
/// </summary>
public readonly record struct SceneTransition
{
    private SceneTransition(
        SceneTransitionKind kind,
        Type? sceneType,
        string? documentName,
        object? payload,
        bool hasPayload)
    {
        Kind = kind;
        SceneType = sceneType;
        DocumentName = documentName;
        Payload = payload;
        HasPayload = hasPayload;
    }

    /// <summary>Which operation the host is being asked to perform.</summary>
    public SceneTransitionKind Kind { get; }

    /// <summary>The requested scene class when <see cref="Kind"/> is <see cref="SceneTransitionKind.Scene"/>.</summary>
    public Type? SceneType { get; }

    /// <summary>The requested document name when <see cref="Kind"/> is <see cref="SceneTransitionKind.Named"/>.</summary>
    public string? DocumentName { get; }

    /// <summary>State offered to the next scene, including null when <see cref="HasPayload"/> is true.</summary>
    public object? Payload { get; }

    /// <summary>
    /// Whether a restart replaces the payload that opened the current scene; every other kind
    /// always carries its payload.
    /// </summary>
    public bool HasPayload { get; }

    internal static SceneTransition Exit() =>
        new(SceneTransitionKind.Exit, null, null, null, false);

    internal static SceneTransition Restart(object? payload, bool hasPayload) =>
        new(SceneTransitionKind.Restart, null, null, payload, hasPayload);

    internal static SceneTransition ToScene(Type sceneType, object? payload) =>
        new(SceneTransitionKind.Scene, sceneType, null, payload, true);

    internal static SceneTransition ToName(string documentName, object? payload) =>
        new(SceneTransitionKind.Named, null, documentName, payload, true);

    internal SceneTransition WithPayload(object? payload) =>
        new(Kind, SceneType, DocumentName, payload, true);
}
