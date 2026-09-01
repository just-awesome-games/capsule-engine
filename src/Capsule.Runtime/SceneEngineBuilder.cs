using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Input;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Runtime;

/// <summary>
/// Fluent, eagerly validated host configuration for a game's generated scene registry. A
/// <c>RunScene</c> blocks until the game requests exit.
/// </summary>
public sealed class SceneEngineBuilder
{
    private const int DefaultWindowWidth = 1280;
    private const int DefaultWindowHeight = 720;
    private const int DefaultStepHertz = 60;
    private const double DefaultSpikeClampSeconds = 0.25;

    private readonly ActionBindings _bindings = new();
    private readonly SceneRegistry _scenes;

    private string _windowTitle;
    private int _windowWidth = DefaultWindowWidth;
    private int _windowHeight = DefaultWindowHeight;
    private bool _resizable = true;
    private bool _fullscreen;
    private (int Width, int Height)? _renderResolution;
    private double _stepSeconds = 1.0 / DefaultStepHertz;
    private double _maxFrameSeconds = DefaultSpikeClampSeconds;
    private float _stickDeadzone = PadFilter.DefaultStickDeadzone;
    private float _triggerDeadzone = PadFilter.DefaultTriggerDeadzone;
    private string? _crashLogAppName;
    private ILogSink? _logSink;
    private ConsoleLogSink? _consoleSink;
    private bool _loggingSilenced;
    private Vector2 _cameraViewport;
    private TextureSampling _sampling = TextureSampling.Linear;

    /// <param name="gameName">
    /// The game's display name: the window's title, and the crash log's folder as a slug of it.
    /// </param>
    /// <param name="scenes">Every scene the game declares, plain and document-backed alike.</param>
    /// <exception cref="ArgumentException">The name is blank, or no safe directory name slugs out of it.</exception>
    internal SceneEngineBuilder(string gameName, SceneRegistry scenes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);
        ArgumentNullException.ThrowIfNull(scenes);

