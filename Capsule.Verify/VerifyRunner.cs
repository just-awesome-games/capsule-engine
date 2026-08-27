using System.Diagnostics;
using Capsule.Input;

namespace Capsule.Verify;

/// <summary>Drives an <see cref="ISimulation"/> from a deterministic per-tick input script.</summary>
public static class VerifyRunner
{
    /// <summary>
    /// Runs one step per snapshot until the script is spent or the simulation requests exit.
    /// </summary>
    /// <param name="simulation">The simulation to drive; it is stepped, never rendered.</param>
    /// <param name="bindings">The game's action bindings, as its shell registers them.</param>
    /// <param name="snapshots">One device snapshot per step, warm-up steps included.</param>
    /// <param name="options">The fixed step, the warm-up length, and any allocation budgets.</param>
    /// <param name="metrics">
    /// Filled with one entry per measured step where it has room for them all; empty collects none.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The step, the warm-up or a budget is out of range.</exception>
    /// <exception cref="ArgumentException">The metrics span is too short for the measured steps.</exception>
    public static VerifyRunResult Run(
        ISimulation simulation,
        ActionBindings bindings,
        ReadOnlySpan<DeviceSnapshot> snapshots,
        in VerifyRunOptions options,
        Span<VerifyFrameMetrics> metrics = default)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(bindings);
        Validate(options, snapshots.Length, metrics.Length);

        InputState input = new(bindings);
        long tick = 0;
        int completedSteps = 0;

        for (; completedSteps < options.WarmupSteps; completedSteps++)
        {
            input.Advance(snapshots[completedSteps]);
            simulation.Step(new StepContext(options.StepSeconds, input, tick));
            tick++;

            if (simulation.ExitRequested)
            {
                completedSteps++;
                return CreateResult(options, snapshots.Length, completedSteps, 0, tick, true, 0, 0, 0, 0);
            }
        }

        long runAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        long totalDurationTicks = 0;
        long peakDurationTicks = 0;
        long peakFrameAllocatedBytes = 0;
        int measuredSteps = 0;

        for (; completedSteps < snapshots.Length; completedSteps++)
        {
            long frameAllocationStart = GC.GetAllocatedBytesForCurrentThread();
            long frameTimeStart = Stopwatch.GetTimestamp();

            input.Advance(snapshots[completedSteps]);
            simulation.Step(new StepContext(options.StepSeconds, input, tick));

            long frameDurationTicks = Stopwatch.GetTimestamp() - frameTimeStart;
            long frameAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - frameAllocationStart;

            totalDurationTicks += frameDurationTicks;
            peakDurationTicks = Math.Max(peakDurationTicks, frameDurationTicks);
            peakFrameAllocatedBytes = Math.Max(peakFrameAllocatedBytes, frameAllocatedBytes);

            if (!metrics.IsEmpty)
            {
                metrics[measuredSteps] = new VerifyFrameMetrics(
                    tick,
                    FromStopwatchTicks(frameDurationTicks),
                    frameAllocatedBytes,
                    simulation.View.Metrics);
            }

            measuredSteps++;
            tick++;

            if (simulation.ExitRequested)
            {
                completedSteps++;
                break;
            }
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - runAllocationStart;

        return CreateResult(
            options,
            snapshots.Length,
            completedSteps,
            measuredSteps,
            tick,
            simulation.ExitRequested,
            allocatedBytes,
            peakFrameAllocatedBytes,
            totalDurationTicks,
            peakDurationTicks);
    }

    /// <summary>Writes the final state and screenshot after measured work has ended.</summary>
    public static void CaptureArtifacts(
        ISimulation simulation,
        in VerifyRunResult result,
        IVerifyArtifactSink sink)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(sink);

        sink.WriteStateDump(simulation, result);
        sink.CaptureScreenshot(simulation.View, result);
    }

    private static VerifyRunResult CreateResult(
        in VerifyRunOptions options,
        int requestedSteps,
        int completedSteps,
        int measuredSteps,
        long nextTick,
        bool exitRequested,
        long allocatedBytes,
        long peakFrameAllocatedBytes,
        long totalDurationTicks,
        long peakDurationTicks) =>
        new(
            requestedSteps,
            completedSteps,
            measuredSteps,
            nextTick,
            exitRequested,
            allocatedBytes,
            peakFrameAllocatedBytes,
            FromStopwatchTicks(totalDurationTicks),
            FromStopwatchTicks(peakDurationTicks),
            options.MaxAllocatedBytesPerStep,
            options.MaxAllocatedBytesPerRun);

    private static TimeSpan FromStopwatchTicks(long ticks) =>
        TimeSpan.FromSeconds(ticks / (double)Stopwatch.Frequency);

    private static void Validate(in VerifyRunOptions options, int snapshotCount, int metricsLength)
    {
        if (!double.IsFinite(options.StepSeconds) || options.StepSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.StepSeconds,
                "The fixed step must be finite and greater than zero.");
        }

        if (options.WarmupSteps < 0 || options.WarmupSteps > snapshotCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.WarmupSteps,
                "Warm-up steps must fit inside the input script.");
        }

        if (options.MaxAllocatedBytesPerStep is < 0 || options.MaxAllocatedBytesPerRun is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Allocation budgets cannot be negative.");
        }

        int measuredCapacity = snapshotCount - options.WarmupSteps;
        if (metricsLength != 0 && metricsLength < measuredCapacity)
        {
            throw new ArgumentException(
                $"Metrics needs capacity for {measuredCapacity} measured frames.",
                "metrics");
        }
    }
}
