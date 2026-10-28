using System.Globalization;
using System.Numerics;
using System.Reflection;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime.DevTools.Overlay;
using Capsule.Runtime.Platform;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Runtime.DevTools.Recordings;

// Plays a recording's input back into the scheduler, one step at a time, with its output extent,
// refresh rate and host acts at the ticks they were recorded at. Each checkpoint is compared after
// its step. A divergence is reported and the replay plays on to its end, unless the game exits first.
internal sealed class Replay
{
    private readonly Recording _recording;
    private readonly SceneHost _scenes;
    private readonly SceneRegistry _registry;
    private readonly Recorder? _recorder;
    private readonly RecordingBody _body;
    private readonly DebugPanel _panel = new();

    // The tick the replay ends at.
    private readonly long _stopTick;

    private DeviceSnapshot _snapshot;
    private long _remaining;
    private Vector2 _output;
    private double _refreshRate;
    private int _checkpoint;
    private long? _matched;

    internal bool HoldsAtEnd { get; }

    internal bool Finished { get; private set; }

    // Whether a windowed replay was released at its end and the run played on live.
    internal bool HandedOver { get; set; }

    // The first tick a checkpoint or a host act disagreed at, or null.
    internal long? DivergedAt { get; private set; }

    // Told of each act as it applies, with the tick of the step it runs inside, its subject's name (empty for the
    // run) and what it does. Null unless a trace listens.
    internal Action<long, string, string>? Acted { get; set; }

    // recorder carries the replayed steps and acts into the live run's recording. A replay that holds
    // at its end stops short of a final step that exits, which leaves a run to resume.
    internal Replay(Recording recording, SceneHost scenes, SceneRegistry registry, Recorder? recorder, bool holdsAtEnd)
    {
        _recording = recording;
        _scenes = scenes;
        _registry = registry;
        _recorder = recorder;
        _body = new RecordingBody(new RecordingReader(recording.File, recording.BodyStart, "replay"));
        HoldsAtEnd = holdsAtEnd;
        _stopTick = holdsAtEnd && recording.End.Exited ? recording.End.FinalTick - 1 : recording.End.FinalTick;
    }

    internal string Outcome => DivergedAt is { } tick
        ? string.Create(CultureInfo.InvariantCulture, $"Replay diverged by tick {tick}")
        : "Replay matched";

    // The run's initial device and key layout, before its first step.
    internal void Seed(InputState input)
    {
        input.Seed(_recording.Header.Device);
        input.UseKeyLayout(_recording.Header.KeyLayout);
    }

    // The input of the step at tick, with the events recorded before it applied. act is the work of the
    // host acts to run inside the step. False once the replay has played to its end.
    internal bool TryNext(long tick, ref Vector2 output, out DeviceSnapshot snapshot, out Action? act)
    {
        snapshot = default;
        act = null;
        if (tick >= _stopTick)
        {
            Finished = true;

            return false;
        }

        while (_remaining == 0)
        {
            if (!_body.Next(out RecordingEvent read))
            {
                Finished = true;

                return false;
            }

            switch (read.Tag)
            {
                case RecordingFormat.Step:
                    _snapshot = read.Snapshot;
                    _remaining = read.Steps;
                    break;
                case RecordingFormat.Output:
                    _output = read.Output;
                    break;
                case RecordingFormat.RefreshRate:
                    _refreshRate = read.RefreshRate;
                    break;
                case RecordingFormat.Act:
                    act += Apply(in read.Act, tick);
                    break;
            }
        }

        _remaining--;
        output = _output;
        _scenes.Run.Display.RefreshRate = _refreshRate;
        snapshot = _snapshot;

        return true;
    }

    // After the step that brought the run to tick. A run that exits before the recording's end, or whose
    // exit at its end disagrees with the recording's, has diverged there.
    internal void Stepped(long tick)
    {
        RecordingEnd end = _recording.End;
        if (tick < end.FinalTick ? _scenes.ExitRequested : tick == end.FinalTick && _scenes.ExitRequested != end.Exited)
        {
            Log.Warning(string.Create(
                CultureInfo.InvariantCulture,
                $"replay {(_scenes.ExitRequested ? "exited" : "did not exit")} at tick {tick}, and the recording {(end.Exited ? "exited" : "did not exit")} at tick {end.FinalTick}"));
            Diverge(tick);
        }

        (long Tick, ulong Checksum)[] checkpoints = _recording.Checkpoints;
        while (_checkpoint < checkpoints.Length && checkpoints[_checkpoint].Tick <= tick)
        {
            (long at, ulong expected) = checkpoints[_checkpoint++];
            if (at != tick)
            {
                continue;
            }

            if (Recorder.Checksum(tick, _scenes) == expected)
            {
                _matched = tick;
            }
            else
            {
                Diverge(tick);
            }
        }
    }

