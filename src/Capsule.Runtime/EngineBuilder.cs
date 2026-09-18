using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Persistence;
using Capsule.Rendering;
using Capsule.Runtime.Persistence;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Runtime;

/// <summary>
/// Fluent, eagerly validated host configuration for a game's generated scene registry. A
/// <c>RunScene</c> blocks until the game requests exit and returns the process's exit code.
/// Every setting has a default, and a game that never calls <see cref="WithInput"/> reads every
/// action as unbound.
/// </summary>
public sealed class EngineBuilder
{
    private const int DefaultWindowWidth = 1280;
    private const int DefaultWindowHeight = 720;
    private const int DefaultMaxStepsPerFrame = 8;

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
    private (int Width, int Height)? _canvas;
    private double _stepSeconds = 1.0 / StepContext.DefaultStepHertz;
    private int _maxStepsPerFrame = DefaultMaxStepsPerFrame;
    private string _localFolderName;
    private bool _writesCrashLog = true;
    private string? _saveDirectory;
    private ISaveStorage? _saveStorage;
    private ILogSink? _logSink;
    private ConsoleLogSink? _consoleSink;
    private bool _loggingSilenced;
    private TextureSampling _sampling = TextureSampling.Linear;
    private ulong _randomSeed = RandomSource.DefaultSeed;
    private string? _frameDiagnosticsPath;
    private double? _frameDiagnosticsExitAfterSeconds;
    private IInputDriver? _driver;
    private CommandLine _commandLine = CommandLine.None;

    internal EngineBuilder(string gameName, SceneRegistry scenes, InputDriverRegistry drivers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(drivers);

        _scenes = scenes;
        _drivers = drivers;
        _gameName = gameName;
        _windowTitle = gameName;
        _localFolderName = SafeName.Slug(gameName)
            ?? throw new ArgumentException(
                $"A game name must slug to one safe directory name for its local folder, and '{gameName}' does not: "
                + "it holds no letter or digit, or what remains is a reserved device name.",
                nameof(gameName));
    }

    internal string Usage => CommandLine.UsageFor(_gameName);

    // The canvas the run's screen layer is laid out in, resolved once by the rule EngineOptions owns.
    private Vector2 Canvas
    {
        get
        {
            (int width, int height) = EngineOptions.CanvasOf(_canvas, _renderResolution, _windowWidth, _windowHeight);

            return new Vector2(width, height);
        }
    }

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
    /// world-unit viewport. Unless <see cref="WithCanvas"/> is called, it is also the canvas the
    /// screen layer is laid out in, so pixel art draws its interface in the same pixels as its world.
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

