using System.Numerics;
using Capsule.Animation;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Xunit.Abstractions;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class ValueAnimatorAllocationTests(ITestOutputHelper output)
{
    // A struct with no IEquatable<T> is boxed by any generic equality check. Playing one on a loop that
    // changes key every step must still allocate nothing.
    [Fact]
    public void PlayingAStructWithNoEquality_AllocatesNothingPerStep()
    {
        Shade last = default;
        ValueAnimator<Shade> animator = new(shade => last = shade);
        using SimulationHost run = SceneFixtures.Simulate(new SceneFixtures.Holder(Vector2.Zero, animator));
        animator.Play(new ValueClip<Shade>([(new Shade(1), 1), (new Shade(2), 1)], loop: true));

        long bytes = StepMeasurement.Measure(run.Simulation).Sum(static sample => sample.StepBytes);

        StepMeasurement.Report(output, "a value animator stepping a struct with no equality", bytes);
        Assert.Equal(animator.Value.Level, last.Level);
    }

    private readonly struct Shade(int level)
    {
        public int Level { get; } = level;
    }
}
