using Capsule.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class CrowdAllocationTests
{
    [Fact]
    public void AThousandCollidingAnimatedBodies_AllocateNothing()
    {
        Scene scene = CrowdWorkload.Room();
        using SceneSimulation simulation = new(scene, run: StageWorkload.Defaults);

        StepSample[] samples = StepMeasurement.Measure(simulation, warmupSteps: 180);

        StepMeasurement.AssertNoAllocation(samples);
    }
}
