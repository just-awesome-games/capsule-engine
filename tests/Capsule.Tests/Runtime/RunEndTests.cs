using Capsule.Input;
using Capsule.Persistence;
using Capsule.Runtime;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Scenes;
using Capsule.Tests.Persistence;
using static System.FormattableString;

namespace Capsule.Tests.Runtime;

// The run-end hooks run once, in order, after the last step and the last scene's stop, however a run
// that does not crash ends. What they write is persisted before the run returns.
[Collection(LogSinkCollection.Name)]
public sealed class RunEndTests
{
    private const int Hertz = 10;

    private static readonly SaveKey<int> Length = new("length", SaveTestJsonContext.Default.Int32, -1);

    // The exit the step of tick 1 requests ends the run at two steps. With no exit the script runs out at three.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHeadlessRunThatExitsOrRunsOutOfInput_EndsOnceAfterItsSceneStops_AndPersistsWhatTheHookWrote(bool exits)
    {
        using TempWorkspace workspace = new(nameof(RunEndTests));
        List<string> events = [];

        HeadlessRunResult result = Builder(events, () => new Ending(events, exitAt: exits ? 1 : null))
            .WithSaveDirectory(workspace.Root)
            .RunHeadless<Ending>(new InputScript().Wait(3).Build());

        Assert.Equal(exits ? 2 : 3, result.Steps);
        Assert.Equal(["stop", Ended(result.Steps), "second"], events);

        int read = 0;
        Builder([], () => new Ending([]))
            .WithSaveDirectory(workspace.Root)
            .WithRunStart(run => read = run.Saves.Read(Length))
            .RunHeadless<Ending>(new InputScript().Build());

        Assert.Equal(result.Steps, read);
    }

    [Fact]
    public void ARunWhoseStepThrows_EndsWithoutTheHook()
    {
        List<string> events = [];

        Assert.Throws<InvalidOperationException>(
            () => Builder(events, () => new Ending(events, throws: true)).RunHeadless<Ending>(new InputScript().Wait(3).Build()));

        Assert.Equal(["stop"], events);
    }

    // A windowed run whose window closes, with no exit requested. Its recording is saved before the hook
    // draws from the run's random source, and the replay of it does not diverge. The replay ends the same way.
    [Fact]
    public void ARecordedRunWhoseWindowCloses_EndsAfterSavingItsRecording_AndItsReplayEndsTheSameWay()
    {
        using TempWorkspace workspace = new("recordings");
        List<string> events = [];
        EngineBuilder builder = Builder(events, () => new Ending(events))
            .WithRunEnd(static run => run.Random.NextFloat())
            .WithSaveStorage(new RecordedSaveStorage([]))
            .WithCommandLine(["--recordings", workspace.Root]);

        builder.RunRecorded(SceneTransition.ToScene(typeof(Ending), null), replayed: null, (host, recorder, _) =>
        {
            FixedStepScheduler scheduler = new(builder.StepSeconds, 8, builder.Input.Bindings, scenes: host, recorder: recorder);
            for (int step = 0; step < 3; step++)
            {
                scheduler.Advance(builder.StepSeconds, DeviceSnapshot.Empty, host);
            }
        });

        Assert.Equal(["stop", Ended(3), "second"], events);

        Recording recording = Recording.Load(Assert.Single(Directory.GetFiles(Path.Combine(workspace.Root, "Recent"))));
        HeadlessRunResult replayed = builder.RunHeadless(recording);

        Assert.Null(replayed.DivergedAt);
        Assert.Equal(["stop", Ended(3), "second", "stop", Ended(3), "second"], events);
    }

    // Two hooks, which run in the order they were added. The first reads the run's clock, and the second
    // writes the run's length in steps.
    private static EngineBuilder Builder(List<string> events, Func<Ending> scene) =>
        CapsuleEngine.Configure(
                "Ending Game",
                new SceneRegistry(new EntityRegistry([]), [SceneRegistration.Plain(typeof(Ending), _ => scene())]))
            .WithFixedStep(Hertz)
            .WithoutCrashLog()
            .WithoutLogging()
            .WithRunEnd(run => events.Add(Invariant($"end at {run.Tick}, {run.TotalSeconds} s")))
            .WithRunEnd(run =>
            {
                events.Add("second");
                run.Saves.Write(Length, (int)run.Tick);
            });

    private static string Ended(long steps) => Invariant($"end at {steps}, {steps * (1.0 / Hertz)} s");

    // Requests exit at exitAt, or throws from its first step, and reports its stop.
    private sealed class Ending(List<string> events, long? exitAt = null, bool throws = false) : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            if (throws)
            {
                throw new InvalidOperationException("Ending was told to fail its step.");
            }

            if (context.Tick == exitAt)
            {
                Run.RequestExit();
            }
        }

        protected override void OnStop() => events.Add("stop");
    }
}
