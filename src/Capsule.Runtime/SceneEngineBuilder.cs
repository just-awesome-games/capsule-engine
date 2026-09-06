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
    private const int DefaultMaxStepsPerFrame = 8;

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
    private int _maxStepsPerFrame = DefaultMaxStepsPerFrame;
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
    private string? _inputRecordingPath;
    private InputTape? _inputTape;
    private string? _stateTracePath;
    private string? _frameCaptureDirectory;
    private long[]? _frameCaptureTicks;

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

    /// <summary>
    /// The most fixed steps one frame may run to catch up on a stall or on steps that cost more
    /// than the step length. Once the bound is reached the frame drops the time it did not run, so
    /// the simulation falls behind wall-clock instead of the frame spiralling. Defaults to 8, which
    /// at 60 Hz absorbs a stall of about an eighth of a second.
    /// </summary>
    /// <param name="steps">Steps per frame; positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">The bound is not positive.</exception>
    public SceneEngineBuilder WithMaxStepsPerFrame(int steps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(steps);
        _maxStepsPerFrame = steps;
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
    /// waits for the display after the host's draw returns. Off unless this is called.
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
    /// Records the snapshot every fixed step consumed and writes it to <paramref name="path"/> as
    /// tape text when the run ends, so a play session becomes an <see cref="InputTape"/> that
    /// replays it. What is recorded is what the simulation saw, past the host's own fullscreen
    /// chord, so recording a replay reproduces the tape it replayed. Off unless this is called.
    /// </summary>
    /// <param name="path">The tape to write; an existing file is overwritten.</param>
    /// <exception cref="ArgumentException">The path is null or blank.</exception>
    public SceneEngineBuilder WithInputRecording(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _inputRecordingPath = path;
        return this;
    }

    /// <summary>
    /// Drives the run from <paramref name="tape"/> rather than from the keyboard and gamepad: one
    /// snapshot per fixed step whatever the frame rate, and the run exits itself once the tape's
    /// last step has run. The fullscreen chord stays the host's and never enters the tape.
    /// </summary>
    /// <exception cref="ArgumentNullException">The tape is null.</exception>
    public SceneEngineBuilder WithInputTape(InputTape tape)
    {
        ArgumentNullException.ThrowIfNull(tape);
        _inputTape = tape;
        return this;
    }

    /// <summary>
    /// Reads the tape text at <paramref name="path"/> now and drives the run from it, exactly as
    /// <see cref="WithInputTape(InputTape)"/> does.
    /// </summary>
    /// <param name="path">A file of the tape text <see cref="WithInputRecording"/> writes.</param>
    /// <exception cref="ArgumentException">The path is null or blank.</exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="FormatException">A line of it is malformed; the message names its number.</exception>
    public SceneEngineBuilder WithInputTape(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _inputTape = InputTape.Parse(File.ReadAllText(path));
        return this;
    }

    /// <summary>
    /// Records what the world looked like at the end of every fixed step and writes it to
    /// <paramref name="path"/> as the CSV a <see cref="StateTrace"/> produces when the run ends.
    /// One trace spans every scene the run passes through. Windowed or headless alike; off unless
    /// this is called, and a run that does not call it pays one null check per step.
    /// </summary>
    /// <param name="path">The CSV to write; an existing file is overwritten.</param>
    /// <exception cref="ArgumentException">The path is null or blank.</exception>
    public SceneEngineBuilder WithStateTrace(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _stateTracePath = path;
        return this;
    }

    /// <summary>
    /// Saves the frame drawn after each of <paramref name="ticks"/> has been simulated as
    /// <c>frame-&lt;tick&gt;.png</c> under <paramref name="directory"/>, which is created if it is
    /// absent. Each listed tick is captured once, on the first frame whose latest completed
    /// simulation step is at or past it; a tick the run never reaches is never captured.
    /// <para>
    /// What is saved is the surface the world was drawn on, ahead of the letterbox blit into the
    /// window: the render target where <see cref="WithRenderResolution"/> declared one, so the
    /// image's size is that resolution however the window is sized or shaped, and the back buffer
    /// where it did not, which follows the window.
    /// </para>
    /// <para>
    /// A windowed run only. <see cref="RunHeadless{TScene}(InputTape, object?)"/> has no graphics
    /// device and draws nothing, so there is no surface to save and it captures nothing.
    /// </para>
    /// </summary>
    /// <param name="directory">Where the images go; existing files of the same names are overwritten.</param>
    /// <param name="ticks">The simulation ticks to capture, in any order; repeats capture once.</param>
    /// <exception cref="ArgumentException">The directory is null or blank, or no tick was given.</exception>
    /// <exception cref="ArgumentNullException">The tick array is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A tick is negative; the first step ever run is tick 0.</exception>
    public SceneEngineBuilder WithFrameCapture(string directory, params long[] ticks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(ticks);

        if (ticks.Length == 0)
        {
            throw new ArgumentException("A frame capture names at least one tick to capture.", nameof(ticks));
        }

        foreach (long tick in ticks)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(tick, nameof(ticks));
        }

        _frameCaptureDirectory = directory;
        _frameCaptureTicks = [.. ticks];
        return this;
    }

    /// <summary>
    /// Runs <typeparamref name="TScene"/> from <paramref name="tape"/> with no window, no graphics
    /// device and no textures: everything this builder configures below the window — bindings, the
    /// fixed step, the seed, scene defaults — applies, scene transitions are honoured, and one
    /// fixed step runs per tape entry until the tape is spent or the game requests exit. Replaces
    /// any tape <see cref="WithInputTape(InputTape)"/> set, and honours
    /// <see cref="WithInputRecording"/>.
    /// </summary>
    /// <remarks>
    /// A headless run writes no crash log whatever <see cref="WithCrashLog"/> configured: an
    /// exception escaping the scene propagates to the caller, which is a test or a CI job.
    /// </remarks>
    /// <typeparam name="TScene">A scene this builder's registry holds.</typeparam>
    /// <param name="tape">The run's input, one snapshot per fixed step.</param>
    /// <param name="payload">Boot state, exactly as <see cref="RunScene{TScene}(object?)"/> takes it.</param>
    /// <exception cref="ArgumentNullException">The tape is null.</exception>
    /// <exception cref="InvalidOperationException">The registry holds no such class.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public HeadlessRunResult RunHeadless<TScene>(InputTape tape, object? payload = null)
        where TScene : Scene
        => RunHeadless(SceneTransition.ToScene(typeof(TScene), payload), tape);

    /// <summary>
    /// Runs the scene the named document backs from <paramref name="tape"/>, exactly as
    /// <see cref="RunHeadless{TScene}(InputTape, object?)"/> runs a class.
    /// </summary>
    /// <param name="sceneName">The document's key under the scene root, without <c>.scene.json</c>.</param>
    /// <param name="tape">The run's input, one snapshot per fixed step.</param>
    /// <param name="payload">Boot state, exactly as <see cref="RunScene(string, object?)"/> takes it.</param>
    /// <exception cref="ArgumentNullException">The tape is null.</exception>
    /// <exception cref="ArgumentException">The name is blank or is no '/'-joined key.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public HeadlessRunResult RunHeadless(string sceneName, InputTape tape, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);

        return RunHeadless(SceneTransition.ToName(sceneName, payload), tape);
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
    /// <exception cref="InvalidOperationException">The registry holds no such class.</exception>
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
    /// <param name="name">The document's key under the scene root, without <c>.scene.json</c>.</param>
    /// <param name="payload">
    /// Boot state, which reaches the scene as its <c>EntryPayload</c> exactly as a payload given to
    /// <see cref="Scene.RequestScene(string, object?)"/> would; null unless the game supplies one.
    /// </param>
    /// <exception cref="ArgumentException">The name is blank or is no '/'-joined key.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public void RunScene(string name, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        RunScene(SceneTransition.ToName(name, payload));
    }

    // Opens the window and runs simulation until it requests exit.
    internal void Run(ISimulation simulation) => Run(simulation, null);

    private void Run(ISimulation simulation, SceneHost? scenes)
    {
        EngineOptions options = new(
            _windowTitle,
            _windowWidth,
            _windowHeight,
            _resizable,
            _fullscreen,
            _renderResolution,
            _stepSeconds,
            _maxStepsPerFrame,
            _stickDeadzone,
            _triggerDeadzone,
            _bindings,
            _inputTape);

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

        // Declared ahead of the host so the trace is written after the last scene has stopped.
        using StateTraceFile? trace = NewStateTrace();

        using SceneHost host = new(
            initialTarget,
            composer.Resolve,
            new SceneDefaults(_sampling),
            new RandomSource(_randomSeed),
            trace?.Trace);

        Run(host, host);
    }

    // No window, no device, no residency: the scene host is the same one a windowed run drives, so
    // transitions, the seed and the scene defaults behave identically.
    private HeadlessRunResult RunHeadless(in SceneTransition initialTarget, InputTape tape)
    {
        ArgumentNullException.ThrowIfNull(tape);

        InstallLogging();

        SceneComposer composer = new(_scenes);

        using StateTraceFile? trace = NewStateTrace();

        using SceneHost host = new(
            initialTarget,
            composer.Resolve,
            new SceneDefaults(_sampling),
            new RandomSource(_randomSeed),
            trace?.Trace);

        using InputRecorder? recorder = _inputRecordingPath is null ? null : new InputRecorder(_inputRecordingPath);

        FixedStepScheduler scheduler = new(_stepSeconds, _maxStepsPerFrame, _bindings, tape, recorder);

        // Exactly one step's worth of time per call, so the accumulator drains one step and the
        // per-frame step bound never binds.
        while (!scheduler.Advance(_stepSeconds, DeviceSnapshot.Empty, host))
        {
        }

        return new HeadlessRunResult((int)scheduler.Tick, host.ExitRequested, host.View.Metrics);
    }

    private StateTraceFile? NewStateTrace() =>
        _stateTracePath is null ? null : new StateTraceFile(_stateTracePath, new StateTrace());

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

        using InputRecorder? recorder = _inputRecordingPath is null ? null : new InputRecorder(_inputRecordingPath);

        FrameCapture? capture = _frameCaptureDirectory is null
            ? null
            : new FrameCapture(_frameCaptureDirectory, _frameCaptureTicks!);

        using CapsuleGame game = new(options, simulation, scenes, diagnostics, recorder, capture);

        if (_consoleSink is not null)
        {
            _consoleSink.Tick = () => game.SimulationTick;
        }

        game.Run();
    }
}
