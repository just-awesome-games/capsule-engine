using System.Diagnostics;
using System.Globalization;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Input;
using Capsule.Scenes.Spawning;

namespace Capsule.Runtime;

/// <summary>
/// Fluent, eagerly validated host configuration for a game's generated scene registry. A
/// <c>RunScene</c> blocks until the game requests exit and returns the process's exit code.
/// </summary>
public sealed class EngineBuilder
{
    private const int DefaultWindowWidth = 1280;
    private const int DefaultWindowHeight = 720;
    private const int DefaultStepHertz = 60;
    private const int DefaultMaxStepsPerFrame = 8;

    // Capsule's standard command line, in one place: what WithCommandLine parses, what --help
    // prints, and what a rejected command line is answered with.
    private const string StandardFlags = """
          --driver <Name>            drive the run from the input driver of that class name
          --headless                 run with no window, which needs a driver
          --frames <csv> [seconds]   write host frame timing, exiting after seconds when given
          --help                     print this and exit
        """;

    private const int BadArgumentExitCode = 2;

    // The boot trace's first stage after process start, so it is taken before any configuration.
    private readonly long _builderEntered = Stopwatch.GetTimestamp();
    private readonly InputConfiguration _input = new();
    private readonly SceneRegistry _scenes;
    private readonly InputDriverRegistry _drivers;
    private readonly string _gameName;

    private string _windowTitle;
    private int _windowWidth = DefaultWindowWidth;
    private int _windowHeight = DefaultWindowHeight;
    private bool _resizable = true;
    private bool _fullscreen;
    private (int Width, int Height)? _renderResolution;
    private double _stepSeconds = 1.0 / DefaultStepHertz;
    private int _maxStepsPerFrame = DefaultMaxStepsPerFrame;
    private string? _crashLogAppName;
    private ILogSink? _logSink;
    private ConsoleLogSink? _consoleSink;
    private bool _loggingSilenced;
    private TextureSampling _sampling = TextureSampling.Linear;
    private ulong _randomSeed = RandomSource.DefaultSeed;
    private string? _frameDiagnosticsPath;
    private double? _frameDiagnosticsExitAfterSeconds;
    private IInputDriver? _driver;
    private string? _driverName;
    private bool _headless;
    private string? _commandLineError;
    private bool _helpRequested;

    internal EngineBuilder(string gameName, SceneRegistry scenes, InputDriverRegistry drivers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(drivers);

        _scenes = scenes;
        _drivers = drivers;
        _gameName = gameName;
        _windowTitle = gameName;
        _crashLogAppName = SafeName.Slug(gameName)
            ?? throw new ArgumentException(
                $"A game name must slug to one safe directory name for its crash log, and '{gameName}' does not: "
                + "it holds no letter or digit, or what remains is a reserved device name.",
                nameof(gameName));
    }

    internal string Usage => $"usage: {_gameName} [options]{Environment.NewLine}{StandardFlags}";

    /// <summary>The window's title, which is the game's name unless this replaces it.</summary>
    /// <exception cref="ArgumentException">The title is null or blank.</exception>
    public EngineBuilder WithWindowTitle(string title)
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
    public EngineBuilder WithWindow(int width, int height, bool resizable = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _windowWidth = width;
        _windowHeight = height;
        _resizable = resizable;
        return this;
    }

    /// <summary>Boots borderless fullscreen at the desktop's resolution; Alt+Enter toggles from there.</summary>
    public EngineBuilder WithFullscreen()
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
    public EngineBuilder WithRenderResolution(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _renderResolution = (width, height);
        return this;
    }

    /// <summary>The simulation's fixed step rate. Defaults to 60 Hz.</summary>
    /// <param name="hertz">Simulation steps per second of simulated time; positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">The rate is not positive.</exception>
    public EngineBuilder WithFixedStep(int hertz)
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
    public EngineBuilder WithMaxStepsPerFrame(int steps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(steps);
        _maxStepsPerFrame = steps;
        return this;
    }

    /// <summary>
    /// Writes an escaping exception to <c>crash.log</c> under the OS-local application data folder
    /// for <paramref name="appName"/>, replacing the folder slugged from the game's name.
    /// </summary>
    /// <param name="appName">Used verbatim as one directory name, so it must be exactly that.</param>
    /// <exception cref="ArgumentException">It is not a single safe directory name.</exception>
    public EngineBuilder WithCrashLog(string appName)
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
    public EngineBuilder WithoutCrashLog()
    {
        _crashLogAppName = null;
        return this;
    }

