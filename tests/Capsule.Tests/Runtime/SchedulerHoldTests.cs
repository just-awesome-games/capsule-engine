using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

public sealed class SchedulerHoldTests
{
    private const double StepSeconds = 0.1;
    private static readonly InputAction HeldAction = new("held");

    [Fact]
    public void Hold_DropsAccumulatedTimeAndResumesWithOneStepAndNoHeldPress()
    {
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = new(
            StepSeconds,
            5,
            new ActionBindings().Bind(HeldAction, Key.Space));

        scheduler.Advance(StepSeconds, DeviceSnapshot.Of(Key.Space), simulation);
        simulation.Steps.Clear();

        scheduler.Advance(StepSeconds / 2, DeviceSnapshot.Of(Key.Space), simulation);
        scheduler.Held = true;

        scheduler.Advance(0.7, DeviceSnapshot.Of(Key.Space), simulation);
        scheduler.Advance(0.7, DeviceSnapshot.Of(Key.Space), simulation);

        Assert.Empty(simulation.Steps);
        Assert.Equal(1, scheduler.Tick);
        Assert.Equal(0, scheduler.StepsThisFrame);
        Assert.Equal(0, scheduler.AccumulatorSeconds);
        Assert.Equal(1f, scheduler.InterpolationAlpha);

        scheduler.Held = false;
        scheduler.Advance(StepSeconds, DeviceSnapshot.Of(Key.Space), simulation);

        Assert.Single(simulation.Steps);
        Assert.Equal(1, simulation.Steps[0].Tick);
        Assert.False(simulation.Steps[0].Pressed);
        Assert.True(simulation.Steps[0].Held);
        Assert.Equal(2, scheduler.Tick);
        Assert.Equal(1, scheduler.StepsThisFrame);
    }

    [Fact]
    public void StepOnce_RunsOneTickWhileHeldOnTheSampledSnapshotAndLeavesTheHoldInPlace()
    {
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = new(
            StepSeconds,
            5,
            new ActionBindings().Bind(HeldAction, Key.Space));

        Assert.Throws<InvalidOperationException>(() => scheduler.StepOnce(DeviceSnapshot.Empty, simulation));

        scheduler.Advance(StepSeconds / 2, DeviceSnapshot.Empty, simulation);
        scheduler.Held = true;

        Assert.False(scheduler.StepOnce(DeviceSnapshot.Of(Key.Space), simulation));

        RecordedStep step = Assert.Single(simulation.Steps);
        Assert.Equal(0, step.Tick);
        Assert.True(step.Pressed);
        Assert.Equal(1, scheduler.Tick);
        Assert.Equal(1, scheduler.StepsThisFrame);
        Assert.Equal(0, scheduler.AccumulatorSeconds);
        Assert.Equal(1f, scheduler.InterpolationAlpha);
        Assert.True(scheduler.Held);

        scheduler.Advance(StepSeconds, DeviceSnapshot.Of(Key.Space), simulation);

        Assert.Single(simulation.Steps);
        Assert.Equal(0, scheduler.StepsThisFrame);
    }

    [Fact]
    public void StepOnce_UnderADriverTakesTheDriversNextSnapshotAndEndsWithIt()
    {
        RecordingDriver driver = new();
        using SceneHost host = CreateHost();
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = new(
            StepSeconds,
            5,
            new ActionBindings(),
            driver,
            host);
        scheduler.Held = true;

        Assert.False(scheduler.StepOnce(DeviceSnapshot.Of(Key.Space), simulation));
        Assert.Equal([0], driver.Ticks);
        Assert.Equal(1, scheduler.Tick);

        driver.Finished = true;

        Assert.True(scheduler.StepOnce(DeviceSnapshot.Empty, simulation));
        Assert.Equal(1, scheduler.Tick);
        Assert.Equal(0, scheduler.StepsThisFrame);
        Assert.Single(simulation.Steps);
    }

    [Fact]
    public void Hold_DoesNotAskADriverForStepsAndResumesAtTheNextTick()
    {
        RecordingDriver driver = new();
        using SceneHost host = CreateHost();
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = new(
            StepSeconds,
            5,
            new ActionBindings(),
            driver,
            host);

        scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, simulation);
        scheduler.Held = true;
        scheduler.Advance(0.8, DeviceSnapshot.Empty, simulation);
        scheduler.Advance(0.8, DeviceSnapshot.Empty, simulation);

        Assert.Equal([0L], driver.Ticks);
        Assert.Equal(0, scheduler.StepsThisFrame);

        scheduler.Held = false;
        scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, simulation);

        Assert.Equal([0L, 1L], driver.Ticks);
        Assert.Equal(1, simulation.Steps[^1].Tick);
        Assert.Equal(2, scheduler.Tick);
    }

    private static SceneHost CreateHost() =>
        new(
            SceneTransition.ToScene(typeof(DriverScene), null),
            static (in SceneTransition _) => new DriverScene(),
            new Run());

    private sealed class DriverScene : Scene;

    private sealed class RecordingDriver : IInputDriver
    {
        internal List<long> Ticks { get; } = [];

        internal bool Finished { get; set; }

        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            snapshot = DeviceSnapshot.Empty;
            if (Finished)
            {
                return false;
            }

            Ticks.Add(tick);

            return true;
        }
    }

    private sealed class RecordingSimulation : ISimulation
    {
        public List<RecordedStep> Steps { get; } = [];

        public bool ExitRequested => false;

        public FrameView View { get; } = new();

        public void Step(in StepContext context) =>
            Steps.Add(new RecordedStep(
                context.Tick,
                context.Input.WasPressed(HeldAction),
                context.Input.IsHeld(HeldAction)));
    }

    private readonly record struct RecordedStep(long Tick, bool Pressed, bool Held);
}
