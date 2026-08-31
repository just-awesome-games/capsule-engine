using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Runtime;

internal enum SceneTargetKind
{
    Scene,
    Named,
}

internal readonly record struct SceneTarget
{
    private SceneTarget(SceneTargetKind kind, Type? sceneType, string? documentName, object? payload)
    {
        Kind = kind;
        SceneType = sceneType;
        DocumentName = documentName;
        Payload = payload;
    }

    internal SceneTargetKind Kind { get; }

    internal Type? SceneType { get; }

    internal string? DocumentName { get; }

    internal object? Payload { get; }

    internal static SceneTarget ForScene(Type sceneType, object? payload = null)
    {
        ArgumentNullException.ThrowIfNull(sceneType);
        return new SceneTarget(SceneTargetKind.Scene, sceneType, null, payload);
    }

    internal static SceneTarget ForName(string documentName, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        return new SceneTarget(SceneTargetKind.Named, null, documentName, payload);
    }

    internal SceneTarget WithPayload(object? payload) => new(Kind, SceneType, DocumentName, payload);
}

internal delegate Scene SceneResolver(in SceneTarget target);

/// <summary>
/// Keeps the runtime alive while scenes replace one another. It resolves requested targets at
/// the host boundary so scene documents and other content never enter game logic as file
/// operations.
/// </summary>
internal sealed class SceneHost : ISimulation, IDisposable
{
    private readonly SceneResolver _resolve;
    private readonly SceneDefaults _defaults;

    private SceneTarget _currentTarget;
    private SceneSimulation _current;
    private bool _disposed;

    internal SceneHost(in SceneTarget initialTarget, SceneResolver resolve, SceneDefaults defaults = default)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        _resolve = resolve;
        _defaults = defaults;
        _currentTarget = initialTarget;
        _current = new SceneSimulation(resolve(initialTarget), initialTarget.Payload, defaults);
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

            case SceneTransitionKind.Named:
                Replace(SceneTarget.ForName(transition.DocumentName!, transition.Payload));
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
        _current = new SceneSimulation(next, target.Payload, _defaults);
        _currentTarget = target;
    }
}
