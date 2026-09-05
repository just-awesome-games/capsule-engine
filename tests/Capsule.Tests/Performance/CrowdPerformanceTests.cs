using System.Globalization;
using Capsule.Scenes;
using Xunit.Abstractions;

namespace Capsule.Tests.Performance;

[Collection(StagePerformanceCollection.Name)]
public sealed class CrowdPerformanceTests(ITestOutputHelper output)
{
    private const int WarmupSteps = 180;
    private const int MeasuredSteps = 600;
    private const double StepSeconds = 1.0 / 60.0;

    // Twice the optimised step this workload measures on a desktop, which is a tripwire and not a
    // target: what it catches is a collision or scene-walk change that costs a multiple of the
    // step, and what it deliberately does not catch is drift. The whole point of the number is
    // that a thousand colliding, animated, drawing bodies fit inside a sixtieth of a frame.
    private const double ReleaseBudgetMilliseconds = 0.6;

    // An unoptimised build runs this workload some eight to nine times slower — every Vector2
    // operation and every small struct copy is a real call — so the same ceiling would fail every
    // Debug run. Scaled rather than skipped, so the workload is still exercised there, and scaled
    // to keep roughly the Release ratio between the measurement and the ceiling.
#if DEBUG
    private const double DebugScale = 10.0;
#else
    private const double DebugScale = 1.0;
#endif

    [Fact]
    public void AThousandCollidingAnimatedBodies_StayWithinTheStepBudgetAndAllocateNothing()
    {
        Scene scene = CrowdWorkload.Room();
        using SceneSimulation simulation = new(scene, null, StageWorkload.Defaults);

        StepSample[] samples = StepMeasurement.Measure(simulation, StepSeconds, WarmupSteps, MeasuredSteps);

        long allocated = 0;
        TimeSpan total = TimeSpan.Zero;
        foreach (StepSample sample in samples)
        {
            allocated += sample.AllocatedBytes;
            total += sample.Duration;
        }

        TimeSpan mean = total / samples.Length;
        TimeSpan budget = TimeSpan.FromMilliseconds(ReleaseBudgetMilliseconds * DebugScale);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{CrowdWorkload.Players} bodies: mean {mean.TotalMilliseconds:0.000} ms a step of {budget.TotalMilliseconds:0.000} ms, {allocated} bytes over {samples.Length} steps"));

        Assert.Equal(0, allocated);
        Assert.True(
            mean < budget,
            FormattableString.Invariant(
                $"{CrowdWorkload.Players} bodies averaged {mean.TotalMilliseconds:0.000} ms a step against a budget of {budget.TotalMilliseconds:0.000} ms."));
    }
}
