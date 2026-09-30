using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Allocation;

// A step's allocations, then those of the frame built after it, apart. The view builds only when read,
// and each measured step reads it as a presenting host does.
internal readonly record struct StepSample(long StepBytes, long ViewBytes, RenderMetrics Render);

internal static class StepMeasurement
{
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

            input.Advance(DeviceSnapshot.Empty);
            simulation.Step(new StepContext(stepSeconds, input, step));
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
}
