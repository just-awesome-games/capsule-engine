using System.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Performance;

/// <summary>What one measured fixed step cost, and what it drew.</summary>
internal readonly record struct StepSample(long AllocatedBytes, TimeSpan Duration, RenderMetrics Render);

/// <summary>Steps a simulation behind a warm-up prefix and samples what the steps after it cost.</summary>
internal static class StepMeasurement
{
    /// <summary>
    /// A sample spans what a host pays for once a frame — advancing input, stepping, then reading
    /// the view — so warm-up walks that same sequence and first-touch work lands there instead of
    /// in a measurement.
    /// </summary>
    internal static StepSample[] Measure(
        SceneSimulation simulation,
        double stepSeconds,
        int warmupSteps,
        int measuredSteps)
    {
        InputState input = new(new ActionBindings());
        StepSample[] samples = new StepSample[measuredSteps];

        for (int step = 0; step < warmupSteps + measuredSteps; step++)
        {
            long startBytes = GC.GetAllocatedBytesForCurrentThread();
            long startTimestamp = Stopwatch.GetTimestamp();

            input.Advance(DeviceSnapshot.Empty);
            simulation.Step(new StepContext(stepSeconds, input, step));
            RenderMetrics render = simulation.View.Metrics;

            TimeSpan duration = Stopwatch.GetElapsedTime(startTimestamp);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;

            if (step >= warmupSteps)
            {
                samples[step - warmupSteps] = new StepSample(bytes, duration, render);
            }
        }

        return samples;
    }
}
