using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// A <see cref="Scenes.Scene"/> behind the engine's simulation seam. It owns the step
/// choreography — retain positions, run the scene's own step, update entities and their
/// components in order, land the step's deferred changes, rewrite the frame — and that order is
/// the contract every scene runs under.
/// </summary>
public sealed class SceneSimulation : ISimulation
{
    private readonly FrameView _view = new();

    /// <summary>Starts <paramref name="scene"/> and builds its first frame.</summary>
    /// <exception cref="InvalidOperationException">
    /// The scene has already been started; a scene belongs to one simulation for its lifetime.
    /// </exception>
    public SceneSimulation(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Scene = scene;
        scene.Start();

        RewriteView();
    }

    public Scene Scene { get; }

    public bool ExitRequested => Scene.ExitRequested;

    public FrameView View => _view;

    public void Step(in StepContext context)
    {
        Scene.BeginStep();
        Scene.RunStep(in context);
        Scene.UpdateEntities(in context);
        Scene.EndStep();

        RewriteView();
    }

    private void RewriteView()
    {
        _view.Clear();
        _view.Camera = new CameraView(Scene.Camera.Center, Scene.Camera.ViewportSize);

        foreach (Renderer renderer in Scene.RenderersInDrawOrder())
        {
            renderer.Draw(_view);
        }
    }
}
