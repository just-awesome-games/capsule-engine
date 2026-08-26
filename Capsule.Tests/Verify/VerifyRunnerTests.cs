using Capsule.Input;
using Capsule.Rendering;
using Capsule.Verify;

namespace Capsule.Tests.Verify;

public sealed class VerifyRunnerTests
{
    private const double StepSeconds = 1.0 / 60.0;
    private static readonly InputAction Jump = new("Jump");

    [Fact]
    public void Run_ReplaysOneSnapshotPerTickAndRecordsFrameMetrics()
    {
        DeviceSnapshot[] script =
        [
            DeviceSnapshot.Empty,
            DeviceSnapshot.Of(Key.Space),
            DeviceSnapshot.Of(Key.Space),
            DeviceSnapshot.Empty,
        ];
        RecordedStep[] steps = new RecordedStep[script.Length];
        RecordingSimulation simulation = new(steps);
        VerifyFrameMetrics[] metrics = new VerifyFrameMetrics[script.Length];

        VerifyRunResult result = VerifyRunner.Run(
            simulation,
            new ActionBindings().Bind(Jump, Key.Space),
            script,
            new VerifyRunOptions(StepSeconds, MaxAllocatedBytesPerStep: long.MaxValue, MaxAllocatedBytesPerRun: long.MaxValue),
            metrics);

        Assert.Equal(script.Length, result.CompletedSteps);
        Assert.Equal(script.Length, result.MeasuredSteps);
        Assert.Equal(script.Length, result.NextTick);
        Assert.False(result.ExitRequested);
        Assert.True(result.AllocationBudgetSatisfied);
        Assert.Collection(
            steps,
            first => Assert.Equal(new RecordedStep(0, false, false, false), first),
            second => Assert.Equal(new RecordedStep(1, true, false, true), second),
            third => Assert.Equal(new RecordedStep(2, false, false, true), third),
            fourth => Assert.Equal(new RecordedStep(3, false, true, false), fourth));
        Assert.Equal(new long[] { 0, 1, 2, 3 }, metrics.Select(metric => metric.Tick));
        Assert.All(metrics, metric => Assert.Equal(new RenderMetrics(1, 1), metric.Render));
        Assert.All(metrics, metric => Assert.True(metric.Duration >= TimeSpan.Zero));
    }

    [Fact]
    public void Run_ExcludesWarmupAllocationsFromTheExactBudget()
    {
        WarmupAllocatingSimulation simulation = new(warmupSteps: 2);

        VerifyRunResult result = VerifyRunner.Run(
            simulation,
            new ActionBindings(),
            new DeviceSnapshot[5],
            new VerifyRunOptions(StepSeconds, WarmupSteps: 2));

        Assert.Equal(5, result.CompletedSteps);
        Assert.Equal(3, result.MeasuredSteps);
        Assert.Equal(0, result.AllocatedBytes);
        Assert.Equal(0, result.PeakFrameAllocatedBytes);
        Assert.True(result.AllocationBudgetSatisfied);
    }

    [Fact]
    public void Run_ReportsBothPerFrameAndWholeRunBudgetFailures()
    {
        VerifyRunResult result = VerifyRunner.Run(
            new AllocatingSimulation(),
            new ActionBindings(),
            new DeviceSnapshot[3],
            new VerifyRunOptions(StepSeconds));

        Assert.True(result.PeakFrameAllocatedBytes > 0);
        Assert.True(result.AllocatedBytes >= result.PeakFrameAllocatedBytes);
        Assert.False(result.AllocationBudgetSatisfied);
    }

    [Fact]
    public void Run_AcceptsBudgetsAtTheMeasuredExactValues()
    {
        VerifyRunResult baseline = VerifyRunner.Run(
            new AllocatingSimulation(),
            new ActionBindings(),
            new DeviceSnapshot[3],
            new VerifyRunOptions(StepSeconds, MaxAllocatedBytesPerStep: long.MaxValue, MaxAllocatedBytesPerRun: long.MaxValue));

        VerifyRunResult gated = VerifyRunner.Run(
            new AllocatingSimulation(),
            new ActionBindings(),
            new DeviceSnapshot[3],
            new VerifyRunOptions(
                StepSeconds,
                MaxAllocatedBytesPerStep: baseline.PeakFrameAllocatedBytes,
                MaxAllocatedBytesPerRun: baseline.AllocatedBytes));

        Assert.Equal(baseline.PeakFrameAllocatedBytes, gated.PeakFrameAllocatedBytes);
        Assert.Equal(baseline.AllocatedBytes, gated.AllocatedBytes);
        Assert.True(gated.AllocationBudgetSatisfied);
    }

    [Fact]
    public void Run_StopsAtTheTickThatRequestsExit()
    {
        VerifyRunResult result = VerifyRunner.Run(
            new ExitSimulation(exitOnTick: 2),
            new ActionBindings(),
            new DeviceSnapshot[10],
            new VerifyRunOptions(StepSeconds, MaxAllocatedBytesPerStep: long.MaxValue, MaxAllocatedBytesPerRun: long.MaxValue));

        Assert.True(result.ExitRequested);
        Assert.Equal(3, result.CompletedSteps);
        Assert.Equal(3, result.MeasuredSteps);
        Assert.Equal(3, result.NextTick);
    }

