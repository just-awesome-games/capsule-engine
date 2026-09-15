using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

public sealed class FixedStepSchedulerTests
{
    private const double StepSeconds = 0.1;
    private static readonly InputAction Jump = new("Jump");

    [Fact]
    public void Advance_AccumulatesPartialFramesAndReportsInterpolation()
    {
        RecordingSimulation simulation = new(Jump);
        FixedStepScheduler scheduler = CreateScheduler();

        Assert.False(scheduler.Advance(0.04, DeviceSnapshot.Empty, simulation));
        Assert.False(scheduler.Advance(0.05, DeviceSnapshot.Empty, simulation));
        Assert.Empty(simulation.Recorded);
        Assert.Equal(0.9f, scheduler.InterpolationAlpha, 5);

        Assert.False(scheduler.Advance(0.02, DeviceSnapshot.Empty, simulation));
        Assert.Single(simulation.Recorded);
        Assert.Equal(0.01, scheduler.AccumulatorSeconds, 10);
        Assert.Equal(0.1f, scheduler.InterpolationAlpha, 5);
    }

    // The spiral of death: a step costing more than the step length would otherwise queue two steps
    // next frame, then three, until every frame drains the whole backlog.
    [Fact]
    public void Advance_HoldsTheStepBoundWhenEveryFrameArrivesLateAndNeverCarriesABacklog()
    {
        RecordingSimulation simulation = new(Jump);
        FixedStepScheduler scheduler = CreateScheduler(maxStepsPerFrame: 3);
        const double FrameSecondsWorthOfSteps = StepSeconds * 4.5;

        for (int frame = 0; frame < 10; frame++)
        {
            simulation.Recorded.Clear();
            scheduler.Advance(FrameSecondsWorthOfSteps, DeviceSnapshot.Empty, simulation);

            Assert.Equal(3, simulation.Recorded.Count);
            Assert.Equal(0, scheduler.AccumulatorSeconds);
        }

        Assert.Equal(30, scheduler.Tick);
    }

    [Fact]
    public void Advance_SuppliesContiguousTicksAndDerivedTime()
    {
        RecordingSimulation simulation = new(Jump);
        FixedStepScheduler scheduler = CreateScheduler();

        scheduler.Advance(0.3, DeviceSnapshot.Empty, simulation);
        scheduler.Advance(0.1, DeviceSnapshot.Empty, simulation);

        Assert.Collection(
            simulation.Recorded,
            step => AssertStep(step, 0),
            step => AssertStep(step, 1),
            step => AssertStep(step, 2),
            step => AssertStep(step, 3));
        Assert.Equal(4, scheduler.Tick);
    }

    [Fact]
    public void Advance_StopsQueuedStepsImmediatelyWhenSimulationRequestsExit()
    {
        RecordingSimulation simulation = new(Jump) { ExitOnTick = 1 };
        FixedStepScheduler scheduler = CreateScheduler();

        Assert.True(scheduler.Advance(0.5, DeviceSnapshot.Empty, simulation));

        Assert.Equal(2, simulation.Recorded.Count);
        Assert.Equal(2, scheduler.Tick);
        Assert.Equal(0.3, scheduler.AccumulatorSeconds, 10);
    }

    [Fact]
    public void Advance_LatchesATapAcrossFramesThatDrainNoStep()
    {
        RecordingSimulation simulation = new(Jump);
        FixedStepScheduler scheduler = CreateScheduler();

        scheduler.Advance(0.02, DeviceSnapshot.Of(Key.Space), simulation);
        scheduler.Advance(0.02, DeviceSnapshot.Empty, simulation);
        scheduler.Advance(0.06, DeviceSnapshot.Empty, simulation);
        scheduler.Advance(0.1, DeviceSnapshot.Empty, simulation);

        Assert.Collection(
            simulation.Recorded,
            first =>
            {
                Assert.True(first.First.Pressed);
                Assert.True(first.First.Held);
            },
            second =>
            {
                Assert.True(second.First.Released);
                Assert.False(second.First.Held);
            });
    }

