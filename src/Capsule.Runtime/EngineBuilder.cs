using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Localization;
using Capsule.Persistence;
using Capsule.Rendering;
using Capsule.Runtime.Persistence;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime;

/// <summary>
/// Host configuration for one game, begun by <c>CapsuleBoot.Configure</c>. Each <c>With</c> call
/// validates its arguments at once and returns this builder.
/// </summary>
/// <remarks>Every setting has a default.</remarks>
public sealed class EngineBuilder
{
    private const int DefaultMaxStepsPerFrame = 8;

    // The boot trace's first stage, taken before any configuration runs.
    private readonly long _builderEntered = Stopwatch.GetTimestamp();
    private readonly InputDriverRegistry _drivers;
    private readonly ScenePayloadRegistry _payloads;
    private readonly string _gameName;
    // The game's generated string table, null for a game with none.
    private readonly StringTable? _strings;

    private (int Width, int Height)? _canvas;
    private string _localFolderName;
    private bool _writesCrashLog = true;
    private string? _saveDirectory;
    private ISaveStorage? _saveStorage;
    private ILogSink? _logSink;
    private ConsoleLogSink? _consoleSink;
    private bool _loggingSilenced;
    // Null until WithRandomSeed or --seed configures one. RunSeed then chooses it per run.
    private ulong? _randomSeed;
    private string? _frameDiagnosticsPath;
    private double? _frameDiagnosticsExitAfterSeconds;
    private Action<Run>? _runStart;
    // The shipped tag --language matched, applied after the run-start hook. Null without the flag.
    private string? _commandLineLanguage;

    // What the command line asked for: the scene class to boot in place of the RunScene call's, the
    // document to compose it from (or the only thing named, for a document no class claims), the
    // payload to boot it with, and whether to run with no window.
    private Type? _commandLineScene;
    private string? _commandLineDocument;
    private IScenePayload? _commandLinePayload;
    private bool _headless;

    // The settled configuration the host reads. Each value is validated by the WithX that wrote it.
    internal HostPlatform Platform { get; private set; } = new();

    internal SceneRegistry Scenes { get; }

    internal InputConfiguration Input { get; } = new();

    internal string WindowTitle { get; private set; }

    // Where the run's display starts. The run holds this instance, and its settings screen changes it.
    internal Display Display { get; } = new();

    internal bool Resizable { get; private set; } = true;

    // Set only by the development flag --uncapped. The host then presents without vertical sync or a
    // frame cap, whatever the run asks for.
    internal bool Uncapped { get; private set; }

    // Where the development plane writes each press's input-to-present latency, or null for none.
    // Only the bench sets it.
    internal string? InputLatencyPath { get; private set; }

    internal (int Width, int Height)? RenderResolution { get; private set; }

    internal double StepSeconds { get; private set; } = 1.0 / StepContext.DefaultStepHertz;

    internal int MaxStepsPerFrame { get; private set; } = DefaultMaxStepsPerFrame;

    internal TextureSampling Sampling { get; private set; } = TextureSampling.Linear;

    // Null unless the run is driven in code instead of from the devices.
    internal IInputDriver? Driver { get; private set; }

    // The clock a headless run paces its frames and times its steps by. A test replaces it.
    internal Func<long> Timestamp { get; set; } = Stopwatch.GetTimestamp;

    internal EngineBuilder(
        string gameName,
        SceneRegistry scenes,
        InputDriverRegistry drivers,
        ScenePayloadRegistry payloads,
        StringTable? strings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(drivers);
        ArgumentNullException.ThrowIfNull(payloads);

        Scenes = scenes;
        _drivers = drivers;
        _payloads = payloads;
        _gameName = gameName;
        _strings = strings;
        WindowTitle = gameName;
        _localFolderName = SafeName.Slug(gameName)
            ?? throw new ArgumentException(
                $"Game name '{gameName}' slugs to no safe directory name for its local folder. Include letters or digits, or set the folder with WithLocalFolder.",
                nameof(gameName));
    }

    /// <summary>
    /// The scene class name or scene document key <c>--scene</c> resolved to, or null.
    /// </summary>
    /// <remarks>
    /// <see cref="WithCommandLine"/> sets it. A shell reads it to choose boot options before it
    /// boots.
    /// </remarks>
    public string? SceneOverride => _commandLineScene?.Name ?? _commandLineDocument;

