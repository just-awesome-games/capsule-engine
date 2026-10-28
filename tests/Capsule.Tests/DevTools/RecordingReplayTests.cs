using System.Numerics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Scenes;
using static Capsule.Tests.DevTools.RecordingFixtures;

namespace Capsule.Tests.DevTools;

// A recorded run replays to the checksums it recorded, and a replay that strays reports where. A
// replay with divergences logs them to the process-wide sink.
[Collection(LogSinkCollection.Name)]
public sealed class RecordingReplayTests
{
    private const int Steps = 60;

    [Fact]
    public void ARecordedRun_ReplaysHeadlessWithoutDiverging()
    {
        using RecordedRun run = new(Builder());
        for (int step = 0; step < Steps; step++)
        {
            run.Step(Varied(run, step));
        }

        Vector2 recorded = ((Reading)run.Host.Scene).Walker.Position;
        Vector2 replayed = default;

        HeadlessRunResult result = Builder().RunHeadless(run.Saved(), (scene, _) => replayed = ((Reading)scene).Walker.Position);

        Assert.Null(result.DivergedAt);
        Assert.Equal(Steps, result.Steps);
        Assert.Equal(recorded, replayed);
    }

    // The extra draw lands in the step of tick 25. The checkpoint at 20 still matches, the one at 30 is
    // the first after it, and the replay plays on to its end.
    [Fact]
    public void AReplayAgainstPerturbedBehaviour_DivergesByTheFirstCheckpointAfterIt()
    {
        using RecordedRun run = new(Builder());
        for (int step = 0; step < Steps; step++)
        {
            run.Step(Varied(run, step));
        }

        HeadlessRunResult result = Builder(() => new Reading { ExtraDrawAt = 25 }).RunHeadless(run.Saved());

        Assert.Equal(30, result.DivergedAt);
        Assert.Equal(Steps, result.Steps);
    }

    // The exit the step of tick 15 requests brings the run to tick 16, short of the recording's end.
    [Fact]
    public void AReplayWhoseGameExitsEarly_DivergesAtTheExit()
    {
        using RecordedRun run = new(Builder());
        for (int step = 0; step < Steps; step++)
        {
            run.Step(Varied(run, step));
        }

        HeadlessRunResult result = Builder(() => new Reading { ExitAt = 15 }).RunHeadless(run.Saved());

        Assert.Equal(16, result.DivergedAt);
        Assert.True(result.ExitRequested);
    }

    [Fact]
    public void ARunEndedThroughTheOverlaysExit_ReplaysItsExit()
    {
        using RecordedRun run = new(Builder());
        using OverlayRig rig = new(run.Host, scheduler: run.Scheduler);
        rig.Frame();
        rig.Frame();
        rig.Open();
        rig.Press(Key.E);
        Assert.True(run.Host.ExitRequested);

        HeadlessRunResult result = Builder().RunHeadless(run.Saved());

        Assert.Null(result.DivergedAt);
        Assert.True(result.ExitRequested);
        Assert.Equal(run.Scheduler.Tick, result.Steps);
    }

    // A command chosen at a finished replay's hold is the run's first live step. The saved run holds the
    // replayed steps, the command and the live steps after it.
    [Fact]
    public void ACommandAtAFinishedReplaysHold_HandsOverAndReplaysInTheSavedRun()
    {
        using RecordedRun first = new(Builder());
        for (int step = 0; step < 25; step++)
        {
            first.Step(Varied(first, step));
        }

        using RecordedRun second = new(Builder(), first.Saved());
        using OverlayRig rig = new(second.Host, scheduler: second.Scheduler);
        while (second.Scheduler.Replay is { Finished: false })
        {
            rig.Frame();
        }

        rig.Overlay.HoldAt("Replay matched");
        rig.OpenEntityPanel("Walker");
        rig.SelectRow("Nudge");

        Assert.Null(second.Scheduler.Replay);
        Assert.Equal(26, second.Scheduler.Tick);

        rig.Press(Key.Grave);
        rig.Frame();
        Vector2 recorded = ((Reading)second.Host.Scene).Walker.Position;
        Vector2 replayed = default;

        HeadlessRunResult result = Builder().RunHeadless(second.Saved(), (scene, _) => replayed = ((Reading)scene).Walker.Position);

        Assert.Null(result.DivergedAt);
        Assert.Equal(second.Scheduler.Tick, result.Steps);
        Assert.Equal(recorded, replayed);
    }

