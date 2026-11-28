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

    /// <summary>Collects the heap, then reads the thread's allocation counter, opening a measured window.</summary>
    /// <remarks>
    /// Every window in the suite opens here. While other tests run in parallel, the counter can rise by up to about 8 KB
    /// with nothing allocated. A full collection first keeps that rise out of the window.
    /// </remarks>
    internal static long Start()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return GC.GetAllocatedBytesForCurrentThread();
    }

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

        long before = Start();
        for (int index = warmup; index < warmup + measured; index++)
        {
            step(index);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// The bytes of <paramref name="measured"/> steps of <paramref name="host"/> after <paramref name="warmup"/>,
    /// each with the frame built after it as a presenting host reads it.
    /// </summary>
    internal static long Bytes(SimulationHost host, int warmup = WarmupSteps, int measured = MeasuredSteps) =>
        Bytes(
            step =>
            {
                host.Step();
                _ = host.Simulation.View;
            },
            warmup,
            measured);

    internal static StepSample[] Measure(
        SceneSimulation simulation,
        int warmupSteps = WarmupSteps,
        int measuredSteps = MeasuredSteps)
    {
        InputState input = new(new ActionBindings());
        StepSample[] samples = new StepSample[measuredSteps];

        for (int step = 0; step < warmupSteps + measuredSteps; step++)
        {
            long startBytes = step == warmupSteps ? Start() : GC.GetAllocatedBytesForCurrentThread();

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

    /// <summary>Asserts no measured step and no frame built after one allocated, naming the first that did.</summary>
    internal static void AssertNoAllocation(StepSample[] samples)
    {
        int first = Array.FindIndex(samples, static sample => sample.StepBytes != 0 || sample.ViewBytes != 0);
        Assert.True(
            first < 0,
            first < 0 ? null : FormattableString.Invariant($"measured step {first} allocated {samples[first].StepBytes} bytes stepping and {samples[first].ViewBytes} building its frame."));
    }

    /// <summary>Writes <paramref name="bytes"/> to the test output and asserts they fit <paramref name="budget"/>.</summary>
    internal static void Report(ITestOutputHelper output, string label, long bytes, int steps = MeasuredSteps, long budget = 0)
    {
        output.WriteLine(FormattableString.Invariant($"{label}: {bytes} bytes over {steps} steps, budget {budget}"));
        Assert.True(
            bytes <= budget,
            FormattableString.Invariant($"{label} allocated {bytes} bytes over {steps} steps, over its budget of {budget}."));
    }

    /// <summary>Writes the share of <paramref name="bytes"/> each of <paramref name="count"/> units took and asserts it fits <paramref name="budgetPerUnit"/>.</summary>
    internal static void ReportPer(ITestOutputHelper output, string unit, long bytes, int count, long budgetPerUnit)
    {
        output.WriteLine(FormattableString.Invariant($"{bytes / count} bytes per {unit}, budget {budgetPerUnit}"));
        Assert.True(
            bytes <= budgetPerUnit * count,
            FormattableString.Invariant($"a {unit} took {bytes / count} bytes, over its budget of {budgetPerUnit}."));
    }
}
