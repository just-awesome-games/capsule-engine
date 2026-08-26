namespace Capsule.Scenes;

/// <summary>The operation a scene asks its host to perform after the current step.</summary>
public enum SceneTransitionKind
{
    Exit,
    Restart,
    Scene,
    Map,
}

/// <summary>
/// A deferred scene operation. <see cref="SceneSimulation.TryTakeTransition"/> exposes it to a
/// host after the step that requested it has finished.
/// </summary>
public readonly record struct SceneTransition
{
    private SceneTransition(
        SceneTransitionKind kind,
        Type? sceneType,
        string? mapName,
        object? payload,
        bool hasPayload)
    {
        Kind = kind;
        SceneType = sceneType;
        MapName = mapName;
        Payload = payload;
        HasPayload = hasPayload;
    }

    public SceneTransitionKind Kind { get; }

    /// <summary>The requested scene class when <see cref="Kind"/> is <see cref="SceneTransitionKind.Scene"/>.</summary>
    public Type? SceneType { get; }

    /// <summary>The requested map name when <see cref="Kind"/> is <see cref="SceneTransitionKind.Map"/>.</summary>
    public string? MapName { get; }

    /// <summary>State offered to the next scene, including null when <see cref="HasPayload"/> is true.</summary>
    public object? Payload { get; }

    /// <summary>
    /// Whether a restart replaces the payload that opened the current scene. Other transition
    /// kinds always carry their payload, which is null when none was supplied.
    /// </summary>
    public bool HasPayload { get; }

    internal static SceneTransition Exit() =>
        new(SceneTransitionKind.Exit, null, null, null, false);

    internal static SceneTransition Restart(object? payload, bool hasPayload) =>
        new(SceneTransitionKind.Restart, null, null, payload, hasPayload);

    internal static SceneTransition ToScene(Type sceneType, object? payload) =>
        new(SceneTransitionKind.Scene, sceneType, null, payload, true);

    internal static SceneTransition ToMap(string mapName, object? payload) =>
        new(SceneTransitionKind.Map, null, mapName, payload, true);
}