    // The run's host is never built, and the recording holds its boot state under the opening's name.
    [Fact]
    public void ARunWhoseOpeningSceneFailsToStart_StillSavesItsBootIntoRecent()
    {
        using TempWorkspace workspace = new("recordings");
        EngineBuilder builder = Builder(() => new Reading { ThrowsOnStart = true })
            .WithSaveStorage(new RecordedSaveStorage([]))
            .WithCommandLine(["--recordings", workspace.Root]);

        Assert.Throws<InvalidOperationException>(
            () => builder.RunRecorded(SceneTransition.ToScene(typeof(Reading), null), replayed: null, static (_, _, _) => { }));

        string saved = Assert.Single(Directory.GetFiles(Path.Combine(workspace.Root, "Recent")));
        Assert.StartsWith("Reading ", Path.GetFileName(saved), StringComparison.Ordinal);
        Recording recording = Recording.Load(saved);
        Assert.Equal(0, recording.End.FinalTick);
        Assert.Null(recording.End.Checksum);
    }

    [Fact]
    public void AShellWhoseCanvasDiffers_IsRefusedNamingTheSetting()
    {
        using RecordedRun run = new(Builder());
        run.Step(DeviceSnapshot.Empty);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => Builder().WithCanvas(100, 100).RunHeadless(run.Saved()));

        Assert.Contains("canvas", refused.Message, StringComparison.Ordinal);
        Assert.Contains("100x100", refused.Message, StringComparison.Ordinal);
    }

    // A panel command and a G move taken through the overlay replay at their ticks, inside their steps.
    [Fact]
    public void ARecordedMoveAndPanelCommand_ReplayToTheSameChecksum()
    {
        using RecordedRun run = new(Builder());
        using OverlayRig rig = new(run.Host, scheduler: run.Scheduler);
        rig.Scheduler.Output = OverlayFixtures.Output;
        rig.Frame();
        rig.OpenEntityPanel("Walker");
        rig.SelectRow("Nudge");
        rig.PlaceWorld();
        rig.Frame(DeviceSnapshot.Of(Key.G).WithPointer(new Vector2(700f, 300f)));
        rig.Frame(DeviceSnapshot.Empty.WithPointer(new Vector2(700f, 300f)));
        Assert.Contains("Moved Walker", rig.Overlay.Scene.Status, StringComparison.Ordinal);
        rig.Press(Key.Grave);
        rig.Frame();

        Vector2 recorded = ((Reading)run.Host.Scene).Walker.Position;
        Vector2 replayed = default;

        HeadlessRunResult result = Builder().RunHeadless(run.Saved(), (replaying, _) => replayed = ((Reading)replaying).Walker.Position);

        Assert.Null(result.DivergedAt);
        Assert.Equal(recorded, replayed);
    }

    // W writes the run so far beside Recent, never into it, and the status line says where.
    [Fact]
    public void SaveRecording_WritesTheRunSoFarAndSaysWhere()
    {
        using TempWorkspace workspace = new("recordings");
        using OverlayRig rig = new(recordings: workspace.Root);
        rig.Frame();
        rig.Frame();
        rig.Open();

        rig.Press(Key.W);

        string saved = Assert.Single(Directory.GetFiles(workspace.Root));
        Assert.Equal($"Saved 2 ticks to {saved}", rig.Overlay.Scene.Status);
        Assert.Equal(2, Recording.Load(saved).End.FinalTick);
    }

    // An automatic save keeps Recent to the newest twenty by write time and leaves the developer's own
    // saves beside it alone.
    [Fact]
    public void AnAutomaticSave_PrunesRecentToTheNewestTwentyAndLeavesRecordingsAlone()
    {
        using TempWorkspace workspace = new("recordings");
        using RecordedRun run = new(Builder());
        run.Step(DeviceSnapshot.Empty);
        string mine = workspace.PathTo("Mine.recording");
        File.WriteAllBytes(mine, []);
        DateTime oldest = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(mine, oldest);
        for (int index = 0; index < 25; index++)
        {
            string old = workspace.PathTo($"Recent/Old {index:00}.recording");
            File.WriteAllBytes(old, []);
            File.SetLastWriteTimeUtc(old, oldest.AddMinutes(index));
        }

        string? saved = RecordingFiles.SaveRecent(run.Recorder, workspace.Root, "Reading", crashed: false);

        string[] kept = [.. Directory.GetFiles(Path.Combine(workspace.Root, "Recent")).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
        Assert.Equal(20, kept.Length);
        Assert.Contains(Path.GetFileName(saved), kept);
        Assert.DoesNotContain("Old 05.recording", kept);
        Assert.Contains("Old 06.recording", kept);
        Assert.True(File.Exists(mine));
    }
}
