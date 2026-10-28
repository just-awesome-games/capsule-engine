using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Localization;
using Capsule.Persistence;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Runtime.Persistence;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime;

public sealed partial class EngineBuilder
{
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
    /// <see cref="WithFrameDiagnostics"/>, which run back to back, one step each, when the command line
    /// gave <c>--uncapped</c>.
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
    /// <exception cref="CommandLineException">
    /// <c>--headless</c> was applied and no driver was set, <c>--trace</c> or <c>--capture</c> could not
    /// write or draw, or one of them halted the run at its ceiling.
    /// </exception>
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
    /// <exception cref="CommandLineException">
    /// <c>--headless</c> was applied and no driver was set, <c>--trace</c> or <c>--capture</c> could not
    /// write or draw, or one of them halted the run at its ceiling.
    /// </exception>
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

            // The switch's check lets a trimmed publish drop the trace and the capture.
            using RunTrace? trace = Development.IsSupported ? OpenTrace() : null;
            using RunCapture? capture = Development.IsSupported ? OpenCapture() : null;
            RunHeadless(in opening, driver, trace, capture);

            return 0;
        }

        RunRecorded(in opening, replayed: null, RunHost);

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
        string openingKey = Scenes.KeyOf(in opening);
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

            SaveRecent(recorder, replay, scenes?.SceneKey ?? openingKey, crashed: false);
        }
        catch (Exception exception) when (_writesCrashLog || recorder is not null)
        {
            SaveRecent(recorder, replay, scenes?.SceneKey ?? openingKey, crashed: true);
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
    private HeadlessRunResult RunHeadless(in SceneTransition initialTarget, IInputDriver driver, RunTrace? trace = null, RunCapture? capture = null)
    {
        ArgumentNullException.ThrowIfNull(driver);

        InstallLogging();

        // Declared first so the host is disposed before it, with its last frame already written.
        using FrameDiagnostics? diagnostics = OpenFrameDiagnostics();

        // No medium unless one was named, which keeps a developer's local folder out of the run.
        using SceneHost scenes = CreateHost(initialTarget, NamedSaveStorage(), RunSeed(played: false), preferredLanguages: null, _commandLineLanguage);
        diagnostics?.Mark(FrameDiagnostics.Stage.HostConstructed);
        HeadlessHost host = new(this, scenes, driver, replay: null, diagnostics, watch: null, trace, capture);
        using TickStamp stamp = StampTicks(() => host.SimulationTick);

        return Finished(host.Run(), trace, capture);
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
            RunStart(preferredLanguages, language),
            Scenes);
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
