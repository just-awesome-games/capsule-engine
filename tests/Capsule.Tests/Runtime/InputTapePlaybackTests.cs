using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Input;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class InputTapePlaybackTests : IDisposable
{
    private const double StepSeconds = 0.1;
    private static readonly InputAction Jump = new("Jump");

    private readonly string _directory =
        Directory.CreateTempSubdirectory(nameof(InputTapePlaybackTests)).FullName;

    private static InputTape Tape { get; } = new InputScript()
        .Wait(2)
        .Tap(Key.Space)
        .Down(Key.Space)
        .Wait(2)
        .Up(Key.Space)
        .Wait(1)
        .Build();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // A tape is one snapshot per fixed step, so how many frames the host drew to reach a step
    // cannot change what that step read.
    [Fact]
    public void Advance_UnderATape_RunsTheSameStepSequenceAtAnyFrameCadence()
    {
        Assert.Equal(Play(framesPerStep: 1), Play(framesPerStep: 7));
    }

    [Fact]
    public void Advance_UnderATape_EndsTheRunAfterItsLastStep()
    {
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = CreateScheduler(Tape);

        for (int step = 0; step < Tape.Count - 1; step++)
        {
            Assert.False(scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, simulation));
        }

        // The run ends on the frame that runs the last step, not on a further empty one.
        Assert.True(scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, simulation));
        Assert.Equal(Tape.Count, simulation.Steps.Count);
    }

    [Fact]
    public void Advance_UnderATape_IgnoresTheSampledDevice()
    {
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = CreateScheduler(Tape);

        scheduler.Advance(StepSeconds, DeviceSnapshot.Of(Key.Space), simulation);

        Assert.False(simulation.Steps[0]);
    }

    [Fact]
    public void WithInputRecording_OverAReplay_WritesBackTheTapeItReplayed()
    {
        string path = Path.Combine(_directory, "run.tape");

        HeadlessRunResult result = Builder()
            .WithInputRecording(path)
            .RunHeadless<Idle>(Tape);

        Assert.Equal(Tape.Count, result.Steps);

        using FileStream recorded = File.OpenRead(path);
        Assert.Equal(Tape, InputTapeFile.Read(recorded));
    }

    [Fact]
    public void RunHeadless_HonoursATransitionAndTheExitTheSecondSceneRequests()
    {
        // Two taps: the first leaves Menu for Room, the second exits from Room. The trailing steps
        // are what proves the run stopped at the exit rather than at the tape's end.
        InputTape tape = new InputScript().Tap(Key.Space).Wait(1).Tap(Key.Space).Wait(5).Build();

        HeadlessRunResult result = Builder().RunHeadless<Menu>(tape);

        Assert.Equal(3, result.Steps);
        Assert.True(result.ExitRequested);
    }

    [Fact]
    public void RunHeadless_RunsEverySnapshotWhenNoSceneExits()
    {
        HeadlessRunResult result = Builder().RunHeadless<Idle>(Tape);

        Assert.Equal(Tape.Count, result.Steps);
        Assert.False(result.ExitRequested);
    }

    [Fact]
    public void RunHeadless_OfAnEmptyTape_RunsNothing()
    {
        HeadlessRunResult result = Builder().RunHeadless<Idle>(InputTape.Empty);

        Assert.Equal(0, result.Steps);
        Assert.False(result.ExitRequested);
    }

    private static FixedStepScheduler CreateScheduler(InputTape tape) =>
        new(StepSeconds, 32, new ActionBindings().Bind(Jump, Key.Space), tape);

    private static SceneEngineBuilder Builder() =>
        CapsuleEngine.Configure(
                "Tape Game",
                new SceneRegistry(
                    new EntityRegistry([]),
                    [
                        SceneRegistration.Plain(typeof(Menu), static () => new Menu()),
                        SceneRegistration.Plain(typeof(Room), static () => new Room()),
                        SceneRegistration.Plain(typeof(Idle), static () => new Idle()),
                    ]))
            .WithFixedStep(10)
            .WithBindings(static bindings => bindings.Bind(Jump, Key.Space))
            .WithoutCrashLog()
            .WithoutLogging();

    // The whole run's press pattern, which is what "the same step sequence" means for a tape.
    private static List<bool> Play(int framesPerStep)
    {
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = CreateScheduler(Tape);

        while (!scheduler.Advance(StepSeconds / framesPerStep, DeviceSnapshot.Empty, simulation))
        {
        }

        return simulation.Steps;
    }

    private sealed class RecordingSimulation : ISimulation
    {
        public List<bool> Steps { get; } = [];

        public bool ExitRequested => false;

        public FrameView View { get; } = new();

        public void Step(in StepContext context) => Steps.Add(context.Input.WasPressed(Jump));
    }

    private sealed class Menu : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            if (context.Input.WasPressed(Jump))
            {
                RequestScene<Room>();
            }
        }
    }

    private sealed class Room : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            if (context.Input.WasPressed(Jump))
            {
                RequestExit();
            }
        }
    }

    private sealed class Idle : Scene;
}
