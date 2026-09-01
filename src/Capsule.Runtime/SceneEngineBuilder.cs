using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Runtime;

/// <summary>Host configuration with a game's generated scene registry.</summary>
public sealed class SceneEngineBuilder : EngineBuilder<SceneEngineBuilder>
{
    private readonly SceneRegistry _scenes;

    private Vector2 _cameraViewport;
    private TextureSampling _sampling = TextureSampling.Linear;

    internal SceneEngineBuilder(string gameName, SceneRegistry scenes)
        : base(gameName)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        _scenes = scenes;
    }

    /// <summary>
    /// The world units every scene's camera opens spanning, unless the scene sets its own. These
    /// are world units and stay independent of <c>WithRenderResolution</c>'s pixels; left unset,
    /// a camera spans nothing and draws nothing.
    /// </summary>
    /// <param name="viewport">Width and height in world units, neither negative nor NaN.</param>
    public SceneEngineBuilder WithCameraViewport(Vector2 viewport)
    {
        // NaN compares false to everything, so the range guard below cannot reject it, and a
        // NaN span would reach the renderer as a projection that maps every quad off-screen.
        if (!float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), viewport, "A camera viewport must span a finite number of world units.");
        }

        if (viewport.X < 0f || viewport.Y < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), viewport, "A camera viewport cannot span a negative number of world units.");
        }

        _cameraViewport = viewport;

        return this;
    }

    /// <summary>
    /// How every scene filters world-space textures unless it sets its own; a pixel-art game
    /// declares <see cref="TextureSampling.Point"/> once here. Defaults to
    /// <see cref="TextureSampling.Linear"/>.
    /// </summary>
    public SceneEngineBuilder WithSampling(TextureSampling sampling)
    {
        if (sampling is not TextureSampling.Linear and not TextureSampling.Point)
        {
            throw new ArgumentOutOfRangeException(nameof(sampling), sampling, "A sampling policy must be one of the declared modes.");
        }

        _sampling = sampling;

        return this;
    }

    /// <summary>
    /// Opens the window and runs <typeparamref name="TScene"/> until game code requests exit. A
    /// scene a document backs loads it first; one that is not runs as it is.
    /// </summary>
    /// <typeparam name="TScene">A scene this builder's registry holds.</typeparam>
    /// <exception cref="InvalidOperationException">The registry holds no such class.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public void RunScene<TScene>()
        where TScene : Scene
        => RunScene(SceneTarget.ForScene(typeof(TScene)));

    /// <summary>
    /// Opens the window and runs the scene the named document backs, or a plain
    /// <see cref="Scene"/> composed from it when no class claims it. The current parsed document
    /// is reused on restart.
    /// </summary>
    /// <param name="name">A scene document's bare name, as its authoring source is named.</param>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public void RunScene(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        RunScene(SceneTarget.ForName(name));
    }

    private void RunScene(in SceneTarget initialTarget)
    {
        // Ahead of composing, not inside Run: a scene's OnStart runs while the host is built here.
        InstallLogging();

        SceneComposer composer = new(_scenes);

        using SceneHost host = new(initialTarget, composer.Resolve, new SceneDefaults(_cameraViewport, _sampling));
        Run(host);
    }
}
