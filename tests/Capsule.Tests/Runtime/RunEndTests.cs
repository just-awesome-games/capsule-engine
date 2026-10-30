using Capsule.Input;
using Capsule.Persistence;
using Capsule.Runtime;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Scenes;
using Capsule.Tests.Persistence;
using static System.FormattableString;

namespace Capsule.Tests.Runtime;

// The game object ends once, after the last step and the last scene's stop, however a run that does not
// crash ends. What it writes is persisted before the run returns.
[Collection(LogSinkCollection.Name)]
public sealed class RunEndTests
{
    private const int Hertz = 10;

    private static readonly SaveKey<int> Length = new("length", SaveTestJsonContext.Default.Int32, -1);

    // The exit the step of tick 1 requests ends the run at two steps. With no exit the script runs out at three.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHeadlessRunThatExitsOrRunsOutOfInput_EndsOnceAfterItsSceneStops_AndPersistsWhatItsEndWrote(bool exits)
    {
        using TempWorkspace workspace = new(nameof(RunEndTests));
        List<string> events = [];

        HeadlessRunResult result = Builder(events, () => new Ending(events, exitAt: exits ? 1 : null))
            .WithSaveDirectory(workspace.Root)
            .RunHeadless<Ending>(new InputScript().Wait(3).Build());

        Assert.Equal(exits ? 2 : 3, result.Steps);
        Assert.Equal(["stop", Ended(result.Steps)], events);

        int read = 0;
        Registered(() => new Ending([]))
            .WithGame(CallbackGame.Starting(run => read = run.Saves.Read(Length)))
            .WithSaveDirectory(workspace.Root)
            .RunHeadless<Ending>(new InputScript().Build());

        Assert.Equal(result.Steps, read);
    }

    [Fact]
    public void ARunWhoseStepThrows_EndsWithoutTheGameObjectsEnd()
    {
        List<string> events = [];

        Assert.Throws<InvalidOperationException>(
            () => Builder(events, () => new Ending(events, throws: true)).RunHeadless<Ending>(new InputScript().Wait(3).Build()));

        Assert.Equal(["stop"], events);
    }

    // A windowed run whose window closes, with no exit requested. Its recording is saved before the game
    // object's end draws from the run's random source, and the replay of it does not diverge. The replay
    // ends the same way.
    [Fact]
    public void ARecordedRunWhoseWindowCloses_EndsAfterSavingItsRecording_AndItsReplayEndsTheSameWay()
    {
        using TempWorkspace workspace = new("recordings");
        List<string> events = [];
        EngineBuilder builder = Builder(events, () => new Ending(events))
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

        Assert.Equal(["stop", Ended(3)], events);

        Recording recording = Recording.Load(Assert.Single(Directory.GetFiles(Path.Combine(workspace.Root, "Recent"))));
        HeadlessRunResult replayed = builder.RunHeadless(recording);

        Assert.Null(replayed.DivergedAt);
        Assert.Equal(["stop", Ended(3), "stop", Ended(3)], events);
    }

    private static EngineBuilder Builder(List<string> events, Func<Ending> scene) =>
        Registered(scene).WithGame(run => new Timing(run, events));

    private static EngineBuilder Registered(Func<Ending> scene) =>
        CapsuleEngine.Configure(
                "Ending Game",
                new SceneRegistry(new EntityRegistry([]), [SceneRegistration.Plain(typeof(Ending), _ => scene())]))
            .WithFixedStep(Hertz)
            .WithoutCrashLog()
            .WithoutLogging();

    private static string Ended(long steps) => Invariant($"end at {steps}, {steps * (1.0 / Hertz)} s");

    // Reports the run's clock at its end, writes the run's length in steps, and draws from the run's random source.
    private sealed class Timing(Run run, List<string> events) : GameInstance(run)
    {
        protected override void OnEnd()
        {
            events.Add(Invariant($"end at {Run.Tick}, {Run.TotalSeconds} s"));
            Run.Saves.Write(Length, (int)Run.Tick);
            Run.Random.NextFloat();
        }
    }

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
