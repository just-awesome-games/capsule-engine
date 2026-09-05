using System.Diagnostics;
using Capsule.Assets;
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

    // The boot trace's first stage after process start, so it is taken before any configuration.
    private readonly long _builderEntered = Stopwatch.GetTimestamp();
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
    private TextureSampling _sampling = TextureSampling.Linear;
    private ulong _randomSeed = RandomSource.DefaultSeed;
    private string? _frameDiagnosticsPath;
    private double? _frameDiagnosticsExitAfterSeconds;

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
    /// <exception cref="ArgumentException">The title is null or blank.</exception>
    public SceneEngineBuilder WithWindowTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _windowTitle = title;
        return this;
    }

    /// <summary>
    /// The windowed-mode window, opened at this size unless the game boots fullscreen and returned
    /// to it whenever fullscreen is left. Defaults to 1280x720, resizable.
    /// </summary>
    /// <param name="width">Client width in pixels.</param>
    /// <param name="height">Client height in pixels.</param>
    /// <param name="resizable">Whether the player may drag the window's edges; windowed mode only.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either dimension is not positive.</exception>
    public SceneEngineBuilder WithWindow(int width, int height, bool resizable = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _windowWidth = width;
        _windowHeight = height;
        _resizable = resizable;
        return this;
    }

    /// <summary>Boots borderless fullscreen at the desktop's resolution; Alt+Enter toggles from there.</summary>
    public SceneEngineBuilder WithFullscreen()
    {
        _fullscreen = true;
        return this;
    }

    /// <summary>
    /// A fixed render surface, letterboxed into the window; independent of the camera's
    /// world-unit viewport.
    /// </summary>
    /// <param name="width">Render-target width in pixels.</param>
    /// <param name="height">Render-target height in pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either dimension is not positive.</exception>
    public SceneEngineBuilder WithRenderResolution(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _renderResolution = (width, height);
        return this;
    }

    /// <summary>The simulation's fixed step rate. Defaults to 60 Hz.</summary>
    /// <param name="hertz">Simulation steps per second of simulated time; positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">The rate is not positive.</exception>
    public SceneEngineBuilder WithFixedStep(int hertz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hertz);
        _stepSeconds = 1.0 / hertz;
        return this;
    }

    /// <summary>Caps simulated time contributed by one frame after a stall. Defaults to 0.25 s.</summary>
    /// <param name="seconds">
    /// Real seconds, positive and finite, and never below one fixed step; the run rejects a
    /// shorter ceiling, which could not carry a whole step.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite and positive.</exception>
    public SceneEngineBuilder WithSpikeClamp(double seconds)
    {
        // NaN passes every comparison-based guard and an infinite ceiling never binds.
        if (!double.IsFinite(seconds))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "A spike clamp must be a finite number of seconds.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        _maxFrameSeconds = seconds;
        return this;
    }

    /// <summary>
    /// A stick reading inside <paramref name="stick"/> radially reads centred and a trigger pull
    /// below <paramref name="trigger"/> reads released; past either, what remains is remapped onto
    /// [0, 1]. Defaults to 0.25 and 0.12.
    /// </summary>
    /// <param name="stick">Stick radius, in [0, 1); 0 applies no stick deadzone.</param>
    /// <param name="trigger">Trigger pull, in [0, 1); 0 applies no trigger deadzone.</param>
    /// <exception cref="ArgumentOutOfRangeException">A radius is NaN or outside [0, 1).</exception>
    public SceneEngineBuilder WithGamepadDeadzones(float stick, float trigger)
    {
        RequireDeadzone(stick, nameof(stick));
        RequireDeadzone(trigger, nameof(trigger));
        _stickDeadzone = stick;
        _triggerDeadzone = trigger;
        return this;
    }

    /// <summary>
    /// Writes an escaping exception to <c>crash.log</c> under the OS-local application data folder
    /// for <paramref name="appName"/>, replacing the folder slugged from the game's name.
    /// </summary>
    /// <param name="appName">Used verbatim as one directory name, so it must be exactly that.</param>
    /// <exception cref="ArgumentException">It is not a single safe directory name.</exception>
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

    /// <summary>Sends <see cref="Log"/> output to <paramref name="sink"/> rather than the console.</summary>
    /// <exception cref="ArgumentNullException">The sink is null.</exception>
    public SceneEngineBuilder WithLogSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _logSink = sink;
        _loggingSilenced = false;
        return this;
    }

    /// <summary>Silences <see cref="Log"/> entirely.</summary>
    public SceneEngineBuilder WithoutLogging()
    {
        _logSink = null;
        _loggingSilenced = true;
        return this;
    }

    /// <summary>Registers action bindings; repeated calls accumulate.</summary>
    /// <exception cref="ArgumentNullException">The callback is null.</exception>
    public SceneEngineBuilder WithBindings(Action<ActionBindings> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_bindings);
        return this;
    }

    /// <summary>
    /// How every scene filters world-space textures unless it sets its own. Defaults to
    /// <see cref="TextureSampling.Linear"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not a declared one.</exception>
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
    /// The seed for the run's <see cref="RandomSource"/>, which game logic reaches through
    /// <see cref="Scenes.Scene.Random"/>. Defaults to <see cref="RandomSource.DefaultSeed"/>, so a
    /// game that never calls this replays identically run to run.
    /// </summary>
    public SceneEngineBuilder WithRandomSeed(ulong seed)
    {
        _randomSeed = seed;
        return this;
    }

    /// <summary>
    /// Writes host timing to a CSV at <paramref name="path"/>: a boot trace giving the
    /// milliseconds from process start to each of builder entry, host construction, device
    /// readiness, texture residency, the first update and the first submitted frame, then one row
    /// per frame holding the interval since the previous frame began, the time spent updating and
    /// the time spent submitting the draw, all in milliseconds. Present is excluded: the backend
    /// waits for the display after the host's draw returns. Off unless this is called, and then
    /// costs one null check per frame.
    /// </summary>
    /// <param name="path">The CSV to write; an existing file is overwritten.</param>
    /// <param name="exitAfterSeconds">
    /// Real seconds after the first submitted frame at which the run exits itself, for an
    /// unattended capture; null runs until the game exits.
    /// </param>
    /// <exception cref="ArgumentException">The path is null or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The duration is not finite and positive.</exception>
    public SceneEngineBuilder WithFrameDiagnostics(string path, double? exitAfterSeconds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (exitAfterSeconds is { } seconds)
        {
            // NaN passes every comparison-based guard and an infinite budget never binds.
            if (!double.IsFinite(seconds))
            {
                throw new ArgumentOutOfRangeException(nameof(exitAfterSeconds), seconds, "A capture duration must be a finite number of seconds.");
            }

            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds, nameof(exitAfterSeconds));
        }

        _frameDiagnosticsPath = path;
        _frameDiagnosticsExitAfterSeconds = exitAfterSeconds;
        return this;
    }

    /// <summary>
    /// Opens the window and runs <typeparamref name="TScene"/> until game code requests exit,
    /// composing it from the document that backs it when one does.
    /// </summary>
    /// <typeparam name="TScene">A scene this builder's registry holds.</typeparam>
    /// <param name="payload">
    /// Boot state, which reaches the scene as its <c>EntryPayload</c> exactly as a payload given to
    /// <see cref="Scene.RequestScene{TScene}(object?)"/> would; null unless the game supplies one.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The registry holds no such class, or the spike clamp is below the fixed step.
    /// </exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public void RunScene<TScene>(object? payload = null)
        where TScene : Scene
        => RunScene(SceneTransition.ToScene(typeof(TScene), payload));

    /// <summary>
    /// Opens the window and runs the scene the named document backs, or a plain
    /// <see cref="Scene"/> composed from it when no class claims it. A restart reuses the parsed
    /// document rather than reading it again.
    /// </summary>
    /// <param name="name">A scene document's bare name, as its authoring source is named.</param>
    /// <param name="payload">
    /// Boot state, which reaches the scene as its <c>EntryPayload</c> exactly as a payload given to
    /// <see cref="Scene.RequestScene(string, object?)"/> would; null unless the game supplies one.
    /// </param>
    /// <exception cref="ArgumentException">The name is blank or is no '/'-joined key.</exception>
    /// <exception cref="InvalidOperationException">The spike clamp is below the fixed step.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public void RunScene(string name, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        RunScene(SceneTransition.ToName(name, payload));
    }

    /// <summary>Opens the window and runs <paramref name="simulation"/> until it requests exit.</summary>
    internal void Run(ISimulation simulation) => Run(simulation, null);

    private void Run(ISimulation simulation, SceneHost? scenes)
    {
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

        try
        {
            Host(options, simulation, scenes);
        }
        catch (Exception exception) when (_crashLogAppName is not null)
        {
            // A windowed build has no console, so an escaping exception would otherwise vanish.
            // Rethrown to preserve the exit code and the debugger break.
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

    private void RunScene(in SceneTransition initialTarget)
    {
        // Before composing, not inside Run: a scene's OnStart logs while the host is built here.
        InstallLogging();

        SceneComposer composer = new(_scenes);

        using SceneHost host = new(initialTarget, composer.Resolve, new SceneDefaults(_sampling), new RandomSource(_randomSeed));
        Run(host, host);
    }

    private void InstallLogging()
    {
        if (_loggingSilenced)
        {
            Log.UseSink(null);
            return;
        }

        Log.UseSink(_logSink ?? (_consoleSink ??= new ConsoleLogSink()));
    }

    private void Host(EngineOptions options, ISimulation simulation, SceneHost? scenes)
    {
        // Declared first so the host, disposed before it, has written its last frame by then.
        using FrameDiagnostics? diagnostics = _frameDiagnosticsPath is null
            ? null
            : new FrameDiagnostics(_frameDiagnosticsPath, _builderEntered, _frameDiagnosticsExitAfterSeconds);

        // Before the backend initialises SDL in the host's constructor: the hint is read there.
        SdlPlatform.TrimStartupSubsystems();

        using CapsuleGame game = new(options, simulation, scenes, diagnostics);

        if (_consoleSink is not null)
        {
            _consoleSink.Tick = () => game.SimulationTick;
        }

        game.Run();
    }
}
