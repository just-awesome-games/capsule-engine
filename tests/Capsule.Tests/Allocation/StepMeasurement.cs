using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Allocation;

internal readonly record struct StepSample(long AllocatedBytes, RenderMetrics Render);

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
            RenderMetrics render = simulation.View.Metrics;

            long bytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;

            if (step >= warmupSteps)
            {
                samples[step - warmupSteps] = new StepSample(bytes, render);
            }
        }

        return samples;
    }
}