    [Fact]
    public void Run_StopsDuringWarmupWithoutStartingMeasurement()
    {
        VerifyRunResult result = VerifyRunner.Run(
            new ExitSimulation(exitOnTick: 1),
            new ActionBindings(),
            new DeviceSnapshot[10],
            new VerifyRunOptions(StepSeconds, WarmupSteps: 4));

        Assert.True(result.ExitRequested);
        Assert.Equal(2, result.CompletedSteps);
        Assert.Equal(0, result.MeasuredSteps);
        Assert.Equal(0, result.AllocatedBytes);
    }

    [Fact]
    public void CaptureArtifacts_InvokesStateThenScreenshotAfterTheRun()
    {
        ExitSimulation simulation = new(exitOnTick: 0);
        VerifyRunResult result = VerifyRunner.Run(
            simulation,
            new ActionBindings(),
            new DeviceSnapshot[1],
            new VerifyRunOptions(StepSeconds, MaxAllocatedBytesPerStep: long.MaxValue, MaxAllocatedBytesPerRun: long.MaxValue));
        RecordingArtifactSink sink = new();

        VerifyRunner.CaptureArtifacts(simulation, result, sink);

        Assert.Equal(["state", "screenshot"], sink.Calls);
        Assert.Same(simulation, sink.Simulation);
        Assert.Same(simulation.View, sink.View);
        Assert.Equal(result, sink.Result);
    }

    [Fact]
    public void Run_RejectsAMetricsBufferTooSmallForTheMeasuredScript()
    {
        VerifyFrameMetrics[] metrics = new VerifyFrameMetrics[1];

        ArgumentException exception = Assert.Throws<ArgumentException>(() => VerifyRunner.Run(
            new ExitSimulation(exitOnTick: null),
            new ActionBindings(),
            new DeviceSnapshot[4],
            new VerifyRunOptions(StepSeconds, WarmupSteps: 1),
            metrics));

        Assert.Contains("3 measured frames", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(StepSeconds, -1)]
    [InlineData(StepSeconds, 2)]
    public void Run_RejectsInvalidTimingOptions(double stepSeconds, int warmupSteps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VerifyRunner.Run(
            new ExitSimulation(exitOnTick: null),
            new ActionBindings(),
            new DeviceSnapshot[1],
            new VerifyRunOptions(stepSeconds, warmupSteps)));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Run_RejectsNegativeAllocationBudgets(long perFrame, long perRun)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VerifyRunner.Run(
            new ExitSimulation(exitOnTick: null),
            new ActionBindings(),
            new DeviceSnapshot[1],
            new VerifyRunOptions(
                StepSeconds,
                MaxAllocatedBytesPerStep: perFrame,
                MaxAllocatedBytesPerRun: perRun)));
    }

    private readonly record struct RecordedStep(long Tick, bool Pressed, bool Released, bool Held);

    private sealed class RecordingSimulation : ISimulation
    {
        private readonly RecordedStep[] _steps;
        private int _count;

        public RecordingSimulation(RecordedStep[] steps)
        {
            _steps = steps;
            View.AddQuad(default);
        }

        public bool ExitRequested => false;

        public FrameView View { get; } = new();

        public void Step(in StepContext context)
        {
            _steps[_count++] = new RecordedStep(
                context.Tick,
                context.Input.WasPressed(Jump),
                context.Input.WasReleased(Jump),
                context.Input.IsHeld(Jump));
        }
    }

    private sealed class WarmupAllocatingSimulation(int warmupSteps) : ISimulation
    {
        private object? _heldAllocation;

        public bool ExitRequested => false;

        public FrameView View { get; } = new();

        public void Step(in StepContext context)
        {
            if (context.Tick < warmupSteps)
            {
                _heldAllocation = new byte[32];
                GC.KeepAlive(_heldAllocation);
            }
        }
    }

    private sealed class AllocatingSimulation : ISimulation
    {
        private object? _heldAllocation;

        public bool ExitRequested => false;

        public FrameView View { get; } = new();

        public void Step(in StepContext context)
        {
            _heldAllocation = new byte[1];
            GC.KeepAlive(_heldAllocation);
        }
    }

    private sealed class ExitSimulation(long? exitOnTick) : ISimulation
    {
        public bool ExitRequested { get; private set; }

        public FrameView View { get; } = new();

        public void Step(in StepContext context)
        {
            ExitRequested = context.Tick == exitOnTick;
        }
    }

    private sealed class RecordingArtifactSink : IVerifyArtifactSink
    {
        public List<string> Calls { get; } = [];

        public ISimulation? Simulation { get; private set; }

        public FrameView? View { get; private set; }

        public VerifyRunResult Result { get; private set; }

        public void WriteStateDump(ISimulation simulation, in VerifyRunResult result)
        {
            Calls.Add("state");
            Simulation = simulation;
            Result = result;
        }

        public void CaptureScreenshot(FrameView view, in VerifyRunResult result)
        {
            Calls.Add("screenshot");
            View = view;
            Result = result;
        }
    }
}
