using System.Diagnostics;
using System.Numerics;
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
/// Fluent, eagerly validated host configuration for a game's generated scene registry. Every setting
/// has a default, and a game that binds nothing reads every action as unbound.
/// A <c>RunScene</c> blocks until the game requests exit, then returns the process's exit code.
/// </summary>
public sealed class EngineBuilder
{
    private const int DefaultWindowWidth = 1280;
    private const int DefaultWindowHeight = 720;
    private const int DefaultMaxStepsPerFrame = 8;

    // The boot trace's first stage, taken before any configuration runs.
    private readonly long _builderEntered = Stopwatch.GetTimestamp();
    private readonly InputDriverRegistry _drivers;
    private readonly string _gameName;

    private (int Width, int Height)? _canvas;
    private string _localFolderName;
    private bool _writesCrashLog = true;
    private string? _saveDirectory;
    private ISaveStorage? _saveStorage;
    private ILogSink? _logSink;
    private ConsoleLogSink? _consoleSink;
    private bool _loggingSilenced;
    private ulong _randomSeed = RandomSource.DefaultSeed;
    private string? _frameDiagnosticsPath;
    private double? _frameDiagnosticsExitAfterSeconds;
    private Action<Run>? _runStart;

    // What the command line asked for: the scene class to boot in place of the RunScene call's, the
    // document to compose it from (or the only thing named, for a document no class claims), and
    // whether to run with no window.
    private Type? _commandLineScene;
    private string? _commandLineDocument;
    private bool _headless;

    internal EngineBuilder(string gameName, HostPlatform platform, SceneRegistry scenes, InputDriverRegistry drivers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(drivers);

        Platform = platform;
        Scenes = scenes;
        _drivers = drivers;
        _gameName = gameName;
        WindowTitle = gameName;
        _localFolderName = SafeName.Slug(gameName)
            ?? throw new ArgumentException(
                $"Game name '{gameName}' slugs to no safe directory name for its local folder. Include letters or digits, or set the folder with WithLocalFolder.",
                nameof(gameName));
    }

    // The settled configuration the host reads. Each value is validated by the WithX that wrote it.
    internal HostPlatform Platform { get; }

    internal SceneRegistry Scenes { get; }

    internal InputConfiguration Input { get; } = new();

    internal string WindowTitle { get; private set; }

    internal int WindowWidth { get; private set; } = DefaultWindowWidth;

    internal int WindowHeight { get; private set; } = DefaultWindowHeight;

    internal bool Resizable { get; private set; } = true;

    internal bool Fullscreen { get; private set; }

    internal (int Width, int Height)? RenderResolution { get; private set; }

    internal double StepSeconds { get; private set; } = 1.0 / StepContext.DefaultStepHertz;

    internal int MaxStepsPerFrame { get; private set; } = DefaultMaxStepsPerFrame;

    internal TextureSampling Sampling { get; private set; } = TextureSampling.Linear;

    // Null unless the run is driven in code instead of from the devices.
    internal IInputDriver? Driver { get; private set; }

    /// <summary>
    /// The scene class name or scene document key <c>--scene</c> resolved to, or null. <see cref="WithCommandLine"/>
    /// sets it, which lets a shell choose boot options before it boots.
    /// </summary>
    public string? SceneOverride => _commandLineScene?.Name ?? _commandLineDocument;

    // The canvas the run's screen layer is laid out in: the declared canvas, else the declared render
    // resolution, else the window size the run was configured with.
    internal Vector2 Canvas
    {
        get
        {
            (int width, int height) = _canvas ?? RenderResolution ?? (WindowWidth, WindowHeight);

            return new Vector2(width, height);
        }
    }

