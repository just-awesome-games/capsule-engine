using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// A <see cref="Scenes.Scene"/> behind the engine's simulation seam. It owns the step
/// choreography — retain positions, run the scene's own step, update entities and their
/// components in order, land the step's deferred changes, rewrite the frame — and that order is
/// the contract every scene runs under.
/// </summary>
public sealed class SceneSimulation : ISimulation, IDisposable
{
    private readonly FrameView _view = new();
    private bool _disposed;

    /// <summary>Starts <paramref name="scene"/> and builds its first frame.</summary>
    /// <exception cref="InvalidOperationException">
    /// The scene has already been started; a scene belongs to one simulation for its lifetime.
    /// </exception>
    public SceneSimulation(Scene scene, object? entryPayload = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Scene = scene;
        try
        {
            scene.Start(entryPayload);
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

    public Scene Scene { get; }

    public bool ExitRequested => Scene.ExitRequested;

    public FrameView View => _view;

    public void Step(in StepContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Scene.BeginStep();
        Scene.RunStep(in context);
        Scene.UpdateEntities(in context);
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
        _view.Camera = new CameraView(Scene.Camera.Center, Scene.Camera.ViewportSize);
        _view.ClearColor = Scene.ClearColor;
        _view.Sampling = Scene.Sampling;

        foreach (Renderer renderer in Scene.RenderersInDrawOrder())
        {
            renderer.Draw(_view);
        }
    }
}
