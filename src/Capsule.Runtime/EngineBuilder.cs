using System.Diagnostics;
using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Localization;
using Capsule.Persistence;
using Capsule.Rendering;
using Capsule.Runtime.Audio;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Scenes;

namespace Capsule.Runtime;

/// <summary>
/// Host configuration for one game, begun by <c>CapsuleBoot.Configure</c>. Each <c>With</c> call
/// validates its arguments at once and returns this builder.
/// </summary>
/// <remarks>Every setting has a default.</remarks>
public sealed partial class EngineBuilder
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
    // What --trace and --capture write, each null without its flag.
    private (string Path, TickSelection Ticks, string[] Names)? _commandLineTrace;
    private (string Directory, TickSelection Ticks)? _commandLineCapture;

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
    // frame cap, whatever the run asks for, and a headless run's diagnostic frames run back to back.
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
    /// The file holds a boot trace in milliseconds from process start, then one row per frame from the
    /// first drawn one: the tick its state shows, numbered as <c>--trace</c> numbers them, the interval
    /// from its start to the next frame's, the update, the view's build and the submission in
    /// milliseconds, the fixed steps run, the gen-0 collections so far, the bytes the frame's thread
    /// allocated over the interval, the bytes every thread allocated over it to within each thread's
    /// allocation quantum, and the managed heap's size once the frame's work is done. A frame that draws
    /// nothing, minimized or exiting, times no view or submission. Its directory is created and an
    /// existing file is overwritten. A headless run paces its frames to the fixed step's rate and submits
    /// nothing, and its trace has no device or media stages. Under <c>--uncapped</c> its frames run back
    /// to back, one step each, and the interval times the step and the view's build alone.
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

        if (Development.IsSupported && parsed.TracePath is { } trace)
        {
            TickSelection ticks = Selected("--trace", parsed.TraceTicks!, RunTrace.Ceiling, parsed.Force);
            _commandLineTrace = (trace, ticks, parsed.TraceNames!);
        }

        if (Development.IsSupported && parsed.CapturePath is { } capture)
        {
            _commandLineCapture = (capture, Selected("--capture", parsed.CaptureTicks!, RunCapture.Ceiling, parsed.Force));
        }

        _recordingsDirectory = parsed.RecordingsPath ?? _recordingsDirectory;
        Uncapped = parsed.Uncapped;
        GpuDriver = parsed.GpuDriver;

        _headless = parsed.Headless;

        return this;
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
}
