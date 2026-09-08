using System.Numerics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Scenes.Input;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class InputDriverPlaybackTests
{
    private const double StepSeconds = 0.1;
    private const int ScriptSteps = 6;

    private static readonly InputAction Jump = new("Jump");

    // A driver is asked once per fixed step, so how many frames the host drew to reach a step
    // cannot change what that step read.
    [Fact]
    public void Advance_UnderADriver_RunsTheSameStepSequenceAtAnyFrameCadence()
    {
        Assert.Equal(Play(framesPerStep: 1), Play(framesPerStep: 7));
    }

    [Fact]
    public void Advance_UnderADriver_EndsTheRunOnceTheDriverIsFinished()
    {
        (FixedStepScheduler scheduler, SceneHost host, Recording scene) = Driven(Script());

        using (host)
        {
            for (int step = 0; step < ScriptSteps; step++)
            {
                Assert.False(scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, host));
            }

            // The finish is the driver's answer to the ask for the step after its last, so it ends
            // the run on the advance that would have run that step.
            Assert.True(scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, host));
            Assert.Equal(ScriptSteps, scene.Presses.Count);
        }
    }

    [Fact]
    public void Advance_UnderADriver_IgnoresTheSampledDevice()
    {
        (FixedStepScheduler scheduler, SceneHost host, Recording scene) = Driven(Script());

        using (host)
        {
            scheduler.Advance(StepSeconds, DeviceSnapshot.Of(Key.Space), host);

            Assert.False(scene.Presses[0]);
        }
    }

    [Fact]
    public void RunHeadless_HonoursATransitionAndTheExitTheSecondSceneRequests()
    {
        // Two taps: the first leaves Menu for Room, the second exits from Room. The trailing steps
        // are what proves the run stopped at the exit rather than at the driver's last step.
        IInputDriver driver = new InputScript().Tap(Key.Space).Wait(1).Tap(Key.Space).Wait(5).Build();

        HeadlessRunResult result = Builder().RunHeadless<Menu>(driver);

        Assert.Equal(3, result.Steps);
        Assert.True(result.ExitRequested);
    }

    [Fact]
    public void RunHeadless_RunsEveryStepTheDriverSuppliesWhenNoSceneExits()
    {
        HeadlessRunResult result = Builder().RunHeadless<Idle>(Script());

        Assert.Equal(ScriptSteps, result.Steps);
        Assert.False(result.ExitRequested);
    }

    [Fact]
    public void RunHeadless_OfADriverThatSuppliesNothing_RunsNothing()
    {
        HeadlessRunResult result = Builder().RunHeadless<Idle>(new InputScript().Build());

        Assert.Equal(0, result.Steps);
        Assert.False(result.ExitRequested);
    }

    // The point of a driver over a fixed sequence: it reads the world it is playing, so a press
    // lands on the step the game reached a state rather than on a step counted in advance.
    [Fact]
    public void RunHeadless_UnderADriverReadingTheScene_PressesOnTheStepTheWorldReachesItsCue()
    {
        HeadlessRunResult result = Builder().RunHeadless<Patrol>(new WaitForTheWalker());

        // The walker starts at 0 and moves one unit a step, so the fourth step is the first the
        // driver sees it past 3, and the press that step exits the run.
        Assert.Equal(5, result.Steps);
        Assert.True(result.ExitRequested);
    }

    private static IInputDriver Script() =>
        new InputScript()
            .Wait(2)
            .Tap(Key.Space)
            .Down(Key.Space)
            .Wait(2)
            .Up(Key.Space)
            .Wait(1)
            .Build();

    private static (FixedStepScheduler Scheduler, SceneHost Host, Recording Scene) Driven(IInputDriver driver)
    {
        Recording scene = new();
        SceneHost host = new(SceneTransition.ToScene(typeof(Recording), null), (in SceneTransition _) => scene);

        return (new FixedStepScheduler(StepSeconds, 32, new ActionBindings().Bind(Jump, Key.Space), driver, host), host, scene);
    }

    private static SceneEngineBuilder Builder() =>
        CapsuleEngine.Configure(
                "Driven Game",
                new SceneRegistry(
                    new EntityRegistry([]),
                    [
                        SceneRegistration.Plain(typeof(Menu), static () => new Menu()),
                        SceneRegistration.Plain(typeof(Room), static () => new Room()),
                        SceneRegistration.Plain(typeof(Idle), static () => new Idle()),
                        SceneRegistration.Plain(typeof(Patrol), static () => new Patrol()),
                    ]))
            .WithFixedStep(10)
            .WithBindings(static bindings => bindings.Bind(Jump, Key.Space))
            .WithoutCrashLog()
            .WithoutLogging();

    // The whole run's press pattern, which is what "the same step sequence" means for a driver.
    private static List<bool> Play(int framesPerStep)
    {
        (FixedStepScheduler scheduler, SceneHost host, Recording scene) = Driven(Script());

        using (host)
        {
            while (!scheduler.Advance(StepSeconds / framesPerStep, DeviceSnapshot.Empty, host))
            {
            }

            return scene.Presses;
        }
    }

    private sealed class Recording : Scene
    {
        internal List<bool> Presses { get; } = [];

        protected override void OnStep(in StepContext context) => Presses.Add(context.Input.WasPressed(Jump));
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

    // One entity marching right, and an exit on the press the driver decides to make.
    private sealed class Patrol : Scene
    {
        internal Walker Walker { get; } = new();

        protected override void OnStart() => Add(Walker);

        protected override void OnStep(in StepContext context)
        {
            if (context.Input.WasPressed(Jump))
            {
                RequestExit();
            }
        }
    }

    private sealed class Walker() : Entity(Vector2.Zero)
    {
        protected internal override void OnStep(in StepContext context) =>
            Teleport(Position + Vector2.UnitX);
    }

    private sealed class WaitForTheWalker : IInputDriver
    {
        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            snapshot = scene is Patrol patrol && patrol.Walker.Position.X > 3f
                ? DeviceSnapshot.Of(Key.Space)
                : DeviceSnapshot.Empty;

            return tick < 100;
        }
    }
}