    // The act's own work, run inside the step, or null for an act that is a transition request or
    // whose target is missing.
    private Action? Apply(in HostAct act, long tick)
    {
        Scene scene = _scenes.Scene;
        Action? work = null;
        string? missing = null;
        switch (act.Kind)
        {
            case HostActKind.Restart:
                missing = Request(SceneTransition.Restart(null, false)) ? null : "the restart was refused";
                break;
            case HostActKind.Exit:
                _scenes.Run.RequestExit();
                break;
            case HostActKind.Load:
                missing = Registration(act.Name!) is not { } target ? $"no registered scene is named '{act.Name}'"
                    : Request(target) ? null
                    : $"loading '{act.Name}' was refused";
                break;
            case HostActKind.Move:
                if (EntityPath.Find(scene, act.Path!) is { } moved and not ScreenEntity)
                {
                    Vector2 point = act.Point;
                    work = () => DebugOverlay.MoveTo(moved, point);
                    Acted?.Invoke(tick, moved.Name, string.Create(CultureInfo.InvariantCulture, $"Move to ({point.X}, {point.Y})"));
                }
                else
                {
                    missing = $"no world entity stands at tree path {string.Join('.', act.Path!)}";
                }

                break;
            case HostActKind.Command:
                work = Command(scene, in act);
                missing = work is null ? $"no panel command '{act.Name}' (occurrence {act.Occurrence}) is offered" : null;
                if (work is not null)
                {
                    Acted?.Invoke(tick, SubjectName(scene, in act), $"Command {act.Name}");
                }

                break;
        }

        if (act.Kind is HostActKind.Restart or HostActKind.Exit or HostActKind.Load && missing is null)
        {
            Acted?.Invoke(tick, string.Empty, act.Kind == HostActKind.Load ? $"Load {act.Name}" : act.Kind.ToString());
        }

        if (missing is not null)
        {
            Log.Warning(string.Create(CultureInfo.InvariantCulture, $"replay skipped the overlay's {act.Kind} at tick {tick}: {missing}"));
            Diverge(tick);

            return null;
        }

        _recorder?.Act(in act);

        return work;
    }

    private bool Request(in SceneTransition transition)
    {
        try
        {
            return _scenes.Run.TryRequest(in transition);
        }
        catch (Exception failure)
        {
            Log.Error($"replay's scene request failed: {failure.Message}");

            return false;
        }
    }

    private SceneTransition? Registration(string name)
    {
        foreach (SceneRegistration registration in _registry.Registrations)
        {
            if (registration.Name == name)
            {
                return registration.DocumentName is { } document
                    ? SceneTransition.ToName(document, null)
                    : SceneTransition.ToScene(registration.SceneType!, null);
            }
        }

        return null;
    }

    // The name a trace gives a command's panel: Scene, Camera or its entity's name.
    private static string SubjectName(Scene scene, in HostAct act) => act.Subject switch
    {
        PanelSubject.Scene => "Scene",
        PanelSubject.Camera => "Camera",
        _ => EntityPath.Find(scene, act.Path!)?.Name ?? string.Empty,
    };

    // Builds the subject's panel as the overlay does and finds the command or toggle it ran.
    private Action? Command(Scene scene, in HostAct act)
    {
        _panel.Clear();
        switch (act.Subject)
        {
            case PanelSubject.Scene:
                scene.RunDebugPanel(_panel);
                break;
            case PanelSubject.Camera:
                scene.Camera.RunDebugPanel(_panel);
                break;
            default:
                if (EntityPath.Find(scene, act.Path!) is not { } entity)
                {
                    return null;
                }

                entity.RunDebugPanel(_panel);
                break;
        }

        int seen = 0;
        foreach (DebugPanelRow row in _panel.Rows)
        {
            if (row.Kind is DebugPanelRowKind.Command or DebugPanelRowKind.Toggle && row.Label == act.Name && seen++ == act.Occurrence)
            {
                return row.Activate;
            }
        }

        return null;
    }

    private void Diverge(long tick)
    {
        if (DivergedAt is not null)
        {
            return;
        }

        DivergedAt = tick;
        string matched = _matched is { } at ? string.Create(CultureInfo.InvariantCulture, $"tick {at}") : "no checkpoint";
        Log.Warning(string.Create(CultureInfo.InvariantCulture, $"replay diverged by tick {tick} ({matched} matched)"));

        RecordingHeader header = _recording.Header;
        string game = BuildIdentity.Version(Assembly.GetEntryAssembly());
        string capsule = BuildIdentity.Version(typeof(Replay).Assembly);
        if (game != header.GameVersion || capsule != header.CapsuleVersion)
        {
            Log.Warning($"the recording was made by game {header.GameVersion} on Capsule {header.CapsuleVersion}, and replayed by game {game} on Capsule {capsule}");
        }
    }
}
