using Capsule.Tests.DevTools;
using static Capsule.Tests.DevTools.RecordingFixtures;

namespace Capsule.Tests.Allocation;

// Recording a step allocates nothing once the run is warm, checkpoints included. Only a full chunk grows
// the buffer, and hours of play fit the first.
[Collection(StageAllocationCollection.Name)]
public sealed class RecordingAllocationTests
{
    [Fact]
    public void RecordingAStepWithChangingInput_AllocatesNothing()
    {
        using RecordedRun run = new(Builder());

        Assert.Equal(0, StepMeasurement.Bytes(step => run.Step(Varied(run, step % 50))));
        Assert.Equal(StepMeasurement.WarmupSteps + StepMeasurement.MeasuredSteps, run.Recorder.Ticks);
    }
}
