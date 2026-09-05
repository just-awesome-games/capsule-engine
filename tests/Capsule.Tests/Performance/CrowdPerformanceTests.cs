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

    // A frame, the ceiling every mean-step gate claims (D-capsule-029): a tripwire for a collapse,
    // not for drift. The optimised step measures 0.3 ms on a desktop and 0.4 to 1.5 ms on hosted
    // runners; the collapse this exists to catch — the tree walked for every mover on layers none
    // detects — was eight times the step and reads on any of them. Drift shows in the printed
    // mean and in the desktop harness, never in the gate.
    private const double ReleaseBudgetMilliseconds = 16.0;

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
