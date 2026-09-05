using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

public sealed class FixedStepSchedulerTests
{
    private const double StepSeconds = 0.1;
    private static readonly InputAction Jump = new("Jump");

    [Fact]
    public void Advance_AccumulatesPartialFramesAndReportsInterpolation()
    {
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = CreateScheduler();

        Assert.False(scheduler.Advance(0.04, DeviceSnapshot.Empty, simulation));
        Assert.False(scheduler.Advance(0.05, DeviceSnapshot.Empty, simulation));
        Assert.Empty(simulation.Steps);
        Assert.Equal(0.9f, scheduler.InterpolationAlpha, 5);

        Assert.False(scheduler.Advance(0.02, DeviceSnapshot.Empty, simulation));
        Assert.Single(simulation.Steps);
        Assert.Equal(0.01, scheduler.AccumulatorSeconds, 10);
        Assert.Equal(0.1f, scheduler.InterpolationAlpha, 5);
    }

    [Fact]
    public void Advance_BoundsAStallToTheStepBoundAndDropsWhatItDidNotRun()
    {
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = CreateScheduler(maxStepsPerFrame: 3);

        scheduler.Advance(30, DeviceSnapshot.Empty, simulation);

        Assert.Equal(3, simulation.Steps.Count);
        Assert.Equal(0, scheduler.AccumulatorSeconds);
    }

    // The spiral of death: a step costing more than the step length would otherwise queue two steps
    // next frame, then three, until every frame drains the whole backlog.
    [Fact]
    public void Advance_HoldsTheStepBoundWhenEveryFrameArrivesLateAndNeverCarriesABacklog()
    {
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = CreateScheduler(maxStepsPerFrame: 3);
        const double FrameSecondsWorthOfSteps = StepSeconds * 4.5;

        for (int frame = 0; frame < 10; frame++)
        {
            simulation.Steps.Clear();
            scheduler.Advance(FrameSecondsWorthOfSteps, DeviceSnapshot.Empty, simulation);

            Assert.Equal(3, simulation.Steps.Count);
            Assert.Equal(0, scheduler.AccumulatorSeconds);
        }

        Assert.Equal(30, scheduler.Tick);
    }

    [Fact]
    public void Advance_SuppliesContiguousTicksAndDerivedTime()
    {
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = CreateScheduler();

        scheduler.Advance(0.3, DeviceSnapshot.Empty, simulation);
        scheduler.Advance(0.1, DeviceSnapshot.Empty, simulation);

        Assert.Collection(
            simulation.Steps,
            step => AssertStep(step, 0),
            step => AssertStep(step, 1),
            step => AssertStep(step, 2),
            step => AssertStep(step, 3));
        Assert.Equal(4, scheduler.Tick);
    }

    [Fact]
    public void Advance_StopsQueuedStepsImmediatelyWhenSimulationRequestsExit()
    {
        RecordingSimulation simulation = new(exitOnTick: 1);
        FixedStepScheduler scheduler = CreateScheduler();

        Assert.True(scheduler.Advance(0.5, DeviceSnapshot.Empty, simulation));

        Assert.Equal(2, simulation.Steps.Count);
        Assert.Equal(2, scheduler.Tick);
        Assert.Equal(0.3, scheduler.AccumulatorSeconds, 10);
    }

    [Fact]
    public void Advance_LatchesATapAcrossFramesThatDrainNoStep()
    {
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = CreateScheduler();

        scheduler.Advance(0.02, DeviceSnapshot.Of(Key.Space), simulation);
        scheduler.Advance(0.02, DeviceSnapshot.Empty, simulation);
        scheduler.Advance(0.06, DeviceSnapshot.Empty, simulation);
        scheduler.Advance(0.1, DeviceSnapshot.Empty, simulation);

        Assert.Collection(
            simulation.Steps,
            first =>
            {
                Assert.True(first.JumpPressed);
                Assert.True(first.JumpHeld);
            },
            second =>
            {
                Assert.True(second.JumpReleased);
                Assert.False(second.JumpHeld);
            });
    }

    [Fact]
    public void Advance_ReusesOneSampleAcrossSeveralStepsWithoutRepeatingAnEdge()
    {
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = CreateScheduler();

        scheduler.Advance(0.3, DeviceSnapshot.Of(Key.Space), simulation);

        Assert.Collection(
            simulation.Steps,
            first => Assert.True(first.JumpPressed),
            second => Assert.False(second.JumpPressed),
            third => Assert.False(third.JumpPressed));
        Assert.All(simulation.Steps, step => Assert.True(step.JumpHeld));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.01)]
    public void Advance_RejectsInvalidElapsedTime(double elapsedSeconds)
    {
        FixedStepScheduler scheduler = CreateScheduler();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scheduler.Advance(elapsedSeconds, DeviceSnapshot.Empty, new RecordingSimulation()));
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

    private static void AssertStep(in RecordedStep step, long tick)
    {
        Assert.Equal(tick, step.Tick);
        Assert.Equal((float)StepSeconds, step.DeltaSeconds);
        Assert.Equal(tick * StepSeconds, step.TotalSeconds);
    }

    private readonly record struct RecordedStep(
        long Tick,
        float DeltaSeconds,
        double TotalSeconds,
        bool JumpPressed,
        bool JumpReleased,
        bool JumpHeld);

    private sealed class RecordingSimulation(long? exitOnTick = null) : ISimulation
    {
        public List<RecordedStep> Steps { get; } = [];

        public bool ExitRequested { get; private set; }

        public FrameView View { get; } = new();

        public void Step(in StepContext context)
        {
            Steps.Add(new RecordedStep(
                context.Tick,
                context.DeltaSeconds,
                context.TotalSeconds,
                context.Input.WasPressed(Jump),
                context.Input.WasReleased(Jump),
                context.Input.IsHeld(Jump)));
            ExitRequested = context.Tick == exitOnTick;
        }
    }
}
