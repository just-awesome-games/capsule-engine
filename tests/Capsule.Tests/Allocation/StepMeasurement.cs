using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Allocation;

// A step's allocations, then those of the frame built after it, apart. The view builds only when read,
// and each measured step reads it as a presenting host does.
internal readonly record struct StepSample(long StepBytes, long ViewBytes, RenderMetrics Render);

internal static class StepMeasurement
{
    /// <summary>The steps run before measuring begins, which take every first-use cost.</summary>
    internal const int WarmupSteps = 120;

    /// <summary>The steps measured after the warm-up.</summary>
    internal const int MeasuredSteps = 600;

    /// <summary>The length of one step at the engine's default rate, in seconds.</summary>
    internal const double StepSeconds = 1.0 / StepContext.DefaultStepHertz;

    /// <summary>
    /// Runs <paramref name="step"/> <paramref name="warmup"/> times, then <paramref name="measured"/> times more,
    /// and returns the bytes the measured calls allocated.
    /// </summary>
    /// <remarks>The step index runs on from the warm-up into the measured stretch.</remarks>
    internal static long Bytes(Action<int> step, int warmup = WarmupSteps, int measured = MeasuredSteps)
    {
        for (int index = 0; index < warmup; index++)
        {
            step(index);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = warmup; index < warmup + measured; index++)
        {
            step(index);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    internal static StepSample[] Measure(
        SceneSimulation simulation,
        int warmupSteps = WarmupSteps,
        int measuredSteps = MeasuredSteps)
    {
        InputState input = new(new ActionBindings());
        StepSample[] samples = new StepSample[measuredSteps];

        for (int step = 0; step < warmupSteps + measuredSteps; step++)
        {
            long startBytes = GC.GetAllocatedBytesForCurrentThread();

            input.Advance(DeviceSnapshot.Empty);
            simulation.Step(new StepContext(StepSeconds, input, step));
            long steppedBytes = GC.GetAllocatedBytesForCurrentThread();

            RenderMetrics render = simulation.View.Metrics;
            long builtBytes = GC.GetAllocatedBytesForCurrentThread();

            if (step >= warmupSteps)
            {
                samples[step - warmupSteps] = new StepSample(steppedBytes - startBytes, builtBytes - steppedBytes, render);
            }
        }

        return samples;
    }

    /// <summary>Writes <paramref name="bytes"/> to the test output and asserts they fit <paramref name="budget"/>.</summary>
    internal static void Report(ITestOutputHelper output, string label, long bytes, int steps = MeasuredSteps, long budget = 0)
    {
        output.WriteLine(FormattableString.Invariant($"{label}: {bytes} bytes over {steps} steps, budget {budget}"));
        Assert.True(
            bytes <= budget,
            FormattableString.Invariant($"{label} allocated {bytes} bytes over {steps} steps, over its budget of {budget}."));
    }
}
