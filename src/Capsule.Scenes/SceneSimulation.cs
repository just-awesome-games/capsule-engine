using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>Runs a <see cref="Scenes.Scene"/> through Capsule's fixed-step lifecycle.</summary>
public sealed class SceneSimulation : ISimulation, IDisposable
{
    private readonly FrameView _view = new();
    private bool _disposed;

    /// <summary>Starts <paramref name="scene"/> under <paramref name="defaults"/> and builds its first frame.</summary>
    /// <param name="scene">The scene to run.</param>
    /// <param name="entryPayload">State supplied by the transition that opened the scene.</param>
    /// <param name="defaults">
    /// The game's scene defaults, which fill in whatever the scene set nothing for.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The scene has already been started; a scene belongs to one simulation for its lifetime.
    /// </exception>
    public SceneSimulation(Scene scene, object? entryPayload = null, SceneDefaults defaults = default)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Scene = scene;
        try
        {
            scene.Start(entryPayload, defaults);
            RewriteView();
        }
        catch (Exception startFailure)
        {
            try
            {
                scene.Stop();
            }
            catch (Exception stopFailure)
            {
                throw new AggregateException(
                    $"Starting and then cleaning up {scene.GetType().Name} both failed.",
                    startFailure,
                    stopFailure);
            }

            throw;
        }
    }

    /// <summary>The scene being advanced, for the lifetime of this simulation.</summary>
    public Scene Scene { get; }

    /// <inheritdoc/>
    public bool ExitRequested => Scene.ExitRequested;

    /// <inheritdoc/>
    public FrameView View => _view;

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The simulation has been disposed.</exception>
    public void Step(in StepContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Scene.BeginStep();
        Scene.RunStep(in context);
        Scene.UpdateEntities(in context);

        // Contacts settle where every position this step will produce has been produced, so an
        // enter or exit is never raised against a position something is about to leave.
        Scene.SettleContacts();

        // Ahead of EndStep, not after it: EndStep clears the deferral flag, so a late step run
        // past it would reach the entity list directly instead of queueing like everything else.
        Scene.RunLateStep(in context);
        Scene.EndStep();

        RewriteView();
    }

    /// <summary>Takes the deferred transition requested by the last step, if one was requested.</summary>
    public bool TryTakeTransition(out SceneTransition transition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Scene.TryTakeTransition(out transition);
    }

    /// <summary>Stops the scene and releases every entity it holds.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Scene.Stop();
    }

    private void RewriteView()
    {
        _view.Clear();
        _view.Camera = new CameraView(Scene.Camera.PreviousCenter, Scene.Camera.Center, Scene.Camera.ViewportSize);
        _view.ClearColor = Scene.ClearColor;
        _view.Sampling = Scene.Sampling;

        foreach (Renderer renderer in Scene.RenderersInDrawOrder())
        {
            renderer.Draw(_view);
        }
    }
}
