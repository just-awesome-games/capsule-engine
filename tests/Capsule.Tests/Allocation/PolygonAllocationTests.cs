using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class PolygonAllocationTests
{
    // A beam that opens from a line and spreads as it moves every step, drawn by an unlit entity over the light
    // it casts, with each step's frame built, as a presenting host builds it.
    [Fact]
    public void APolygonAndItsLightReshapedEveryStep_AllocateNothingOnceWarm()
    {
        ColorPolygon beam = new([Vector2.Zero, new(-20f, 120f), new(-20f, 120f)]) { Blend = BlendMode.Additive };
        PolygonLight light = new([Vector2.Zero, new(-20f, 120f), new(-20f, 120f)]);
        SceneFixtures.Drifter holder = new(new Vector2(160f, 40f)) { Unlit = true };
        holder.Add(beam);
        holder.Add(light);
        SimulationHost run = SceneFixtures.Simulate(holder);
        int drawn = 0;

        long bytes = StepMeasurement.Bytes(step =>
        {
            float spread = step % 40;
            beam.SetPoints([Vector2.Zero, new(-20f - spread, 120f), new(-20f + spread, 120f)]);
            light.SetPoints([Vector2.Zero, new(-20f - spread, 120f), new(-20f + spread, 120f)]);
            run.Step();
            drawn += run.Simulation.View.Polygons.Length + run.Simulation.View.LightPolygons.Length;
        });

        Assert.Equal(0, bytes);
        Assert.True(drawn > 1);
    }
}