    /// <summary>The window's title. Defaults to the game's name.</summary>
    /// <exception cref="ArgumentException">The title is null or blank.</exception>
    public EngineBuilder WithWindowTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        WindowTitle = title;
        return this;
    }

    /// <summary>
    /// The windowed-mode window size in client pixels. The window opens at this size unless the game
    /// boots fullscreen, and returns to it whenever fullscreen is left. Defaults to 1280x720,
    /// resizable.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Either dimension is not positive.</exception>
    public EngineBuilder WithWindow(int width, int height, bool resizable = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        WindowWidth = width;
        WindowHeight = height;
        Resizable = resizable;
        return this;
    }

    /// <summary>Boots borderless fullscreen at the desktop's resolution. Alt+Enter toggles from there.</summary>
    public EngineBuilder WithFullscreen()
    {
        Fullscreen = true;
        return this;
    }

    /// <summary>
    /// A fixed render surface in pixels, letterboxed into the window and independent of the camera's
    /// world-unit viewport. It also becomes the canvas unless <see cref="WithCanvas"/> declares one,
    /// so pixel art draws its interface in the same pixels as its world.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Either dimension is not positive.</exception>
    public EngineBuilder WithRenderResolution(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        RenderResolution = (width, height);
        return this;
    }

    /// <summary>
    /// The screen layer's extent in canvas pixels. It is the base size a
    /// <see cref="Capsule.UI.ScreenEntity"/> is anchored and laid out in, scaled to fit whatever the
    /// frame is presented on. Defaults to the render resolution, then to the window size. The game
    /// may move it during the run through <see cref="Run.Canvas"/>, where a larger canvas means a
    /// smaller interface.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Either dimension is not positive.</exception>
    public EngineBuilder WithCanvas(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _canvas = (width, height);
        return this;
    }

    /// <summary>The simulation's fixed step rate in steps per second of simulated time. Defaults to 60.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The rate is not positive.</exception>
    public EngineBuilder WithFixedStep(int hertz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hertz);
        StepSeconds = 1.0 / hertz;
        return this;
    }

    /// <summary>
    /// The most fixed steps one frame may run to catch up on a stall, 8 by default. At the bound the
    /// frame drops the time it did not run, so the simulation falls behind wall clock instead of the
    /// frame spiralling.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The bound is not positive.</exception>
    public EngineBuilder WithMaxStepsPerFrame(int steps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(steps);
        MaxStepsPerFrame = steps;
        return this;
    }

    /// <summary>
    /// The directory name the platform opens saves and the crash log under. It replaces the folder
    /// slugged from the game's name, which lets a game renamed after release keep its players' saves.
    /// </summary>
    /// <exception cref="ArgumentException">It is not a single safe directory name.</exception>
    public EngineBuilder WithLocalFolder(string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        if (!SafeName.IsOneSafeDirectoryName(folderName))
        {
            throw new ArgumentException(
                "A local folder name must be one directory name: no separators, no relative segment, no reserved device name, and no trailing dot or space.",
                nameof(folderName));
        }

        _localFolderName = folderName;
        return this;
    }

    /// <summary>Stops an escaping exception being reported to the platform's crash log.</summary>
    public EngineBuilder WithoutCrashLog()
    {
        _writesCrashLog = false;
        return this;
    }

    /// <summary>
    /// Keeps save documents directly under <paramref name="path"/> instead of on the platform's own
    /// medium. <c>--saves</c> sets this, and it is how a headless run persists anything. The
    /// directory is created on the first persist.
    /// </summary>
    /// <exception cref="ArgumentException">The path is null or blank.</exception>
    public EngineBuilder WithSaveDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _saveDirectory = path;
        return this;
    }

    /// <summary>
    /// Replaces the medium save documents are kept on, windowed and headless alike. It wins over
    /// <see cref="WithSaveDirectory"/> and the platform's own medium.
    /// </summary>
    /// <exception cref="ArgumentNullException">The storage is null.</exception>
    public EngineBuilder WithSaveStorage(ISaveStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _saveStorage = storage;
        return this;
    }

    /// <summary>
    /// Sends <see cref="Log"/> output to <paramref name="sink"/>. Without this, a console sink writes
    /// every level to standard output in write order, each line prefixed with the simulation tick it
    /// was written on, or <c>boot</c> before the host's clock exists.
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
    /// Runs <paramref name="start"/> once when the run starts, after saves are restored and before
    /// the first scene starts. A game binds its input and applies its saved settings here. Repeated
    /// calls run in order.
    /// </summary>
    /// <exception cref="ArgumentNullException">The callback is null.</exception>
    public EngineBuilder WithRunStart(Action<Run> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        _runStart += start;
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

        Sampling = sampling;
        return this;
    }

    /// <summary>
    /// The seed for the run's <see cref="RandomSource"/>, which game logic reaches through
    /// <see cref="global::Capsule.Run.Random"/>. Defaults to <see cref="RandomSource.DefaultSeed"/>,
    /// and a game that never calls this replays identically.
    /// </summary>
    public EngineBuilder WithRandomSeed(ulong seed)
    {
        _randomSeed = seed;
        return this;
    }

    /// <summary>
    /// Writes host timing to a CSV at <paramref name="path"/>, off unless this is called. The file
    /// holds a boot trace of the milliseconds from process start to each boot stage, then one row per
    /// frame with its interval, its update and draw milliseconds, the fixed steps it ran and the
    /// process's cumulative gen-0 collection count. The file's directory is created and an existing
    /// file is overwritten.
    /// </summary>
    /// <param name="path">The CSV to write.</param>
    /// <param name="exitAfterSeconds">
    /// Real seconds after the first submitted frame at which the run exits itself, for an unattended
    /// capture. Null runs until the game exits.
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
    /// Drives the run from <paramref name="driver"/> instead of the keyboard and gamepad. The driver
    /// is asked once per fixed step whatever the frame rate, and the run exits itself once the driver
    /// reports it is finished. The fullscreen chord stays the host's.
    /// </summary>
    /// <exception cref="ArgumentNullException">The driver is null.</exception>
    public EngineBuilder WithInputDriver(IInputDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        Driver = driver;
        return this;
    }

    /// <summary>
    /// Applies Capsule's standard command line, the flags <c>--help</c> prints, giving a game driven
    /// play, headless play and frame timing without a parser of its own. Nothing is read ambiently,
    /// and a shell that never passes its <c>args</c> has no command line. A shipping build
    /// (<see cref="Development.IsSupported"/> false) keeps <c>--saves</c> and <c>--help</c> and
    /// refuses the development flags as unknown options.
    /// <para>
    /// <c>--driver</c> on its own opens the window and plays that driver in it, and a later
    /// <see cref="WithInputDriver"/> replaces it. <c>--headless</c> alongside it opens no window.
    /// <c>--scene</c> replaces the scene the <c>RunScene</c> call names and keeps that call's boot
    /// payload. It takes a registered scene class name, else a scene document key such as
    /// <c>halls/hall</c>. A scene a document backs is opened through that document.
    /// </para>
    /// </summary>
    /// <param name="args">
    /// The process arguments, holding standard flags only. Capsule refuses a flag it does not
    /// declare, and a game strips its own flags first.
    /// </param>
    /// <exception cref="ArgumentNullException">The argument array is null.</exception>
    /// <exception cref="CommandLineException">The command line is malformed, names a driver or scene nothing registered, asks for a headless run with no driver, or asked for <c>--help</c>.</exception>
    public EngineBuilder WithCommandLine(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        CommandLine parsed = CommandLine.Parse(args, _gameName);

        if (parsed.HelpRequested)
        {
            throw CommandLine.Help(_gameName);
        }

        if (parsed.FramesPath is { } frames)
        {
            WithFrameDiagnostics(frames, parsed.FramesSeconds);
        }

        if (parsed.SavesPath is { } saves)
        {
            WithSaveDirectory(saves);
        }

        if (parsed.DriverName is { } driverName)
        {
            Driver = _drivers.TryCreate(driverName, out IInputDriver? named)
                ? named
                : throw CommandLine.Refuse(_gameName, $"no input driver is named '{driverName}'. Registered: {_drivers.RegisteredNames()}.");
        }

        if (parsed.SceneName is { } sceneName)
        {
            if (!Scenes.TryResolveName(sceneName, out _commandLineScene, out _commandLineDocument))
            {
                throw CommandLine.Refuse(
                    _gameName,
                    $"no registered scene class is named '{sceneName}' and no scene document has that key. "
                    + $"Name a class ({Scenes.RegisteredClassNames()}) or a document key ({Scenes.RegisteredDocumentKeys()}).");
            }
        }

        _headless = parsed.Headless;
        if (_headless && Driver is null)
        {
            throw CommandLine.Refuse(_gameName, "--headless has nobody to play the game. Name an input driver with --driver.");
        }

        return this;
    }

    /// <summary>
    /// Runs <typeparamref name="TScene"/> from <paramref name="driver"/> with no window, graphics
    /// device or textures. Everything this builder configures below the window applies, scene
    /// transitions are honoured, and the driver is asked for one snapshot per fixed step until it
    /// reports it is finished or the game requests exit. Replaces any driver
    /// <see cref="WithInputDriver"/> set.
    /// <para>
    /// A headless run writes no crash log, and an escaping exception propagates to the caller. It
    /// persists nothing unless <see cref="WithSaveDirectory"/> or <see cref="WithSaveStorage"/> named
    /// a medium, which keeps a developer's own saves out of the run.
    /// </para>
    /// </summary>
    /// <typeparam name="TScene">A scene this builder's registry holds.</typeparam>
    /// <param name="driver">The run's input, one snapshot per fixed step.</param>
    /// <param name="payload">Boot state, as <see cref="RunScene{TScene}(object?)"/> takes it.</param>
    /// <exception cref="ArgumentNullException">The driver is null.</exception>
    /// <exception cref="InvalidOperationException">The registry holds no such class.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public HeadlessRunResult RunHeadless<TScene>(IInputDriver driver, object? payload = null)
        where TScene : Scene
        => RunHeadless(SceneTransition.ToScene(typeof(TScene), payload), driver);

    /// <summary>
    /// Runs the scene the named document backs from <paramref name="driver"/>, as
    /// <see cref="RunHeadless{TScene}(IInputDriver, object?)"/> runs a class.
    /// </summary>
    /// <param name="sceneName">The document's key under the scene root, without <c>.scene.json</c>.</param>
    /// <param name="driver">The run's input, one snapshot per fixed step.</param>
    /// <param name="payload">Boot state, as <see cref="RunScene(string, object?)"/> takes it.</param>
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
    /// composing it from the document that backs it when one does. Returns the process's exit code,
    /// 0 unless the game asked for another.
    /// </summary>
    /// <typeparam name="TScene">A scene this builder's registry holds.</typeparam>
    /// <param name="payload">
    /// Boot state. It reaches the scene as its <c>EntryPayload</c>, like a payload given to
    /// <see cref="global::Capsule.Run.RequestScene{TScene}(object?)"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">The registry holds no such class.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public int RunScene<TScene>(object? payload = null)
        where TScene : Scene
        => RunWindowed(SceneTransition.ToScene(typeof(TScene), payload));

    /// <summary>
    /// Opens the window and runs the scene the named document backs, or a plain <see cref="Scene"/>
    /// composed from it when no class claims it. A restart reuses the parsed document instead of
    /// reading the file again.
    /// </summary>
    /// <param name="name">The document's key under the scene root, without <c>.scene.json</c>.</param>
    /// <param name="payload">Boot state, as <see cref="RunScene{TScene}(object?)"/> takes it.</param>
    /// <exception cref="ArgumentException">The name is blank or is no '/'-joined key.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public int RunScene(string name, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return RunWindowed(SceneTransition.ToName(name, payload));
    }

    // Opens the window, or plays the driver with no window under --headless, and runs until the game
    // requests exit.
    private int RunWindowed(in SceneTransition target)
    {
        SceneTransition opening = Opening(in target);

        if (_headless)
        {
            RunHeadless(in opening, Driver!);

            return 0;
        }

        // Installed before composing, because a scene's OnStart logs while RunHost builds the host.
        InstallLogging();

        SceneComposer composer = new(Scenes, Platform);

        ISaveStorage storage = _saveStorage
            ?? (_saveDirectory is { } directory ? new DirectorySaveStorage(directory) : Platform.OpenSaveStorage(_localFolderName));

        using SceneHost host = new(
            opening,
            composer.Resolve,
            new Run(new RandomSource(_randomSeed)) { Canvas = Canvas, Sampling = Sampling, Input = Input, RenderResolution = RenderResolution },
            storage,
            _runStart);

        RunHost(host, host);

        return 0;
    }

    // The scene the run opens: what --scene named, keeping the call's payload, or else the call's own.
    private SceneTransition Opening(in SceneTransition target) =>
        _commandLineDocument is { } document ? SceneTransition.ToName(document, target.Payload)
        : _commandLineScene is { } scene ? SceneTransition.ToScene(scene, target.Payload)
        : target;

    // Builds the host and runs simulation until it requests exit, reporting an escaping exception to
    // the platform's crash log. The exception is rethrown to preserve the exit code and the debugger
    // break.
    private void RunHost(ISimulation simulation, SceneHost? scenes)
    {
        try
        {
            // Declared first so the host is disposed before it, with its last frame already written.
            using FrameDiagnostics? diagnostics = _frameDiagnosticsPath is null
                ? null
                : new FrameDiagnostics(_frameDiagnosticsPath, _builderEntered, _frameDiagnosticsExitAfterSeconds);

            using CapsuleGame game = new(this, simulation, scenes, diagnostics);

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
        catch (Exception exception) when (_writesCrashLog)
        {
            Platform.ReportCrash(_localFolderName, exception);
            throw;
        }
    }

    // No window, device or media loading. The scene host is the same a windowed run drives, so
    // transitions, the seed and the run settings behave identically.
    private HeadlessRunResult RunHeadless(in SceneTransition initialTarget, IInputDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        InstallLogging();

        SceneComposer composer = new(Scenes, Platform);

        // No medium unless one was named, which keeps a developer's local folder out of the run.
        ISaveStorage? storage = _saveStorage
            ?? (_saveDirectory is { } directory ? new DirectorySaveStorage(directory) : null);

        using SceneHost host = new(
            initialTarget,
            composer.Resolve,
            new Run(new RandomSource(_randomSeed)) { Canvas = Canvas, Sampling = Sampling, Input = Input, RenderResolution = RenderResolution },
            storage,
            _runStart);

        FixedStepScheduler scheduler = new(StepSeconds, MaxStepsPerFrame, Input.Bindings, driver, host);

        // The scheduler owns the clock only while it runs. A line written after the run, or by the
        // next run's scene construction, has no clock.
        if (_consoleSink is not null)
        {
            _consoleSink.Tick = () => scheduler.Tick;
        }

        try
        {
            // One step's worth of time per call, so the accumulator drains a single step and the
            // per-frame step bound cannot bind.
            while (!scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, host))
            {
                // Nothing is drawn here. A capture request is taken and dropped instead of waiting
                // for a frame that never comes.
                host.TryTakeFrameCapture(out _);
                host.FlushSaves();
            }

            // The advance that ends the run may have run a step whose request and writes the loop
            // body never sees.
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
}
