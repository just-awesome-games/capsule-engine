using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Runtime;

internal enum SceneTargetKind
{
    Scene,
    Map,
}

internal readonly record struct SceneTarget
{
    private SceneTarget(SceneTargetKind kind, Type? sceneType, string? mapName, object? payload)
    {
        Kind = kind;
        SceneType = sceneType;
        MapName = mapName;
        Payload = payload;
    }

    internal SceneTargetKind Kind { get; }

    internal Type? SceneType { get; }

    internal string? MapName { get; }

    internal object? Payload { get; }

    internal static SceneTarget ForScene(Type sceneType, object? payload = null)
    {
        ArgumentNullException.ThrowIfNull(sceneType);
        return new SceneTarget(SceneTargetKind.Scene, sceneType, null, payload);
    }

    internal static SceneTarget ForMap(string mapName, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        return new SceneTarget(SceneTargetKind.Map, null, mapName, payload);
    }

    internal SceneTarget WithPayload(object? payload) => new(Kind, SceneType, MapName, payload);
}

internal delegate Scene SceneResolver(in SceneTarget target);

/// <summary>
/// Keeps the runtime alive while scenes replace one another. It resolves requested targets at
/// the host boundary so maps and other content never enter game logic as file operations.
/// </summary>
internal sealed class SceneHost : ISimulation, IDisposable
{
    private readonly SceneResolver _resolve;

    private SceneTarget _currentTarget;
    private SceneSimulation _current;
    private bool _disposed;

    internal SceneHost(in SceneTarget initialTarget, SceneResolver resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        _resolve = resolve;
        _currentTarget = initialTarget;
        _current = new SceneSimulation(resolve(initialTarget), initialTarget.Payload);
    }

    public bool ExitRequested { get; private set; }

    public FrameView View => _current.View;

    internal Scene Scene => _current.Scene;

    public void Step(in StepContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _current.Step(context);
        if (!_current.TryTakeTransition(out SceneTransition transition))
        {
            return;
        }

        switch (transition.Kind)
        {
            case SceneTransitionKind.Exit:
                ExitRequested = true;
                _current.Dispose();
                break;

            case SceneTransitionKind.Restart:
                Replace(transition.HasPayload
                    ? _currentTarget.WithPayload(transition.Payload)
                    : _currentTarget);
                break;

            case SceneTransitionKind.Scene:
                Replace(SceneTarget.ForScene(transition.SceneType!, transition.Payload));
                break;

            case SceneTransitionKind.Map:
                Replace(SceneTarget.ForMap(transition.MapName!, transition.Payload));
                break;

            default:
                throw new InvalidOperationException($"Unknown scene transition kind '{transition.Kind}'.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _current.Dispose();
    }

    private void Replace(in SceneTarget target)
    {
        Scene next = _resolve(target);

        _current.Dispose();
        _current = new SceneSimulation(next, target.Payload);
        _currentTarget = target;
    }
}