    /// <summary>
    /// The screen layer's extent in canvas pixels: the base size a
    /// <see cref="Capsule.UI.ScreenEntity"/> is anchored and laid out in, scaled to fit whatever
    /// the frame is presented on. The run's <see cref="Run.Canvas"/> is this; else the render
    /// resolution (<see cref="WithRenderResolution"/>); else the window size
    /// <see cref="WithWindow"/> declared. So a pixel-art game declares only its render resolution
    /// and its interface shares those pixels; an HD game declares neither, renders at native size
    /// and lays its interface out in the window it asked for; and a game that wants a fixed
    /// low-resolution world surface under a high-resolution interface, or the reverse, declares
    /// both. The game may move the canvas during the run through <see cref="Run.Canvas"/>: a
    /// larger canvas is a smaller interface.
    /// </summary>
    /// <param name="width">Canvas width in canvas pixels.</param>
    /// <param name="height">Canvas height in canvas pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either dimension is not positive.</exception>
    public EngineBuilder WithCanvas(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _canvas = (width, height);
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
    /// The most fixed steps one frame may run to catch up on a stall. Once the bound is reached
    /// the frame drops the time it did not run, so the simulation falls behind wall-clock instead
    /// of the frame spiralling. Defaults to 8, an eighth of a second at 60 Hz.
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
    /// The game's per-user local folder, holding <c>crash.log</c> and the <c>saves</c> subfolder
    /// (<c>docs/persistence.md</c> lists the per-OS paths); replaces the folder slugged from the
    /// game's name, so a game renamed after release keeps its players' saves.
    /// </summary>
    /// <param name="folderName">Used verbatim as one directory name, so it must be exactly that.</param>
    /// <exception cref="ArgumentException">It is not a single safe directory name.</exception>
    public EngineBuilder WithLocalFolder(string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        if (!SafeName.IsOneSafeDirectoryName(folderName))
        {
            throw new ArgumentException(
                "A local folder name must be a single directory name: no separators, no relative segment, no reserved device name, and no trailing dot or space.",
                nameof(folderName));
        }

        _localFolderName = folderName;
        return this;
    }

    /// <summary>Disables crash-log writes for escaping exceptions.</summary>
    public EngineBuilder WithoutCrashLog()
    {
        _writesCrashLog = false;
        return this;
    }

    /// <summary>
    /// Keeps save documents under <paramref name="path"/>, the saves directory itself, instead of
    /// the local folder's <c>saves</c>; what <c>--saves &lt;dir&gt;</c> sets, and the one way a
    /// headless run persists anything.
    /// </summary>
    /// <param name="path">Created on the first persist; a relative path resolves against the working directory.</param>
    /// <exception cref="ArgumentException">The path is null or blank.</exception>
    public EngineBuilder WithSaveDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _saveDirectory = path;
        return this;
    }

    /// <summary>
    /// Replaces the medium save documents are kept on, windowed and headless alike, over
    /// <see cref="WithSaveDirectory"/> and the local folder.
    /// </summary>
    /// <exception cref="ArgumentNullException">The storage is null.</exception>
    public EngineBuilder WithSaveStorage(ISaveStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _saveStorage = storage;
        return this;
    }

    /// <summary>
    /// Sends <see cref="Log"/> output to <paramref name="sink"/> rather than the console. The
    /// console sink installed otherwise writes every level to standard output in write order,
    /// each line prefixed with the simulation tick it was written on — <c>[     30] warn  …</c> —
    /// or <c>boot</c> before the host's clock exists, and a shell launched with its standard
    /// output closed still runs.
    /// </summary>
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
    /// Registers the game's input — its bindings, its gamepad deadzones and the debug-menu
    /// button; repeated calls accumulate. A game that never calls this reads every action as
    /// unbound.
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
    /// <see cref="global::Capsule.Run.Random"/>. Defaults to <see cref="RandomSource.DefaultSeed"/>, so a
    /// game that never calls this replays identically run to run.
    /// </summary>
    public EngineBuilder WithRandomSeed(ulong seed)
    {
        _randomSeed = seed;
        return this;
    }

