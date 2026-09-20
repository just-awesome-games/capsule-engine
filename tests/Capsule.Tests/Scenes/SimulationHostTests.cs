using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

public sealed class SimulationHostTests
{
    private static readonly InputAction Jump = new("Jump");

    [Fact]
    public void TheTickCountsOnAcrossEveryWayOfStepping()
    {
        List<long> ticks = [];
        void Hook(Scene scene, in StepContext context) => ticks.Add(context.Tick);

        using SimulationHost run = new(new SceneFixtures.HookScene(step: Hook));

        run.Step(2);

        // A script is positional, so a five-step one handed a run at tick 2 serves 2, 3 and 4.
        run.Play(new InputScript().Wait(5).Build());
        run.Step();

        Assert.Equal([0L, 1, 2, 3, 4, 5], ticks);
        Assert.Equal(6, run.Tick);
    }

    [Fact]
    public void Run_IsTheSimulationsRun_AndStepCountAdvancesThatManyTicks()
    {
        List<long> ticks = [];
        void Hook(Scene scene, in StepContext context) => ticks.Add(context.Tick);
        Run configured = new();

        using SimulationHost host = new(new SceneFixtures.HookScene(step: Hook), run: configured);

        Assert.Same(configured, host.Run);

        host.Step(3);

        Assert.Equal([0L, 1, 2], ticks);
        Assert.Equal(3, host.Tick);
    }

    // The rate a caller builds a run with is the one a distance is measured against, so it has to
    // be both readable and what the scene is actually stepped at.
    [Fact]
    public void TheStepLengthTheRunWasBuiltWith_IsWhatItReportsAndWhatTheSceneSees()
    {
        List<float> deltas = [];
        void Hook(Scene scene, in StepContext context) => deltas.Add(context.DeltaSeconds);

        using SimulationHost run = new(new SceneFixtures.HookScene(step: Hook), stepHertz: 120);

        run.Step(2);

        Assert.Equal(1.0 / 120.0, run.StepSeconds);
        Assert.Equal([1f / 120f, 1f / 120f], deltas);
    }

    // Constructing the run starts the scene, so a rate rejected afterwards would strand it started
    // with no run holding it to stop, and every retry over it would be refused.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ARejectedStepRate_LeavesTheSceneUnstarted_AndStillWorthARun(int stepHertz)
    {
        SceneFixtures.HookScene scene = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationHost(scene, stepHertz: stepHertz));
        Assert.Equal(0, scene.Starts);

        using SimulationHost run = new(scene);
        run.Step();

        Assert.Equal(1, scene.Starts);
        Assert.Equal(1, run.Tick);
    }

    // The whole point of one input for the run: an edge is the difference between two steps, and a
    // run that minted a state per step would report a held button as pressed on every one of them.
    [Fact]
    public void OneInputStateSpansTheRun_SoAHeldButtonPressesOnce()
    {
        List<bool> presses = [];
        void Hook(Scene scene, in StepContext context) => presses.Add(context.Input.WasPressed(Jump));

        Run configured = new();
        configured.Input.Bindings.Bind(Jump, Key.Space);

        using SimulationHost run = new(new SceneFixtures.HookScene(step: Hook), run: configured);

        run.Step(3, DeviceSnapshot.Of(Key.Space));

        Assert.Equal([true, false, false], presses);
    }

    // The done-when for runtime rebinding: a write to Run.Input.Bindings reaches the very next
    // snapshot, because the host's InputState and the run share one ActionBindings instance.
    [Fact]
    public void RebindingMidRun_IsHonouredByTheNextSnapshot()
    {
        List<bool> presses = [];
        void Hook(Scene scene, in StepContext context) => presses.Add(context.Input.WasPressed(Jump));

        Run configured = new();
        configured.Input.Bindings.Bind(Jump, Key.Space);

        using SimulationHost run = new(new SceneFixtures.HookScene(step: Hook), run: configured);

        run.Step(DeviceSnapshot.Of(Key.Space));

        run.Run.Input.Bindings.Rebind(Jump, Key.F);

        run.Step(DeviceSnapshot.Of(Key.F));
        run.Step(DeviceSnapshot.Of(Key.Space));

        Assert.Equal([true, true, false], presses);
    }

    [Fact]
    public void PlayEndsOnTheExitRequest_WithStepsLeftInTheDriver()
    {
        using SimulationHost run = new(new ExitsOnTick(2));

        run.Play(new InputScript().Wait(10).Build());

        Assert.True(run.Simulation.ExitRequested);
        Assert.Equal(3, run.Tick);
    }

    // The host reads the exit after the step, never before one, so a scene that gives up during its
    // start still runs the step the driver had already supplied. A Play that checked first would
    // report a run of no steps where RunHeadless reports one.
    [Fact]
    public void PlayRunsTheDriversFirstStep_EvenWhenTheSceneExitedFromItsStart()
    {
        using SimulationHost run = new(new ExitsOnStart());

        run.Play(new InputScript().Wait(10).Build());

        Assert.True(run.Simulation.ExitRequested);
        Assert.Equal(1, run.Tick);
    }

    [Fact]
    public void RunUntil_StopsOnTheStepTheConditionFirstHolds()
    {
        SceneFixtures.Drifter drifter = new(Vector2.Zero);
        SceneFixtures.HookScene scene = new();
        scene.Add(drifter);

        using SimulationHost run = new(scene);

        Assert.True(run.RunUntil(() => drifter.Position.X >= 3f, 10));
        Assert.Equal(3, run.Tick);
    }

    [Fact]
    public void RunUntil_SpendsItsBudgetAndNoMore_WhenTheConditionNeverHolds()
    {
        using SimulationHost run = new(new SceneFixtures.HookScene());

        Assert.False(run.RunUntil(static () => false, 4));
        Assert.Equal(4, run.Tick);
    }

    private sealed class ExitsOnStart : Scene
    {
        protected override void OnStart() => Run.RequestExit();
    }

    private sealed class ExitsOnTick(long tick) : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            if (context.Tick == tick)
            {
                Run.RequestExit();
            }
        }
    }
}