    /// <summary>Sends <see cref="Log"/> output to <paramref name="sink"/> rather than the console.</summary>
    /// <exception cref="ArgumentNullException">The sink is null.</exception>
    public EngineBuilder WithLogSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _logSink = sink;
        _loggingSilenced = false;
        return this;
    }

    /// <summary>Silences <see cref="Log"/> entirely.</summary>
    public EngineBuilder WithoutLogging()
    {
        _logSink = null;
        _loggingSilenced = true;
        return this;
    }

    /// <summary>
    /// Registers the game's input — its bindings and its gamepad deadzones; repeated calls
    /// accumulate.
    /// </summary>
    /// <exception cref="ArgumentNullException">The callback is null.</exception>
    public EngineBuilder WithInput(Action<InputConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_input);
        return this;
    }

    /// <summary>
    /// How every scene filters world-space textures unless it sets its own. Defaults to
    /// <see cref="TextureSampling.Linear"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not a declared one.</exception>
    public EngineBuilder WithSampling(TextureSampling sampling)
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
    public EngineBuilder WithRandomSeed(ulong seed)
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
    public EngineBuilder WithFrameDiagnostics(string path, double? exitAfterSeconds = null)
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
    /// Drives the run from <paramref name="driver"/> rather than from the keyboard and gamepad: the
    /// driver is asked once per fixed step whatever the frame rate, and the run exits itself once
    /// the driver reports it is finished. The fullscreen chord stays the host's and never reaches
    /// the driver or the simulation.
    /// </summary>
    /// <exception cref="ArgumentNullException">The driver is null.</exception>
    public EngineBuilder WithInputDriver(IInputDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _driver = driver;
        return this;
    }

    /// <summary>
    /// Applies Capsule's standard command line — the flags <c>--help</c> prints — so a game gets
    /// driven play, headless play and frame timing without writing a parser. Nothing is read
    /// ambiently: a shell that never passes its <c>args</c> has no command line at all.
    /// </summary>
    /// <param name="args">
    /// The process arguments, holding standard flags only: a game with flags of its own removes
    /// them first, since anything Capsule does not declare is rejected here.
    /// </param>
    /// <remarks>
    /// Nothing is thrown and no driver is built: a malformed command line, and a driver name no
    /// registered driver answers to, are held so the fluent chain completes, and <c>RunScene</c>
    /// reports the defect and returns 2.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The argument array is null.</exception>
    public EngineBuilder WithCommandLine(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        HashSet<string> given = new(StringComparer.Ordinal);

        for (int index = 0; index < args.Length; index++)
        {
            string flag = args[index];

            if (!given.Add(flag))
            {
                return RejectCommandLine($"{flag} was given more than once.");
            }

            switch (flag)
            {
                case "--help":
                    _helpRequested = true;
                    break;

                case "--driver":
                    if (!TryValue(args, ref index, out string driver))
                    {
                        return RejectCommandLine("--driver needs a driver name.");
                    }

                    _driverName = driver;
                    break;

                case "--headless":
                    _headless = true;
                    break;

                case "--frames":
                    if (!TryValue(args, ref index, out string frames))
                    {
                        return RejectCommandLine("--frames needs a path.");
                    }

                    if (!TrySeconds(args, ref index, out double? seconds))
                    {
                        return RejectCommandLine("--frames takes a finite duration in seconds above zero.");
                    }

                    WithFrameDiagnostics(frames, seconds);
                    break;

                default:
                    return RejectCommandLine($"unknown option '{flag}'.");
            }
        }

        return this;
    }

    /// <summary>
    /// Runs <typeparamref name="TScene"/> from <paramref name="driver"/> with no window, no graphics
    /// device and no textures: everything this builder configures below the window — bindings, the
    /// fixed step, the seed, scene defaults — applies, scene transitions are honoured, and the
    /// driver is asked for one snapshot per fixed step until it reports it is finished or the game
    /// requests exit. Replaces any driver <see cref="WithInputDriver"/> set.
    /// </summary>
    /// <remarks>
    /// A headless run writes no crash log whatever <see cref="WithCrashLog"/> configured: an
    /// exception escaping the scene propagates to the caller, which is a test or a CI job.
    /// </remarks>
    /// <typeparam name="TScene">A scene this builder's registry holds.</typeparam>
    /// <param name="driver">The run's input, one snapshot per fixed step.</param>
    /// <param name="payload">Boot state, exactly as <see cref="RunScene{TScene}(object?)"/> takes it.</param>
    /// <exception cref="ArgumentNullException">The driver is null.</exception>
    /// <exception cref="InvalidOperationException">The registry holds no such class.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public HeadlessRunResult RunHeadless<TScene>(IInputDriver driver, object? payload = null)
        where TScene : Scene
        => RunHeadless(SceneTransition.ToScene(typeof(TScene), payload), driver);

    /// <summary>
    /// Runs the scene the named document backs from <paramref name="driver"/>, exactly as
    /// <see cref="RunHeadless{TScene}(IInputDriver, object?)"/> runs a class.
    /// </summary>
    /// <param name="sceneName">The document's key under the scene root, without <c>.scene.json</c>.</param>
    /// <param name="driver">The run's input, one snapshot per fixed step.</param>
    /// <param name="payload">Boot state, exactly as <see cref="RunScene(string, object?)"/> takes it.</param>
    /// <exception cref="ArgumentNullException">The driver is null.</exception>
    /// <exception cref="ArgumentException">The name is blank or is no '/'-joined key.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public HeadlessRunResult RunHeadless(string sceneName, IInputDriver driver, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);

        return RunHeadless(SceneTransition.ToName(sceneName, payload), driver);
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
    /// <returns>The process's exit code, as <see cref="RunScene(string, object?)"/> defines it.</returns>
    /// <exception cref="InvalidOperationException">The registry holds no such class.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public int RunScene<TScene>(object? payload = null)
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
    /// <returns>
    /// The process's exit code: 2 when <see cref="WithCommandLine"/> rejected the command line, or
    /// named a driver no registered driver answers to, or asked for a headless run with no driver —
    /// each reported on standard error with the usage block; otherwise 0.
    /// </returns>
    /// <exception cref="ArgumentException">The name is blank or is no '/'-joined key.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public int RunScene(string name, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return RunScene(SceneTransition.ToName(name, payload));
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
            _input,
            _driver);

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

    private int RunScene(in SceneTransition initialTarget)
    {
        if (_commandLineError is not null)
        {
            return Reject(_commandLineError);
        }

        if (_helpRequested)
        {
            Console.Out.WriteLine(Usage);

            return 0;
        }

        if (_driverName is not null)
        {
            if (!_drivers.TryCreate(_driverName, out IInputDriver? named))
            {
                return Reject($"no input driver is named '{_driverName}'. Registered: {_drivers.RegisteredNames()}.");
            }

            _driver = named;
        }

        if (_headless)
        {
            if (_driver is null)
            {
                return Reject("--headless has no one to play the game: name an input driver with --driver.");
            }

            RunHeadless(initialTarget, _driver);

            return 0;
        }

        // Before composing, not inside Run: a scene's OnStart logs while the host is built here.
        InstallLogging();

        SceneComposer composer = new(_scenes);

        using SceneHost host = new(initialTarget, composer.Resolve, new SceneDefaults(_sampling), new RandomSource(_randomSeed));
        Run(host, host);

        return 0;
    }

    private EngineBuilder RejectCommandLine(string error)
    {
        _commandLineError ??= error;

        return this;
    }

    private int Reject(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine(Usage);

        return BadArgumentExitCode;
    }

    // A value never starts with the flag prefix, so a missing one is caught here rather than
    // swallowing the flag that follows it. Blank is missing too: every value reaches a setter that
    // rejects a blank path.
    private static bool TryValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length
            || args[index + 1].StartsWith("--", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = string.Empty;

            return false;
        }

        value = args[++index];

        return true;
    }

    // --frames takes an optional duration, so a following token is only its own when it parses as a
    // number; one that does but is not a duration the builder accepts is a malformed command line,
    // not a run that fails late.
    private static bool TrySeconds(string[] args, ref int index, out double? seconds)
    {
        seconds = null;

        if (index + 1 >= args.Length
            || !double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return true;
        }

        index++;

        if (!double.IsFinite(parsed) || parsed <= 0d)
        {
            return false;
        }

        seconds = parsed;

        return true;
    }

    // No window, no device, no residency: the scene host is the same one a windowed run drives, so
    // transitions, the seed and the scene defaults behave identically.
    private HeadlessRunResult RunHeadless(in SceneTransition initialTarget, IInputDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        InstallLogging();

        SceneComposer composer = new(_scenes);

        using SceneHost host = new(initialTarget, composer.Resolve, new SceneDefaults(_sampling), new RandomSource(_randomSeed));

        FixedStepScheduler scheduler = new(_stepSeconds, _maxStepsPerFrame, _input.Bindings, driver, host);

        // Exactly one step's worth of time per call, so the accumulator drains one step and the
        // per-frame step bound never binds.
        while (!scheduler.Advance(_stepSeconds, DeviceSnapshot.Empty, host))
        {
            // No surface is ever drawn here, so a capture request is taken and dropped rather than
            // standing for a frame that never comes.
            host.TryTakeFrameCapture(out _);
        }

        // The advance that ends the run may have executed a step of its own, whose request the loop
        // body never reaches.
        host.TryTakeFrameCapture(out _);

        return new HeadlessRunResult(scheduler.Tick, host.ExitRequested, host.View.Metrics);
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

        using CapsuleGame game = new(options, simulation, scenes, diagnostics);

        if (_consoleSink is not null)
        {
            _consoleSink.Tick = () => game.SimulationTick;
        }

        game.Run();
    }
}