    /// <summary>
    /// Writes host timing to a CSV at <paramref name="path"/>, off unless this is called: a boot
    /// trace giving the milliseconds from process start to each of builder entry, host
    /// construction, device readiness, initial scene assets loaded, the first update and the first
    /// submitted frame, then one row per frame holding the interval since the previous frame began,
    /// the time spent updating and the time spent submitting the draw, all in milliseconds, the
    /// number of fixed steps the update ran, which is zero on a frame that had not accumulated one,
    /// and the process's cumulative gen-0 garbage collection count as the frame ended. Present is
    /// excluded: the backend waits for the display after the host's draw returns.
    /// </summary>
    /// <param name="path">The CSV to write; its directory is created and an existing file is overwritten.</param>
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
    /// Drives the run from <paramref name="driver"/> rather than from the keyboard and gamepad: it
    /// is asked once per fixed step whatever the frame rate, and the run exits itself once the
    /// driver reports it is finished. The fullscreen chord stays the host's.
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
    /// ambiently: a shell that never passes its <c>args</c> has no command line at all. A shipping
    /// build (<see cref="Development.IsSupported"/> false) keeps only <c>--saves</c> and
    /// <c>--help</c> and refuses the development flags as unknown options.
    /// </summary>
    /// <param name="args">
    /// The process arguments, holding standard flags only: a game with flags of its own removes
    /// them first, since anything Capsule does not declare is rejected here.
    /// </param>
    /// <remarks>
    /// <c>--driver</c> on its own opens the window and plays the driver in it; <c>--headless</c>
    /// alongside it opens no window at all. <c>--scene</c> replaces the scene the <c>RunScene</c>
    /// call names, keeping that call's boot payload, and a scene a document backs is opened through
    /// that document. Nothing is thrown and nothing is looked up here: a defect is held so the
    /// fluent chain completes, and <c>RunScene</c> reports it and returns 2.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The argument array is null.</exception>
    public EngineBuilder WithCommandLine(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        _commandLine = CommandLine.Parse(args);

        if (_commandLine.FramesPath is { } frames)
        {
            WithFrameDiagnostics(frames, _commandLine.FramesSeconds);
        }

        if (_commandLine.SavesPath is { } saves)
        {
            WithSaveDirectory(saves);
        }

        return this;
    }

    /// <summary>
    /// The scene class name <c>--scene</c> named on the command line <see cref="WithCommandLine"/>
    /// was given, or null: no command line was given, it named no scene, or it was rejected, in
    /// which case <c>RunScene</c> reports the rejection and returns 2 as it would anyway. Set the
    /// moment <see cref="WithCommandLine"/> returns, before anything is looked up, so a shell can
    /// choose boot options — a render resolution, a sampling mode — for the scene it is about to
    /// boot; whether a registered scene answers to the name is settled when the run starts.
    /// </summary>
    public string? SceneName => _commandLine.Error is null ? _commandLine.SceneName : null;

