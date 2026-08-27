using System.Numerics;
using Capsule.Maps;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Runtime;

/// <summary>
/// The engine configured with the scenes a game declares, so it can boot one by class or by map
/// name as well as host a bare <see cref="ISimulation"/>. The generated
/// <c>Capsule.Runtime.Generated.GameBoot</c> hands one of these back already holding the game's
/// registry; a hand-built <see cref="SceneRegistry"/> reaches the same builder through
/// <see cref="CapsuleEngine.Configure(string, SceneRegistry)"/>.
/// </summary>
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
    /// scene composed from a map loads it first; one that is not runs as it is.
    /// </summary>
    /// <typeparam name="TScene">A scene this builder's registry holds.</typeparam>
    /// <exception cref="InvalidOperationException">The registry holds no such class.</exception>
    /// <exception cref="MapFormatException">The map file is malformed.</exception>
    /// <exception cref="SpawnException">A map object's spawn type is claimed by no entity.</exception>
    public void RunScene<TScene>()
        where TScene : Scene
        => RunScene(SceneTarget.ForScene(typeof(TScene)));

    /// <summary>
    /// Opens the window and runs a map until game code requests exit: as the class claiming that
    /// map name, or as a plain <see cref="Capsule.Scenes.MapScene"/> when no class claims it. The
    /// map is read from <c>assets/maps/{mapName}.map.json</c> beside the executable, where the map
    /// build hook ships it, and the map being played is then kept parsed — so reconstructing a
    /// scene from it, which is what <see cref="Scene.RequestRestart()"/> does, reads no file.
    /// </summary>
    /// <param name="mapName">A map's bare name, as its authoring source is named.</param>
    /// <exception cref="MapFormatException">The map file is malformed.</exception>
    /// <exception cref="SpawnException">A map object's spawn type is claimed by no entity.</exception>
    public void RunScene(string mapName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);

        RunScene(SceneTarget.ForMap(mapName));
    }

    private void RunScene(in SceneTarget initialTarget)
    {
        SceneComposer composer = new(_scenes);

        using SceneHost host = new(initialTarget, composer.Resolve, new SceneDefaults(_cameraViewport, _sampling));
        Run(host);
    }
}