    [Fact]
    public void Advance_ReusesOneSampleAcrossSeveralStepsWithoutRepeatingAnEdge()
    {
        RecordingSimulation simulation = new(Jump);
        FixedStepScheduler scheduler = CreateScheduler();

        scheduler.Advance(0.3, DeviceSnapshot.Of(Key.Space), simulation);

        Assert.Collection(
            simulation.Recorded,
            first => Assert.True(first.First.Pressed),
            second => Assert.False(second.First.Pressed),
            third => Assert.False(third.First.Pressed));
        Assert.All(simulation.Recorded, step => Assert.True(step.First.Held));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.01)]
    public void Advance_RejectsInvalidElapsedTime(double elapsedSeconds)
    {
        FixedStepScheduler scheduler = CreateScheduler();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scheduler.Advance(elapsedSeconds, DeviceSnapshot.Empty, new RecordingSimulation(Jump)));
    }

    // A frame worth exactly one step at 1x buys `scale` steps at `scale`: halves run a step every
    // second frame, multiples run several in one.
    [Theory]
    [InlineData(0.25, new[] { 0, 0, 0, 1, 0, 0, 0, 1 })]
    [InlineData(0.5, new[] { 0, 1, 0, 1, 0, 1, 0, 1 })]
    [InlineData(1.0, new[] { 1, 1, 1, 1, 1, 1, 1, 1 })]
    [InlineData(2.0, new[] { 2, 2, 2, 2, 2, 2, 2, 2 })]
    [InlineData(4.0, new[] { 4, 4, 4, 4, 4, 4, 4, 4 })]
    public void TimeScale_BuysTheFramesElapsedTimeThatManyStepsWithoutMovingTheStepLength(double scale, int[] perFrame)
    {
        RecordingSimulation simulation = new(Jump);
        FixedStepScheduler scheduler = CreateScheduler(maxStepsPerFrame: 8);
        scheduler.TimeScale = scale;

        long ticks = 0;
        foreach (int expected in perFrame)
        {
            simulation.Recorded.Clear();
            scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, simulation);
            ticks += expected;

            Assert.Equal(expected, simulation.Recorded.Count);
            Assert.Equal(expected, scheduler.StepsThisFrame);
            Assert.Equal(ticks, scheduler.Tick);
            Assert.InRange(scheduler.InterpolationAlpha, 0f, 0.9999f);
            Assert.All(simulation.Recorded, static step => Assert.Equal((float)StepSeconds, step.DeltaSeconds));
        }
    }

    // The pace is the host's, never the simulation's: the same run in steps is handed the same
    // ticks and the same time whichever pace the frames arrived at.
    [Fact]
    public void TimeScale_LeavesEveryStepTheSimulationIsHandedIdentical()
    {
        RecordingSimulation fast = new(Jump);
        FixedStepScheduler atOne = CreateScheduler();
        for (int frame = 0; frame < 8; frame++)
        {
            atOne.Advance(StepSeconds, DeviceSnapshot.Empty, fast);
        }

        RecordingSimulation slow = new(Jump);
        FixedStepScheduler atQuarter = CreateScheduler();
        atQuarter.TimeScale = 0.25;
        for (int frame = 0; frame < 32; frame++)
        {
            atQuarter.Advance(StepSeconds, DeviceSnapshot.Empty, slow);
        }

        Assert.Equal(8, fast.Recorded.Count);
        Assert.Equal(fast.Recorded, slow.Recorded);
        Assert.Equal(atOne.Tick, atQuarter.Tick);
    }

    // A single step is one step at any pace, and it never touches the accumulator the pace feeds.
    [Fact]
    public void TimeScale_DoesNotChangeAStepTakenByHandWhileHeld()
    {
        RecordingSimulation simulation = new(Jump);
        FixedStepScheduler scheduler = CreateScheduler();
        scheduler.TimeScale = 4;
        scheduler.Held = true;

        Assert.False(scheduler.StepOnce(DeviceSnapshot.Empty, simulation));

        Assert.Single(simulation.Recorded);
        Assert.Equal(1, scheduler.StepsThisFrame);
        Assert.Equal(0, scheduler.AccumulatorSeconds);
    }

    // The pace asks for more steps than the frame bound allows, so the frame runs its bound and
    // drops the rest: past the bound a scale buys no more steps.
    [Fact]
    public void TimeScale_StillGivesWayToTheFrameStepBound()
    {
        RecordingSimulation simulation = new(Jump);
        FixedStepScheduler scheduler = CreateScheduler(maxStepsPerFrame: 3);
        scheduler.TimeScale = 4;

        scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, simulation);

        Assert.Equal(3, simulation.Recorded.Count);
        Assert.Equal(0, scheduler.AccumulatorSeconds);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    public void TimeScale_RejectsANonPositiveOrNonFinitePace(double scale)
    {
        FixedStepScheduler scheduler = CreateScheduler();

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.TimeScale = scale);
        Assert.Equal(1, scheduler.TimeScale);
    }

    // The run owns the pace and the scheduler holds what is applied: the host copies the one into
    // the other ahead of each advance, which is the frame HostFrame runs.
    [Fact]
    public void ThePaceTheRunHolds_ChangesHowManyStepsTheFramesAfterItRun()
    {
        PaceScene scene = new();
        using SceneHost host = new(
            SceneTransition.ToScene(typeof(PaceScene), null),
            (in SceneTransition _) => scene,
            new Run());
        FixedStepScheduler scheduler = CreateScheduler();

        // A frame worth one step buys one, until the scene's first step halves the pace.
        HostFrame(scheduler, host);

        Assert.Equal(1, scheduler.Tick);
        Assert.Equal(0.5, host.Run.TimeScale);

        // At a half it buys one every second frame; the scene's second step then doubles the pace.
        HostFrame(scheduler, host);
        Assert.Equal(1, scheduler.Tick);
        HostFrame(scheduler, host);

        Assert.Equal(2, scheduler.Tick);
        Assert.Equal(2, host.Run.TimeScale);

        HostFrame(scheduler, host);

        Assert.Equal(4, scheduler.Tick);
    }

    [Fact]
    public void StepContext_DerivesTotalSecondsFromTheDoublePrecisionStep()
    {
        const double Sixty = 1.0 / 60.0;
        const long AnHourOfTicks = 216_000;

        StepContext context = new(Sixty, new InputState(new ActionBindings()), AnHourOfTicks);

        Assert.Equal(AnHourOfTicks * Sixty, context.TotalSeconds);
        Assert.NotEqual(AnHourOfTicks * (double)(float)Sixty, context.TotalSeconds);
    }

    private static FixedStepScheduler CreateScheduler(int maxStepsPerFrame = 5) =>
        new(StepSeconds, maxStepsPerFrame, new ActionBindings().Bind(Jump, Key.Space));

    // One host frame: the run's pace applied, then the frame's elapsed time spent.
    private static void HostFrame(FixedStepScheduler scheduler, SceneHost host)
    {
        scheduler.TimeScale = host.Run.TimeScale;
        scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, host);
    }

    private static void AssertStep(in RecordedStep step, long tick)
    {
        Assert.Equal(tick, step.Tick);
        Assert.Equal((float)StepSeconds, step.DeltaSeconds);
        Assert.Equal(tick * StepSeconds, step.TotalSeconds);
    }

    // Halves the run's pace on its first step and doubles it on its second.
    private sealed class PaceScene : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            if (context.Tick == 0)
            {
                Run.TimeScale = 0.5;
            }
            else if (context.Tick == 1)
            {
                Run.TimeScale = 2;
            }
        }
    }
}