    /// <summary>
    /// Runs <typeparamref name="TScene"/> from <paramref name="driver"/> with no window, no
    /// graphics device and no textures: everything this builder configures below the window
    /// applies, scene transitions are honoured, and the driver is asked for one snapshot per fixed
    /// step until it reports it is finished or the game requests exit. Replaces any driver
    /// <see cref="WithInputDriver"/> set.
    /// </summary>
    /// <remarks>
    /// A headless run writes no crash log — an escaping exception propagates to the caller — and
    /// persists nothing unless <see cref="WithSaveDirectory"/> or <see cref="WithSaveStorage"/>
    /// named a medium, so persisted state is initial state and never a developer's own.
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
    /// <see cref="global::Capsule.Run.RequestScene{TScene}(object?)"/> would; null unless the game supplies one.
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
    /// <see cref="global::Capsule.Run.RequestScene(string, object?)"/> would; null unless the game supplies one.
    /// </param>
    /// <returns>
    /// The process's exit code: 2 when <see cref="WithCommandLine"/> rejected the command line, or
    /// named a driver no registered driver answers to, or named a scene no one registered scene
    /// class answers to, or asked for a headless run with no driver — each reported on standard
    /// error with the usage block; otherwise 0.
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
            _canvas,
            _stepSeconds,
            _maxStepsPerFrame,
            _input,
            _driver,
            _scenes);

        try
        {
            Host(options, simulation, scenes);
        }
        catch (Exception exception) when (_writesCrashLog)
        {
            // A windowed build has no console, so an escaping exception would otherwise vanish.
            // Rethrown to preserve the exit code and the debugger break.
            CrashLog.TryWrite(_localFolderName, exception);
            throw;
        }
    }

    private int RunScene(in SceneTransition initialTarget)
    {
        if (_commandLine.Error is { } defect)
        {
            return CommandLine.Reject(_gameName, defect);
        }

        if (_commandLine.HelpRequested)
        {
            Console.Out.WriteLine(Usage);

            return 0;
        }

        if (_commandLine.DriverName is { } driverName)
        {
            if (!_drivers.TryCreate(driverName, out IInputDriver? named))
            {
                return CommandLine.Reject(_gameName, $"no input driver is named '{driverName}'. Registered: {_drivers.RegisteredNames()}.");
            }

            _driver = named;
        }

        SceneTransition target = initialTarget;

        if (_commandLine.SceneName is { } sceneName)
        {
            if (_scenes.SceneNamed(sceneName) is not { } selected)
            {
                return CommandLine.Reject(_gameName, $"no one registered scene is named '{sceneName}'. Registered: {_scenes.RegisteredTypes()}.");
            }

            // A document-backed scene is composed through its document, so the flag opens it the
            // way the scene itself would be opened rather than by its class.
            target = _scenes.DocumentNameOf(selected) is { } document
                ? SceneTransition.ToName(document, initialTarget.Payload)
                : SceneTransition.ToScene(selected, initialTarget.Payload);
        }

        if (_commandLine.Headless)
        {
            if (_driver is null)
            {
                return CommandLine.Reject(_gameName, "--headless has no one to play the game: name an input driver with --driver.");
            }

            RunHeadless(target, _driver);

            return 0;
        }

        // Before composing, not inside Run: a scene's OnStart logs while the host is built here.
        InstallLogging();

        SceneComposer composer = new(_scenes);

        ISaveStorage storage = _saveStorage
            ?? (_saveDirectory is { } directory ? new DirectorySaveStorage(directory) : DirectorySaveStorage.InLocalFolder(_localFolderName));

        using SceneHost host = new(
            target,
            composer.Resolve,
            new Run(new RandomSource(_randomSeed)) { Canvas = Canvas, Sampling = _sampling },
            storage);
        Run(host, host);

        return 0;
    }

    // No window, device or media loading: the scene host is the same one a windowed run drives, so
    // transitions, the seed and the run settings behave identically.
    private HeadlessRunResult RunHeadless(in SceneTransition initialTarget, IInputDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        InstallLogging();

        SceneComposer composer = new(_scenes);

        // No medium unless one was named: persisted state is initial state, never a developer's
        // own local folder.
        ISaveStorage? storage = _saveStorage
            ?? (_saveDirectory is { } directory ? new DirectorySaveStorage(directory) : null);

        using SceneHost host = new(
            initialTarget,
            composer.Resolve,
            new Run(new RandomSource(_randomSeed)) { Canvas = Canvas, Sampling = _sampling },
            storage);

        FixedStepScheduler scheduler = new(_stepSeconds, _maxStepsPerFrame, _input.Bindings, driver, host);

        // The clock is the scheduler's for exactly as long as it runs: a line written after the run,
        // or by the next run's scene construction, is before any clock again.
        if (_consoleSink is not null)
        {
            _consoleSink.Tick = () => scheduler.Tick;
        }

        try
        {
            // Exactly one step's worth of time per call, so the accumulator drains one step and the
            // per-frame step bound never binds.
            while (!scheduler.Advance(_stepSeconds, DeviceSnapshot.Empty, host))
            {
                // No surface is ever drawn here, so a capture request is taken and dropped rather than
                // standing for a frame that never comes.
                host.TryTakeFrameCapture(out _);
                host.FlushSaves();
            }

            // The advance that ends the run may have executed a step of its own, whose request and
            // writes the loop body never reaches.
            host.TryTakeFrameCapture(out _);
            host.FlushSaves();

            return new HeadlessRunResult(scheduler.Tick, host.ExitRequested, host.View.Metrics);
        }
        finally
        {
            if (_consoleSink is not null)
            {
                _consoleSink.Tick = null;
            }
        }
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

        try
        {
            game.Run();
        }
        finally
        {
            if (_consoleSink is not null)
            {
                _consoleSink.Tick = null;
            }
        }
    }
}