        _scenes = scenes;
        _windowTitle = gameName;
        _crashLogAppName = SafeName.Slug(gameName)
            ?? throw new ArgumentException(
                $"A game name must slug to one safe directory name for its crash log, and '{gameName}' does not: "
                + "it holds no letter or digit, or what remains is a reserved device name.",
                nameof(gameName));
    }

    /// <summary>The window's title, which is the game's name unless this replaces it.</summary>
    public SceneEngineBuilder WithWindowTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        _windowTitle = title;

        return this;
    }

    /// <summary>
    /// The windowed-mode window: opened at this size unless the game boots fullscreen, and
    /// returned to at this size whenever fullscreen is left. Defaults to 1280x720, resizable.
    /// </summary>
    /// <param name="width">Client width in pixels.</param>
    /// <param name="height">Client height in pixels.</param>
    /// <param name="resizable">Whether the player may drag the window's edges; windowed mode only.</param>
    public SceneEngineBuilder WithWindow(int width, int height, bool resizable = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _windowWidth = width;
        _windowHeight = height;
        _resizable = resizable;

        return this;
    }

    /// <summary>
    /// Boots fullscreen — borderless, at the desktop's own resolution. Alt+Enter toggles
    /// either way from there.
    /// </summary>
    public SceneEngineBuilder WithFullscreen()
    {
        _fullscreen = true;

        return this;
    }

    /// <summary>
    /// Sets a fixed render surface, letterboxed into the window. Dimensions are pixels and are
    /// independent of the camera's world-unit viewport.
    /// </summary>
    /// <param name="width">Render-target width in pixels.</param>
    /// <param name="height">Render-target height in pixels.</param>
    public SceneEngineBuilder WithRenderResolution(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _renderResolution = (width, height);

        return this;
    }

    /// <param name="hertz">Simulation steps per second of simulated time.</param>
    public SceneEngineBuilder WithFixedStep(int hertz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hertz);

        _stepSeconds = 1.0 / hertz;

        return this;
    }

    /// <summary>Caps simulated time contributed by one frame after a stall.</summary>
    /// <param name="seconds">
    /// Real seconds, positive and finite, never below one fixed step; a shorter ceiling
    /// could not carry a whole step, and the run — where both values are known — rejects it.
    /// </param>
    public SceneEngineBuilder WithSpikeClamp(double seconds)
    {
        // NaN passes every comparison-based guard and an infinite ceiling never binds:
        // either would silently disable the clamp the method exists to set.
        if (!double.IsFinite(seconds))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "A spike clamp must be a finite number of seconds.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);

        _maxFrameSeconds = seconds;

        return this;
    }

    /// <summary>
    /// A stick reading inside <paramref name="stick"/> radially reads centred and a trigger
    /// pull below <paramref name="trigger"/> reads released; past either, what remains is
    /// remapped onto [0, 1], so full deflection stays reachable.
    /// </summary>
    /// <param name="stick">Stick radius, in [0, 1); 0 applies no stick deadzone.</param>
    /// <param name="trigger">Trigger pull, in [0, 1); 0 applies no trigger deadzone.</param>
    public SceneEngineBuilder WithGamepadDeadzones(float stick, float trigger)
    {
        RequireDeadzone(stick, nameof(stick));
        RequireDeadzone(trigger, nameof(trigger));

        _stickDeadzone = stick;
        _triggerDeadzone = trigger;

        return this;
    }

    /// <summary>
    /// Writes an escaping exception to <c>crash.log</c> under the OS-local application data
    /// folder for <paramref name="appName"/>, replacing the folder slugged from the game's name.
    /// </summary>
    /// <param name="appName">Used verbatim as one directory name, so it must be exactly that.</param>
    public SceneEngineBuilder WithCrashLog(string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        if (!SafeName.IsOneSafeDirectoryName(appName))
        {
            throw new ArgumentException(
                "A crash-log application name must be a single directory name: no separators, no relative segment, no reserved device name, and no trailing dot or space.",
                nameof(appName));
        }

        _crashLogAppName = appName;

        return this;
    }

    /// <summary>Disables crash-log writes for escaping exceptions.</summary>
    public SceneEngineBuilder WithoutCrashLog()
    {
        _crashLogAppName = null;

        return this;
    }

    /// <summary>
    /// Sends <see cref="Log"/> output to <paramref name="sink"/> instead of to the console the
    /// host would otherwise write to.
    /// </summary>
    public SceneEngineBuilder WithLogSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        _logSink = sink;
        _loggingSilenced = false;

        return this;
    }

    /// <summary>Silences <see cref="Log"/> entirely, so nothing the game writes goes anywhere.</summary>
    public SceneEngineBuilder WithoutLogging()
    {
        _logSink = null;
        _loggingSilenced = true;

        return this;
    }

    /// <summary>Registers action bindings; call it more than once and the registrations accumulate.</summary>
    public SceneEngineBuilder WithBindings(Action<ActionBindings> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(_bindings);

        return this;
    }

    /// <summary>
    /// The world units every scene's camera opens spanning, unless the scene sets its own. These
    /// are world units and stay independent of <see cref="WithRenderResolution"/>'s pixels; left
    /// unset, a camera spans nothing and draws nothing.
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
    /// <exception cref="InvalidOperationException">
    /// The registry holds no such class, or the spike clamp is below the fixed step.
    /// </exception>
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
    /// <exception cref="InvalidOperationException">The spike clamp is below the fixed step.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public void RunScene(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        RunScene(SceneTarget.ForName(name));
    }

    /// <summary>Opens the window and runs <paramref name="simulation"/> until it requests exit.</summary>
    internal void Run(ISimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        InstallLogging();

        // Neither call can see the other's value, so the pair settles here.
        if (_maxFrameSeconds < _stepSeconds)
        {
            throw new InvalidOperationException(
                $"A spike clamp of {_maxFrameSeconds} s is below the fixed step of {_stepSeconds} s: no frame could contribute a whole step, so the simulation would fall behind real time at any frame rate.");
        }

        EngineOptions options = new(
            _windowTitle,
            _windowWidth,
            _windowHeight,
            _resizable,
            _fullscreen,
            _renderResolution,
            _stepSeconds,
            _maxFrameSeconds,
            _stickDeadzone,
            _triggerDeadzone,
            _bindings);

        if (_crashLogAppName is null)
        {
            Host(options, simulation);
            return;
        }

        try
        {
            Host(options, simulation);
        }
        catch (Exception exception)
        {
            // A windowed build has no console, so an escaping exception would otherwise
            // vanish. Rethrow to preserve the exit code and the debugger break.
            CrashLog.TryWrite(_crashLogAppName, exception);
            throw;
        }
    }

    private static void RequireDeadzone(float value, string parameterName)
    {
        // NaN compares false to everything, so the range guards below cannot reject it.
        if (float.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A deadzone radius cannot be NaN.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, 1f, parameterName);
    }

    private void RunScene(in SceneTarget initialTarget)
    {
        // Ahead of composing, not inside Run: a scene's OnStart runs while the host is built here.
        InstallLogging();

        SceneComposer composer = new(_scenes);

        using SceneHost host = new(initialTarget, composer.Resolve, new SceneDefaults(_cameraViewport, _sampling));
        Run(host);
    }

    // Idempotent, and called before anything a game could log from: a scene's OnStart runs while
    // the host is still being composed, and its lines have to reach the same sink as the rest.
    private void InstallLogging()
    {
        if (_loggingSilenced)
        {
            Log.UseSink(null);
            return;
        }

        Log.UseSink(_logSink ?? (_consoleSink ??= new ConsoleLogSink()));
    }

    private void Host(EngineOptions options, ISimulation simulation)
    {
        using CapsuleGame game = new(options, simulation);

        if (_consoleSink is not null)
        {
            _consoleSink.Tick = () => game.SimulationTick;
        }

        game.Run();
    }
}