    // The canvas the run's screen layer is laid out in: the declared canvas, else the declared render
    // resolution, else the window size the run was configured with.
    internal Vector2 Canvas
    {
        get
        {
            if ((_canvas ?? RenderResolution) is { } declared)
            {
                return new Vector2(declared.Width, declared.Height);
            }

            return Display.WindowSize;
        }
    }

    /// <summary>The window's title. Defaults to the game's name.</summary>
    public EngineBuilder WithWindowTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        WindowTitle = title;
        return this;
    }

    /// <summary>
    /// The window's starting client size at 100% display scaling, which <see cref="Display.WindowSize"/>
    /// starts at, and whether the player may resize it.
    /// </summary>
    /// <remarks>Defaults to 1280x720, resizable.</remarks>
    public EngineBuilder WithWindow(int width, int height, bool resizable = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Display.WindowSize = new Vector2(width, height);
        Resizable = resizable;
        return this;
    }

    /// <summary>Starts <see cref="Display.Fullscreen"/> on. The window opens covering its display.</summary>
    public EngineBuilder WithFullscreen()
    {
        Display.Fullscreen = true;
        return this;
    }

    /// <summary>The mode <see cref="Display.VSync"/> starts in.</summary>
    public EngineBuilder WithVSync(VSyncMode mode)
    {
        Display.VSync = mode;
        return this;
    }

    /// <summary>The value <see cref="Display.MaxFrameRate"/> starts at, 0 for no cap.</summary>
    public EngineBuilder WithMaxFrameRate(int framesPerSecond)
    {
        Display.MaxFrameRate = framesPerSecond;
        return this;
    }

    /// <summary>The value <see cref="Display.UnfocusedMaxFrameRate"/> starts at, 30 unless set, or 0 to keep the focused rate.</summary>
    public EngineBuilder WithUnfocusedMaxFrameRate(int framesPerSecond)
    {
        Display.UnfocusedMaxFrameRate = framesPerSecond;
        return this;
    }

    /// <summary>
    /// A fixed render surface in pixels, letterboxed into the window and independent of the
    /// camera's world-unit viewport. Off by default.
    /// </summary>
    /// <remarks>
    /// It is also the canvas unless <see cref="WithCanvas"/> declares one. Pixel art then draws its
    /// interface in the same pixels as its world.
    /// </remarks>
    public EngineBuilder WithRenderResolution(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        RenderResolution = (width, height);
        return this;
    }

    /// <summary>
    /// The screen layer's extent in canvas pixels. It is the base size a
    /// <see cref="Capsule.UI.ScreenEntity"/> is anchored and laid out in, scaled to fit whatever
    /// the frame is presented on.
    /// </summary>
    /// <remarks>
    /// Defaults to the render resolution, then to the window size. The game may change it during
    /// the run through <see cref="Run.Canvas"/>. A larger canvas gives a smaller interface.
    /// </remarks>
    public EngineBuilder WithCanvas(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _canvas = (width, height);
        return this;
    }

    /// <summary>The simulation's fixed step rate in steps per second of simulated time. Defaults to 60.</summary>
    public EngineBuilder WithFixedStep(int hertz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hertz);
        StepSeconds = 1.0 / hertz;
        return this;
    }

    /// <summary>
    /// The most fixed steps one frame may run to catch up on a stall, 8 by default. At the bound
    /// the frame drops the time it did not run, and the simulation falls behind wall clock.
    /// </summary>
    /// <remarks>
    /// A frame also stops catching up once its steps have taken one step length of real time. Steps
    /// that cost more than their length then run one a frame, and the game slows down at the frame rate
    /// they allow. A bound of 1 slows down at any stall. A frame cap widens the bound and the time to
    /// what its period covers.
    /// </remarks>
    public EngineBuilder WithMaxStepsPerFrame(int steps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(steps);
        MaxStepsPerFrame = steps;
        return this;
    }

    /// <summary>Runs on a port's <see cref="HostPlatform"/> subclass in place of the SDL3 defaults.</summary>
    public EngineBuilder WithPlatform(HostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        Platform = platform;
        return this;
    }

    /// <summary>
    /// The directory name the platform opens saves and the crash log under, in place of the folder
    /// slugged from the game's name. A game renamed after release sets its old name here to keep its
    /// players' saves.
    /// </summary>
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
    /// medium.
    /// </summary>
    /// <remarks><c>--saves</c> sets this. The directory is created on the first persist.</remarks>
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
    /// the first scene starts.
    /// </summary>
    /// <remarks>
    /// A game binds its input and applies its saved settings here. Repeated calls run in order. The
    /// run's starting language is already set, and a language a hook sets wins over it.
    /// </remarks>
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
    /// <see cref="global::Capsule.Run.Random"/>. With none configured, a windowed run no driver plays
    /// draws a fresh seed and a headless or driven run starts from <see cref="RandomSource.DefaultSeed"/>.
    /// </summary>
    /// <remarks>
    /// A windowed run logs the seed it starts from, and <c>--seed</c> starts a development build from
    /// it again. A headless or driven run with no seed configured replays identically.
    /// </remarks>
    public EngineBuilder WithRandomSeed(ulong seed)
    {
        _randomSeed = seed;
        return this;
    }

    // Measures every press's input-to-present latency in a windowed run and writes it to path when the
    // host closes. A shipping build ignores it.
    internal EngineBuilder WithInputLatency(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        InputLatencyPath = path;
        return this;
    }

    /// <summary>
    /// Writes host timing to a CSV at <paramref name="path"/>. Off by default.
    /// </summary>
    /// <remarks>
    /// The file holds a boot trace in milliseconds from process start, then one row per frame: the
    /// interval since the previous frame began, the update, the view's build and the submission in
    /// milliseconds, the fixed steps run, the gen-0 collections so far, and the bytes the frame's thread
    /// allocated over the interval. Its directory is created and an existing file is overwritten. A
    /// headless run paces its frames to the fixed step's rate and submits nothing, and its trace has no
    /// device or media stages.
    /// </remarks>
    /// <param name="path">The CSV to write.</param>
    /// <param name="exitAfterSeconds">
    /// Real seconds after the first submitted frame at which the run exits itself, for an unattended
    /// capture. Null runs until the game exits.
    /// </param>
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
    /// Drives the run from <paramref name="driver"/> instead of the keyboard and gamepad. The
    /// driver is asked once per fixed step whatever the frame rate, and the run exits itself once
    /// the driver reports it is finished.
    /// </summary>
    public EngineBuilder WithInputDriver(IInputDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        Driver = driver;
        return this;
    }

    /// <summary>Applies Capsule's standard command line, the flags <c>--help</c> prints.</summary>
    /// <remarks>
    /// A shipping build (<see cref="Development.IsSupported"/> false) accepts only <c>--saves</c> and
    /// <c>--help</c>. A later <see cref="WithInputDriver"/> or <see cref="WithRandomSeed"/> call
    /// replaces what <c>--driver</c> or <c>--seed</c> set. Only <c>RunScene</c> applies <c>--scene</c>
    /// and <c>--payload</c>, and each replaces only its own half of that call's opening scene.
    /// <c>--language</c> sets <see cref="Run.Language"/> after <see cref="WithRunStart"/> hooks run, in
    /// every run.
    /// </remarks>
    /// <param name="args">
    /// The process arguments, holding standard flags only. Capsule refuses a flag it does not
    /// declare, and a game strips its own flags first.
    /// </param>
    /// <exception cref="CommandLineException">The command line is malformed, names a driver, scene, payload or language nothing registered or ships, or asked for <c>--help</c>.</exception>
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

        if (parsed.Seed is { } seed)
        {
            WithRandomSeed(seed);
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

        if (parsed.PayloadName is { } payloadName)
        {
            _commandLinePayload = _payloads.TryCreate(payloadName, out IScenePayload? payload)
                ? payload
                : throw CommandLine.Refuse(_gameName, $"no payload is named '{payloadName}'. Registered: {_payloads.RegisteredNames()}.");
        }

        if (parsed.Language is { } language)
        {
            _commandLineLanguage = ShippedLanguage(language);
        }

        Uncapped = parsed.Uncapped;

        _headless = parsed.Headless;

        return this;
    }

    /// <summary>
    /// Runs <typeparamref name="TScene"/> from <paramref name="driver"/> with no window, graphics
    /// device or textures, until the driver reports it is finished or the game requests exit.
    /// </summary>
    /// <remarks>
    /// Every setting except the window's applies. <paramref name="driver"/> replaces any driver
    /// <see cref="WithInputDriver"/> set, and <c>--scene</c> and <c>--payload</c> are ignored. A
    /// headless run writes no crash log and lets an escaping exception reach the caller. It persists
    /// nothing unless <see cref="WithSaveDirectory"/> or <see cref="WithSaveStorage"/> named a medium.
    /// It steps as fast as the driver allows, or in real-time frames under
    /// <see cref="WithFrameDiagnostics"/>.
    /// </remarks>
    /// <typeparam name="TScene">A scene this builder's registry holds.</typeparam>
    /// <param name="driver">The run's input, one snapshot per fixed step.</param>
    /// <param name="payload">The opening scene's payload, as <see cref="RunScene{TScene}(IScenePayload?)"/> takes it.</param>
    /// <exception cref="InvalidOperationException">The registry holds no such class.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's type key is claimed by no entity.</exception>
    public HeadlessRunResult RunHeadless<TScene>(IInputDriver driver, IScenePayload? payload = null)
        where TScene : Scene
        => RunHeadless(SceneTransition.ToScene(typeof(TScene), payload), driver);

    /// <summary>
    /// Runs the scene the named document backs from <paramref name="driver"/>, as
    /// <see cref="RunHeadless{TScene}(IInputDriver, IScenePayload?)"/> runs a class.
    /// </summary>
    /// <remarks>
    /// It boots <paramref name="scene"/> with <paramref name="payload"/> and ignores <c>--scene</c>
    /// and <c>--payload</c>.
    /// </remarks>
    /// <param name="scene">The document's key, a <c>CapsuleAssets.Scenes</c> member.</param>
    /// <param name="driver">The run's input, one snapshot per fixed step.</param>
    /// <param name="payload">The opening scene's payload, as <see cref="RunScene(SceneKey, IScenePayload?)"/> takes it.</param>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's type key is claimed by no entity.</exception>
    public HeadlessRunResult RunHeadless(SceneKey scene, IInputDriver driver, IScenePayload? payload = null) =>
        RunHeadless(SceneTransition.ToName(scene.Required(nameof(scene)), payload), driver);

    /// <summary>
    /// Opens the window and runs <typeparamref name="TScene"/> until game code requests exit,
    /// composing it from the document that backs it when one does. Returns 0 as the process's exit
    /// code.
    /// </summary>
    /// <typeparam name="TScene">A scene this builder's registry holds.</typeparam>
    /// <param name="payload">
    /// The opening scene's <see cref="IScenePayload"/>. It reaches the scene as its <c>EntryPayload</c>,
    /// like a payload given to <see cref="global::Capsule.Run.RequestScene{TScene}(IScenePayload?)"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">The registry holds no such class.</exception>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's type key is claimed by no entity.</exception>
    /// <exception cref="CommandLineException"><c>--headless</c> was applied and no driver was set.</exception>
    public int RunScene<TScene>(IScenePayload? payload = null)
        where TScene : Scene
        => RunWindowed(SceneTransition.ToScene(typeof(TScene), payload));

    /// <summary>
    /// Opens the window and runs the scene the named document backs, or a plain <see cref="Scene"/>
    /// composed from it when no class claims it. A restart reuses the parsed document instead of
    /// reading the file again.
    /// </summary>
    /// <param name="scene">The document's key, a <c>CapsuleAssets.Scenes</c> member.</param>
    /// <param name="payload">The opening scene's payload, as <see cref="RunScene{TScene}(IScenePayload?)"/> takes it.</param>
    /// <exception cref="SceneDocumentFormatException">The scene document file is malformed.</exception>
    /// <exception cref="SpawnException">A placement's type key is claimed by no entity.</exception>
    /// <exception cref="CommandLineException"><c>--headless</c> was applied and no driver was set.</exception>
    public int RunScene(SceneKey scene, IScenePayload? payload = null) =>
        RunWindowed(SceneTransition.ToName(scene.Required(nameof(scene)), payload));

    // Opens the window, or plays the driver with no window under --headless, and runs until the game
    // requests exit.
    private int RunWindowed(in SceneTransition target)
    {
        SceneTransition opening = Opening(in target);

        if (_headless)
        {
            // Checked here, not by WithCommandLine. A WithInputDriver call after it supplies the driver.
            if (Driver is not { } driver)
            {
                throw CommandLine.Refuse(_gameName, "--headless has nobody to play the game. Name an input driver with --driver or WithInputDriver.");
            }

            RunHeadless(in opening, driver);

            return 0;
        }

        // Everything from here on reaches the crash log, the first scene's composition included. The
        // exception is rethrown to preserve the exit code and the debugger break.
        try
        {
            // Installed before composing, because a scene's OnStart logs while the host is built.
            InstallLogging();

            ulong seed = RunSeed(played: Driver is null);
            Log.Info($"random seed {seed.ToString(CultureInfo.InvariantCulture)}");

            using SceneHost host = CreateHost(opening, NamedSaveStorage() ?? Platform.OpenSaveStorage(_localFolderName), seed, played: Driver is null);
            RunHost(host);
        }
        catch (Exception exception) when (_writesCrashLog)
        {
            Platform.ReportCrash(_localFolderName, exception);
            throw;
        }

        return 0;
    }

    // The scene the run opens: what --scene named, else the call's own, with the payload --payload
    // named, else the call's own.
    private SceneTransition Opening(in SceneTransition target)
    {
        IScenePayload? payload = _commandLinePayload ?? target.Payload;

        return _commandLineDocument is { } document ? SceneTransition.ToName(document, payload)
            : _commandLineScene is { } scene ? SceneTransition.ToScene(scene, payload)
            : target.WithPayload(payload);
    }

    // Builds the window host and runs the scenes until they request exit.
    private void RunHost(SceneHost scenes)
    {
        // Declared first so the host is disposed before it, with its last frame already written.
        using FrameDiagnostics? diagnostics = OpenFrameDiagnostics();

        WindowHost window = new(this, scenes, diagnostics);
        using TickStamp stamp = StampTicks(() => window.SimulationTick);

        window.Run();
    }

    // No window, device or media loading. The scene host is the same a windowed run drives.
    // Transitions, the seed and the run settings behave identically.
    private HeadlessRunResult RunHeadless(in SceneTransition initialTarget, IInputDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        InstallLogging();

        // Declared first so the host is disposed before it, with its last frame already written.
        using FrameDiagnostics? diagnostics = OpenFrameDiagnostics();

        // No medium unless one was named, which keeps a developer's local folder out of the run.
        using SceneHost host = CreateHost(initialTarget, NamedSaveStorage(), RunSeed(played: false), played: false);
        diagnostics?.Mark(FrameDiagnostics.Stage.HostConstructed);
        FixedStepScheduler scheduler = new(StepSeconds, MaxStepsPerFrame, Input.Bindings, driver, host, Timestamp);
        using TickStamp stamp = StampTicks(() => scheduler.Tick);

        if (diagnostics is null)
        {
            // One step's worth of time per call. The accumulator drains a single step and
            // neither the step bound nor the step budget can bind.
            while (!scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, host))
            {
                // Nothing is drawn here. A capture request is taken and dropped instead of waiting
                // for a frame that never comes.
                host.TryTakeFrameCapture(out _);
                host.FlushSaves();
            }
        }
        else
        {
            PaceFrames(scheduler, host, diagnostics);
        }

        // The advance that ends the run may have run a step whose request and writes the loop
        // body never sees.
        host.TryTakeFrameCapture(out _);
        host.FlushSaves();

        return new HeadlessRunResult(scheduler.Tick, host.ExitRequested, host.View.Metrics);
    }

    // Frames as a window presenting once per fixed step runs them. Each frame advances by the wall
    // time since the last one began, and the scheduler decides how much of it to catch up. The view
    // column times the frame's build. Nothing is submitted, and the draw section is empty.
    private void PaceFrames(FixedStepScheduler scheduler, SceneHost host, FrameDiagnostics diagnostics)
    {
        long period = (long)(StepSeconds * Stopwatch.Frequency);
        long previous = Timestamp();
        while (true)
        {
            long start = Timestamp();
            diagnostics.BeginUpdate();
            scheduler.TimeScale = host.Run.TimeScale;
            bool exiting = scheduler.Advance((start - previous) / (double)Stopwatch.Frequency, DeviceSnapshot.Empty, host);
            previous = start;
            diagnostics.EndUpdate(scheduler.StepsThisFrame);

            diagnostics.BeginView();
            _ = host.View;
            diagnostics.BeginDraw();
            if (diagnostics.EndDraw() || exiting)
            {
                return;
            }

            host.TryTakeFrameCapture(out _);
            host.FlushSaves();

            // The present's wait for the next refresh. A frame that overran it starts at once.
            while (Timestamp() - start < period)
            {
                Thread.Yield();
            }
        }
    }

    // The configured seed, else a fresh one for a run a person plays. A headless or driven run is a
    // test or a replay and starts from the default.
    internal ulong RunSeed(bool played)
    {
        if (_randomSeed is { } configured)
        {
            return configured;
        }

        if (!played)
        {
            return RandomSource.DefaultSeed;
        }

        Span<byte> entropy = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(entropy);

        return BinaryPrimitives.ReadUInt64LittleEndian(entropy);
    }

    // A played run is windowed with no driver, and the only kind that asks the platform for a language.
    internal SceneHost CreateHost(in SceneTransition opening, ISaveStorage? storage, ulong seed, bool played) =>
        new(
            opening,
            new SceneComposer(Scenes, Platform).Resolve,
            new Run(new RandomSource(seed)) { Canvas = Canvas, Sampling = Sampling, Input = Input, Display = Display, RenderResolution = RenderResolution },
            storage,
            RunStart(played));

    // The starting language around the game's hooks. The player's is set before them, and a saved choice
    // they apply wins over it. --language is set after them and wins over both.
    private Action<Run>? RunStart(bool played)
    {
        if (_strings is not { } strings)
        {
            return _runStart;
        }

        return run =>
        {
            string preferred = "";
            if (played)
            {
                string[] languages = [.. Platform.PreferredLanguages()];
                int matched = LanguageMatcher.Match(strings.Languages, languages);
                run.Language = strings.Languages[matched < 0 ? strings.Fallback : matched];
                preferred = languages.Length == 0 ? "none" : string.Join(", ", languages);
            }

            _runStart?.Invoke(run);

            if (_commandLineLanguage is { } language)
            {
                run.Language = language;
            }

            if (played)
            {
                Log.Info($"language {strings.Languages[strings.Resolve(run.Language)]} from preferred {preferred}");
            }
        };
    }

    // The shipped tag --language names, refused when no shipped language matches it.
    private string ShippedLanguage(string tag)
    {
        if (_strings is not { } strings)
        {
            throw CommandLine.Refuse(_gameName, "--language has nothing to choose, because this game ships no string tables.");
        }

        int matched = LanguageMatcher.Match(strings.Languages, new ReadOnlySpan<string>(in tag));

        return matched >= 0
            ? strings.Languages[matched]
            : throw CommandLine.Refuse(_gameName, $"no shipped language matches '{tag}'. Shipped: {string.Join(", ", strings.Languages.ToArray())}.");
    }

    // The medium the shell named, which wins over the platform's own.
    private ISaveStorage? NamedSaveStorage() =>
        _saveStorage ?? (_saveDirectory is { } directory ? new DirectorySaveStorage(directory) : null);

    private FrameDiagnostics? OpenFrameDiagnostics() =>
        _frameDiagnosticsPath is null
            ? null
            : new FrameDiagnostics(_frameDiagnosticsPath, _builderEntered, _frameDiagnosticsExitAfterSeconds, Timestamp);

    // Prefixes console lines with tick until the stamp is disposed. The host owns the clock only while
    // it runs. A line written after the run, or by the next run's scene construction, has no clock.
    private TickStamp StampTicks(Func<long> tick)
    {
        _consoleSink?.Tick = tick;

        return new TickStamp(_consoleSink);
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

    private readonly struct TickStamp(ConsoleLogSink? sink) : IDisposable
    {
        public void Dispose() => sink?.Tick = null;
    }
}
