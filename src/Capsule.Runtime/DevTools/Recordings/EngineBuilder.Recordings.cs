using System.Globalization;
using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Runtime.Platform;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime;

public sealed partial class EngineBuilder
{
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
            using RunTrace? trace = OpenTrace();
            using RunCapture? capture = OpenCapture();

            return RunReplayHeadless(recording, in opening, watch: null, trace, capture).DivergedAt is null ? 0 : 1;
        }

        RunRecorded(in opening, recording, RunHost);

        return 0;
    }

    // The ticks a --trace or --capture names, refused here when they pass the flag's ceiling before the run.
    // A recording knows its last tick, and an open range over a driver halts the run at the ceiling instead.
    private TickSelection Selected(string flag, TickRange[] ranges, int ceiling, bool force)
    {
        TickSelection ticks = new(flag, ranges, ceiling, force);

        return ticks.Refusal(_commandLineReplay?.End.FinalTick) is { } refusal
            ? throw CommandLine.Refuse(_gameName, refusal)
            : ticks;
    }

    // The file --trace names, opened before the run so a path it cannot write is refused at once.
    private RunTrace? OpenTrace()
    {
        if (_commandLineTrace is not var (path, ticks, names))
        {
            return null;
        }

        try
        {
            return new RunTrace(path, ticks, names);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw CommandLine.Refuse(_gameName, $"--trace could not write '{path}': {failure.Message}");
        }
    }

    // The directory --capture names and the device it draws on, opened before the run so a directory it
    // cannot write or a GPU that will not open is refused at once.
    private RunCapture? OpenCapture()
    {
        if (_commandLineCapture is not var (directory, ticks))
        {
            return null;
        }

        try
        {
            return new RunCapture(this, directory, ticks);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw CommandLine.Refuse(_gameName, $"--capture could not write '{directory}': {failure.Message}");
        }
        catch (InvalidOperationException failure)
        {
            throw CommandLine.Refuse(_gameName, $"--capture could not open a GPU to draw on: {failure.Message}");
        }
    }

    // Builds the headless host over the recording's boot state and replays it to its end. Its saves are
    // kept in memory and persist nowhere.
    private HeadlessRunResult RunReplayHeadless(Recording recording, in SceneTransition opening, Action<Scene, long>? watch, RunTrace? trace = null, RunCapture? capture = null)
    {
        InstallLogging();

        // Declared first so the host is disposed before it, with its last frame already written.
        using FrameDiagnostics? diagnostics = OpenFrameDiagnostics();

        RecordingHeader header = recording.Header;
        using SceneHost scenes = CreateHost(opening, new RecordedSaveStorage(header.Saves), header.Seed, header.PreferredLanguages, header.LanguageOverride);
        diagnostics?.Mark(FrameDiagnostics.Stage.HostConstructed);
        Replay replay = new(recording, scenes, Scenes, recorder: null, holdsAtEnd: false);
        if (Development.IsSupported && trace is not null)
        {
            replay.Acted = (tick, subject, act) => trace.Act(tick + 1, subject, act);
        }

        HeadlessHost host = new(this, scenes, driver: null, replay, diagnostics, watch, trace, capture);
        using TickStamp stamp = StampTicks(() => host.SimulationTick);

        return Finished(host.Run(), trace, capture);
    }

    // A run that exits ends on the tick before its exiting step. A run a trace or a capture halted is refused
    // once both have finished, keeping what they wrote.
    private HeadlessRunResult Finished(HeadlessRunResult result, RunTrace? trace, RunCapture? capture)
    {
        if (!Development.IsSupported)
        {
            return result;
        }

        long finalTick = result.ExitRequested ? result.Steps - 1 : result.Steps;
        trace?.Finish(finalTick);
        capture?.Finish(finalTick);
        if (_commandLineTrace is var (path, _, _) && trace?.Ticks.HaltedAt is not null)
        {
            throw CommandLine.Refuse(_gameName, trace.Ticks.HaltMessage($"'{path}'"));
        }

        if (_commandLineCapture is var (directory, _) && capture?.Ticks.HaltedAt is not null)
        {
            throw CommandLine.Refuse(_gameName, capture.Ticks.HaltMessage($"'{directory}'"));
        }

        return result;
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

    // Saves the run into Recent and logs where, named for sceneKey: its scene's, or its opening's when it crashed
    // before its host was built. A replay never handed over to live input is the recording it replayed,
    // which is saved already. A failed write masks neither the run's end nor its crash.
    private void SaveRecent(Recorder? recorder, Replay? replay, string sceneKey, bool crashed)
    {
        if (!Development.IsSupported || recorder is null || replay is { HandedOver: false })
        {
            return;
        }

        if (RecordingFiles.SaveRecent(recorder, RecordingsDirectory(), sceneKey, crashed) is { } path)
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
}
