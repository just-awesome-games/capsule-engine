using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;

namespace Capsule.Runtime;

internal delegate Scene SceneResolver(in SceneTransition target);

// Keeps the runtime alive while scenes replace one another, resolving each requested target at the
// host boundary so content never enters game logic as a file operation.
internal sealed class SceneHost : ISimulation, IDisposable
{
    private readonly SceneResolver _resolve;
    private readonly SceneDefaults _defaults;
    private readonly RandomSource _random;

    private SceneTransition _target;
    private SceneSimulation _current;
    private bool _disposed;

    // random is one source for the whole run: every scene the host opens draws from it, so a
    // transition neither reseeds nor rewinds the sequence.
    internal SceneHost(in SceneTransition initialTarget, SceneResolver resolve, SceneDefaults defaults = default, RandomSource? random = null)
    {
        _resolve = resolve;
        _defaults = defaults;
        _random = random ?? new RandomSource();
        _target = initialTarget;
        _current = new SceneSimulation(resolve(initialTarget), initialTarget.Payload, defaults, _random);
    }

    public bool ExitRequested { get; private set; }

    public FrameView View => _current.View;

    internal Scene Scene => _current.Scene;

    /// <summary>
    /// What keeps the current scene's textures on the device. Null until the device is up, which is
    /// after the first scene is composed: the host applies that scene's set itself, and every set
    /// from a transition on comes through here.
    /// </summary>
    internal SceneResidency? Residency { get; set; }

    /// <summary>The current scene's texture set, and the class name a wiring fault in it names.</summary>
    internal (string Scene, IReadOnlyList<TextureHandle> Textures) TextureSet =>
        (_current.Scene.GetType().Name, _current.Scene.TextureSet);

    public void Step(in StepContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The exit already tore the current scene down; there is nothing left to step.
        if (ExitRequested)
        {
            return;
        }

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
                Replace(transition.HasPayload ? _target.WithPayload(transition.Payload) : _target);
                break;

            case SceneTransitionKind.Scene:
            case SceneTransitionKind.Named:
                Replace(transition);
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

    private void Replace(in SceneTransition target)
    {
        Scene next = _resolve(target);

        // Before the outgoing scene is torn down: composing the incoming one is what settles its
        // set, and a set that cannot be made resident must leave the run on the scene it was on.
        Residency?.MakeResident(next.GetType().Name, next.TextureSet);

        _current.Dispose();
        _current = new SceneSimulation(next, target.Payload, _defaults, _random);
        _target = target;
    }
}
