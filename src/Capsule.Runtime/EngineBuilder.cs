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
using Capsule.Runtime.Audio;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Runtime.Persistence;
using Capsule.Runtime.Platform;
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
    private bool _records = true;
    // Null for Recordings in the local folder, unless --recordings named one.
    private string? _recordingsDirectory;
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
    // The recording --replay loaded, which a windowed run replays in place of its opening.
    private Recording? _commandLineReplay;

    internal string GameName => _gameName;

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

    internal int FramesInFlight { get; private set; } = 1;

    internal int AudioLeadFrames { get; private set; } = MixThread.DefaultLeadFrames;

    // The GPU driver --gpu-driver named, or null to let SDL choose.
    internal string? GpuDriver { get; private set; }

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

    /// <summary>How many frames the GPU may hold at once, from 1 to 3, and 1 unless set.</summary>
    /// <remarks>
    /// With more than one, the next frame is built while the GPU still draws the last, which raises the
    /// frame rate when both are busy for most of a refresh. Under vertical sync each frame beyond the
    /// first puts up to a refresh between a press and the screen.
    /// </remarks>
    public EngineBuilder WithFramesInFlight(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frames, 3);
        FramesInFlight = frames;
        return this;
    }

    /// <summary>
    /// The sound the host keeps queued ahead of the output device, from 5 to 250 milliseconds, and 30
    /// unless set.
    /// </summary>
    /// <remarks>
    /// A sound reaches the speaker about this long after the step that plays it, plus the output device's
    /// own buffer. A queue shorter than that buffer runs dry at every pull, and the host keeps at least one
    /// and a half buffers queued, logging when it raises the lead. A lead shorter than the longest pause of
    /// the mix thread, such as a garbage collection, plays a gap, and the log warns of the first.
    /// </remarks>
    public EngineBuilder WithAudioLatency(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(milliseconds, 5);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(milliseconds, 250);
        AudioLeadFrames = AudioOutput.SampleRate / 1000 * milliseconds;
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
    /// they allow. A bound of 1 slows down at any stall. A frame cap, or vertical sync on a display that
    /// reports its refresh rate, widens the bound and the time to what a frame's period covers.
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

    /// <summary>Stops a windowed development run recording itself.</summary>
    /// <remarks>
    /// The debug overlay then offers no Save Recording row, and nothing is saved at exit or on a crash.
    /// </remarks>
    public EngineBuilder WithoutRecording()
    {
        _records = false;
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
    /// Writes host timing to a CSV at <paramref name="path"/>. Off by default, and a shipping build
    /// ignores it and logs a warning.
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
    /// A shipping build (<see cref="Development.IsSupported"/> false) accepts only <c>--saves</c>,
    /// <c>--gpu-driver</c> and <c>--help</c>. <c>--gpu-driver</c> replaces the driver the platform
    /// would choose, and the window fails to open on a machine that cannot draw through it. A later <see cref="WithInputDriver"/> or <see cref="WithRandomSeed"/> call
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

        // A shipping build parses no --replay, and the check lets a trimmed publish drop the decoder.
        if (Development.IsSupported && parsed.ReplayPath is { } replay)
        {
            try
            {
                _commandLineReplay = Recording.Load(replay);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                throw CommandLine.Refuse(_gameName, $"--replay could not read '{replay}': {failure.Message}");
            }
        }

        _recordingsDirectory = parsed.RecordingsPath ?? _recordingsDirectory;
        Uncapped = parsed.Uncapped;
        GpuDriver = parsed.GpuDriver;

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
    /// Replays <paramref name="recording"/> with no window, as <see cref="RunHeadless{TScene}(IInputDriver, IScenePayload?)"/>
    /// runs a driver, and reports whether the run diverged from it.
    /// </summary>
    /// <remarks>
    /// The recording supplies the opening scene and payload, the seed, the language and the saves restored
    /// at boot, and the shell's other settings apply. Restored saves are kept in memory and persist
    /// nowhere. The whole recording plays whether or not the run diverges, unless the game exits first,
    /// which ends the run there and is itself a divergence.
    /// </remarks>
    /// <example>
    /// <code>
    /// HeadlessRunResult result = builder.RunHeadless(Recording.Load(path));
    /// Assert.Null(result.DivergedAt);
    /// </code>
    /// </example>
    /// <param name="recording">The run to replay.</param>
    /// <param name="watch">Runs after every step with the scene and the tick that step ran at.</param>
    /// <exception cref="InvalidOperationException">
    /// The shell's canvas, render resolution or fixed step differs from the recording's, the recording
    /// opens a scene or payload this game does not register, or <see cref="Development.IsSupported"/> is
    /// false.
    /// </exception>
    public HeadlessRunResult RunHeadless(Recording recording, Action<Scene, long>? watch = null)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (!Development.IsSupported)
        {
            throw new InvalidOperationException(Recording.ShippingRefusal);
        }

        if (ReplayRefusal(recording, out SceneTransition opening) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        return RunReplayHeadless(recording, in opening, watch);
    }

    /// <summary>
    /// Opens the window and runs <typeparamref name="TScene"/> until game code requests exit,
    /// composing it from the document that backs it when one does. Returns 0 as the process's exit
    /// code, or 1 when a <c>--replay</c> run under <c>--headless</c> diverged.
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
    // requests exit. Under --replay the recording supplies the opening, and a headless replay's exit code
    // says whether it diverged.
    private int RunWindowed(in SceneTransition target)
    {
        // The switch's check lets a trimmed publish drop the replay and its decoder.
        if (Development.IsSupported && _commandLineReplay is { } recording)
        {
            return RunCommandLineReplay(recording);
        }

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

        RunRecorded(in opening, replayed: null, RunHost);

        return 0;
    }

    // --replay: the recording's opening in the window, or with none under --headless, whose exit code says
    // whether it diverged.
    private int RunCommandLineReplay(Recording recording)
    {
        if (ReplayRefusal(recording, out SceneTransition opening) is { } refusal)
        {
            throw CommandLine.Refuse(_gameName, refusal);
        }

        if (_headless)
        {
            return RunReplayHeadless(recording, in opening, watch: null).DivergedAt is null ? 0 : 1;
        }

        RunRecorded(in opening, recording, RunHost);

        return 0;
    }

    // Builds the scenes a windowed run hosts, from replayed's boot state when given, and hands them to run.
    // Everything from here on reaches the crash log and the run's recording, the first scene's composition
    // included. The exception is rethrown to preserve the exit code and the debugger break.
    internal void RunRecorded(in SceneTransition opening, Recording? replayed, Action<SceneHost, Recorder?, Replay?> run)
    {
        Recorder? recorder = null;
        SceneHost? scenes = null;
        Replay? replay = null;
        try
        {
            // Installed before composing, because a scene's OnStart logs while the host is built.
            InstallLogging();

            // Read only when no replay supplies the boot state.
            bool played = Driver is null;
            ulong seed = replayed?.Header.Seed ?? RunSeed(played);
            Log.Info($"random seed {seed.ToString(CultureInfo.InvariantCulture)}");

            string[]? languages = replayed is null ? PreferredLanguages(played) : replayed.Header.PreferredLanguages;
            string? language = replayed is null ? _commandLineLanguage : replayed.Header.LanguageOverride;
            ISaveStorage storage = replayed is null
                ? NamedSaveStorage() ?? Platform.OpenSaveStorage(_localFolderName)
                : new RecordedSaveStorage(replayed.Header.Saves);

            // Before the host is built, which runs the game's startup. A crash inside it still saves the
            // boot state, and the shell settings are read before a run-start hook can change them.
            recorder = NewRecorder(in opening, seed, languages, language);
            using SceneHost host = CreateHost(in opening, storage, seed, languages, language, recorder);
            scenes = host;
            replay = Development.IsSupported && replayed is not null ? new Replay(replayed, host, Scenes, recorder, holdsAtEnd: true) : null;
            run(host, recorder, replay);

            SaveRecent(recorder, replay, scenes, crashed: false);
        }
        catch (Exception exception) when (_writesCrashLog || recorder is not null)
        {
            SaveRecent(recorder, replay, scenes, crashed: true);
            if (_writesCrashLog)
            {
                Platform.ReportCrash(_gameName, _localFolderName, exception);
            }

            throw;
        }
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
    private void RunHost(SceneHost scenes, Recorder? recorder, Replay? replay)
    {
        // Declared first so the host is disposed before it, with its last frame already written.
        using FrameDiagnostics? diagnostics = OpenFrameDiagnostics();

        WindowHost window = new(this, scenes, diagnostics, recorder, replay);
        using TickStamp stamp = StampTicks(() => window.SimulationTick);

        window.Run();
    }

    // Builds the headless host and plays the driver until it finishes or the game requests exit.
    private HeadlessRunResult RunHeadless(in SceneTransition initialTarget, IInputDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        InstallLogging();

        // Declared first so the host is disposed before it, with its last frame already written.
        using FrameDiagnostics? diagnostics = OpenFrameDiagnostics();

        // No medium unless one was named, which keeps a developer's local folder out of the run.
        using SceneHost scenes = CreateHost(initialTarget, NamedSaveStorage(), RunSeed(played: false), preferredLanguages: null, _commandLineLanguage);
        diagnostics?.Mark(FrameDiagnostics.Stage.HostConstructed);
        HeadlessHost host = new(this, scenes, driver, replay: null, diagnostics);
        using TickStamp stamp = StampTicks(() => host.SimulationTick);

        return host.Run();
    }

    // Builds the headless host over the recording's boot state and replays it to its end. Its saves are
    // kept in memory and persist nowhere.
    private HeadlessRunResult RunReplayHeadless(Recording recording, in SceneTransition opening, Action<Scene, long>? watch)
    {
        InstallLogging();

        // Declared first so the host is disposed before it, with its last frame already written.
        using FrameDiagnostics? diagnostics = OpenFrameDiagnostics();

        RecordingHeader header = recording.Header;
        using SceneHost scenes = CreateHost(opening, new RecordedSaveStorage(header.Saves), header.Seed, header.PreferredLanguages, header.LanguageOverride);
        diagnostics?.Mark(FrameDiagnostics.Stage.HostConstructed);
        HeadlessHost host = new(this, scenes, driver: null, new Replay(recording, scenes, Scenes, recorder: null, holdsAtEnd: false), diagnostics, watch);
        using TickStamp stamp = StampTicks(() => host.SimulationTick);

        return host.Run();
    }

    // Why the recording cannot replay under this shell, or null with the opening it replays. The shell's
    // settings the simulation reads must match, and the opening must be registered here.
    private string? ReplayRefusal(Recording recording, out SceneTransition opening)
    {
        opening = default;
        RecordingHeader header = recording.Header;
        if (Mismatch("fixed step", Hertz(header.StepSeconds), Hertz(StepSeconds)) is { } step)
        {
            return step;
        }

        if (Mismatch("canvas", Size(header.Canvas), Size(Canvas)) is { } canvas)
        {
            return canvas;
        }

        if (Mismatch("render resolution", Declared(header.RenderResolution), Declared(RenderResolution)) is { } resolution)
        {
            return resolution;
        }

        if (!Scenes.TryResolveName(header.Scene, out Type? sceneType, out string? document)
            || (header.OpensDocument ? document is null : sceneType is null))
        {
            return $"the recording opens scene '{header.Scene}', which this game does not register. Replay it with the build that recorded it.";
        }

        IScenePayload? payload = null;
        if (header.Payload is { } named && !_payloads.TryCreate(named, out payload))
        {
            return $"the recording opens {header.Scene} with a {named} payload, which is not a registered payload class. "
                + $"Give {named} a public parameterless constructor, which registers it as --payload needs, then record the run again.";
        }

        opening = header.OpensDocument ? SceneTransition.ToName(document!, payload) : SceneTransition.ToScene(sceneType!, payload);

        return null;

        static string Hertz(double seconds) => (1 / seconds).ToString("0.###", CultureInfo.InvariantCulture) + " Hz";

        static string Size(Vector2 size) => string.Create(CultureInfo.InvariantCulture, $"{size.X}x{size.Y}");

        static string Declared((int Width, int Height)? size) =>
            size is { } declared ? string.Create(CultureInfo.InvariantCulture, $"{declared.Width}x{declared.Height}") : "none";

        static string? Mismatch(string setting, string recorded, string configured) =>
            recorded == configured ? null
                : $"the recording was made with a {setting} of {recorded} and this shell configures {configured}. "
                    + $"Configure the shell's {setting} as the recording's was, or record the run again.";
    }

    // Saves the run into Recent and logs where, named for its scene, or for its opening when it crashed
    // before its host was built. A replay never handed over to live input is the recording it replayed,
    // which is saved already. A failed write masks neither the run's end nor its crash.
    private void SaveRecent(Recorder? recorder, Replay? replay, SceneHost? scenes, bool crashed)
    {
        if (!Development.IsSupported || recorder is null || replay is { HandedOver: false })
        {
            return;
        }

        string scene = scenes?.Scene.GetType().Name ?? recorder.OpeningScene;
        if (RecordingFiles.SaveRecent(recorder, RecordingsDirectory(), scene, crashed) is { } path)
        {
            string ticks = recorder.Ticks.ToString(CultureInfo.InvariantCulture);
            if (crashed)
            {
                Log.Error($"the crashed run's recording of {ticks} ticks is at {path}");
            }
            else
            {
                Log.Info($"Saved {ticks} ticks to {path}");
            }
        }
    }

    // Where the overlay saves recordings, and whose Recent holds the automatic ones.
    internal string RecordingsDirectory() =>
        _recordingsDirectory ?? Path.Combine(LocalFolder.Resolve(_localFolderName), RecordingFiles.DirectoryName);

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

    // The recorder of a windowed run, with its boot state and the shell's settings as they stand before
    // the game's startup runs, or null when the shell turned recording off.
    internal Recorder? NewRecorder(in SceneTransition opening, ulong seed, string[]? languages, string? language)
    {
        if (!Development.IsSupported || !_records)
        {
            return null;
        }

        RecordingHeader boot = new()
        {
            GameVersion = BuildIdentity.Version(System.Reflection.Assembly.GetEntryAssembly()),
            CapsuleVersion = BuildIdentity.Version(typeof(EngineBuilder).Assembly),
            Seed = seed,
            StepSeconds = StepSeconds,
            Canvas = Canvas,
            RenderResolution = RenderResolution,
            OpensDocument = opening.Kind == SceneTransitionKind.Named,
            Scene = opening.Kind == SceneTransitionKind.Named ? opening.DocumentName! : opening.SceneType!.Name,
            Payload = opening.Payload?.GetType().Name,
            PreferredLanguages = languages,
            LanguageOverride = language,
        };

        return new Recorder(boot);
    }

    // A played run asks the platform for its languages, and --language overrides.
    internal SceneHost CreateHost(in SceneTransition opening, ISaveStorage? storage, ulong seed, bool played) =>
        CreateHost(in opening, storage, seed, PreferredLanguages(played), _commandLineLanguage);

    // preferredLanguages is what the platform reported, null for a run that asked for none, and language
    // is the --language override. recorder records the host from its first step and copies every document
    // the storage restores.
    internal SceneHost CreateHost(
        in SceneTransition opening,
        ISaveStorage? storage,
        ulong seed,
        string[]? preferredLanguages,
        string? language,
        Recorder? recorder = null)
    {
        SceneHost host = new(
            opening,
            new SceneComposer(Scenes, Platform).Resolve,
            new Run(new RandomSource(seed)) { Canvas = Canvas, Sampling = Sampling, Input = Input, Display = Display, RenderResolution = RenderResolution },
            storage is null || recorder is null ? storage : new TeeSaveStorage(storage, recorder.Restored),
            RunStart(preferredLanguages, language));
        recorder?.Attach(host);

        return host;
    }

    // A played run is windowed with no driver, and the only kind that asks the platform for its languages,
    // in a game with string tables.
    private string[]? PreferredLanguages(bool played) =>
        played && _strings is not null ? [.. Platform.PreferredLanguages()] : null;

    // The starting language around the game's hooks. The player's is set before them, and a saved choice
    // they apply wins over it. --language is set after them and wins over both.
    private Action<Run>? RunStart(string[]? preferredLanguages, string? language)
    {
        if (_strings is not { } strings)
        {
            return _runStart;
        }

        return run =>
        {
            if (preferredLanguages is not null)
            {
                int matched = LanguageMatcher.Match(strings.Languages, preferredLanguages);
                run.Language = strings.Languages[matched < 0 ? strings.Fallback : matched];
            }

            _runStart?.Invoke(run);

            if (language is not null)
            {
                run.Language = language;
            }

            if (preferredLanguages is not null)
            {
                string preferred = preferredLanguages.Length == 0 ? "none" : string.Join(", ", preferredLanguages);
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

    // The switch's check lets a trimmed publish drop the capture and the process clock it reads. The
    // warning names the exit too, since an unattended run counting on it would never end.
    private FrameDiagnostics? OpenFrameDiagnostics()
    {
        if (_frameDiagnosticsPath is null)
        {
            return null;
        }

        if (!Development.IsSupported)
        {
            Log.Warning(
                _frameDiagnosticsExitAfterSeconds is { } seconds
                    ? FormattableString.Invariant($"frame diagnostics are off in a shipping build: nothing is written to {_frameDiagnosticsPath}, and the run does not exit after {seconds} s")
                    : $"frame diagnostics are off in a shipping build: nothing is written to {_frameDiagnosticsPath}");

            return null;
        }

        return new FrameDiagnostics(_frameDiagnosticsPath, _builderEntered, _frameDiagnosticsExitAfterSeconds, Timestamp);
    }

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
