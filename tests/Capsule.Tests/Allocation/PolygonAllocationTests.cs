using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class PolygonAllocationTests
{
    // A beam that opens from a line and spreads as it moves every step, with each step's frame built, as a
    // presenting host builds it.
    [Fact]
    public void APolygonReshapedEveryStep_AllocatesNothingOnceWarm()
    {
        ColorPolygon beam = new([Vector2.Zero, new(-20f, 120f), new(-20f, 120f)]) { Blend = BlendMode.Additive };
        SceneFixtures.Drifter holder = new(new Vector2(160f, 40f));
        holder.Add(beam);
        SimulationHost run = SceneFixtures.Simulate(holder);
        int drawn = 0;

        long bytes = StepMeasurement.Bytes(step =>
        {
            float spread = step % 40;
            beam.SetPoints([Vector2.Zero, new(-20f - spread, 120f), new(-20f + spread, 120f)]);
            run.Step();
            drawn += run.Simulation.View.Polygons.Length;
        });

        Assert.Equal(0, bytes);
        Assert.True(drawn > 0);
    }
}
