using Capsule.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class CrowdAllocationTests
{
    private const int WarmupSteps = 180;
    private const int MeasuredSteps = 600;
    private const double StepSeconds = 1.0 / 60.0;

    [Fact]
    public void AThousandCollidingAnimatedBodies_AllocateNothing()
    {
        Scene scene = CrowdWorkload.Room();
        using SceneSimulation simulation = new(scene, run: StageWorkload.Defaults);

        StepSample[] samples = StepMeasurement.Measure(simulation, StepSeconds, WarmupSteps, MeasuredSteps);

        long allocated = 0;
        foreach (StepSample sample in samples)
        {
            allocated += sample.AllocatedBytes;
        }

        Assert.Equal(0, allocated);
    }
}
