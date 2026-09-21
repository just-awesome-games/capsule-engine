using System.Numerics;
using Capsule.Animation;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class ParticleAllocationTests
{
    // A full pool that ages, frees, recycles and spawns every tick, drawn through the scene's own
    // FrameView every step: the whole path a workload of live particles runs.
    [Fact]
    public void AStepThatSpawnsAgesRecyclesAndDrawsAFullPool_AllocatesNothing()
    {
        Sprite tile = SceneFixtures.Frame(4, 4);
        ParticleEmitter emitter = new(tile, capacity: 64)
        {
            Rate = 64f * 60f,
            Lifetime = new FloatRange(0.1f, 0.1f),
            Shape = EmitShape.Circle(8f),
            Speed = new FloatRange(5f, 20f),
            Gravity = new Vector2(0f, 40f),
            ScaleOverLifetime = Curve.Linear(1f, 0f),
            Color = Gradient.Linear(ColorRgba.White, ColorRgba.Black),
        };

        Root root = new(Vector2.Zero);
        root.Add(emitter);

        SceneFixtures.HookScene scene = new(start: s => s.Add(root));
        SimulationHost host = new(scene);

        for (int index = 0; index < 100; index++)
        {
            host.Step();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0; index < 1000; index++)
        {
            host.Step();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(emitter.Alive > 0);
    }

    private sealed class Root(Vector2 position) : Entity(position);
}
