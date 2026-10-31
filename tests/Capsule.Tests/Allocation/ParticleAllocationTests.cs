using System.Numerics;
using Capsule.Animation;
using Capsule.Particles;
using Capsule.Physics;
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

        EntityHierarchyFixtures.Node root = new(Vector2.Zero);
        root.Add(emitter);

        SceneFixtures.HookScene scene = new(start: s => s.Add(root));
        SimulationHost host = new(scene);

        long bytes = StepMeasurement.Bytes(
            step =>
            {
                host.Step();
                _ = host.Simulation.View;
            },
            warmup: 100,
            measured: 1000);

        Assert.Equal(0, bytes);
        Assert.True(emitter.Alive > 0);
    }

    // Particles that swirl, bounce off a floor and die into a second emitter's bursts: every 11d path at once.
    [Fact]
    public void AStepThatSwirlsCollidesAndBurstsOnDeath_AllocatesNothing()
    {
        ParticleEmitter splash = new(SceneFixtures.Frame(2, 2), capacity: 64) { Lifetime = new FloatRange(0.1f, 0.1f) };
        ParticleEmitter drops = new(SceneFixtures.Frame(4, 4), capacity: 64)
        {
            Rate = 90f,
            Shape = EmitShape.Rect(48f, 0f),
            Lifetime = new FloatRange(0.4f, 0.6f),
            Gravity = new Vector2(0f, 600f),
            Noise = new ParticleNoise(20f, 16f) { Scroll = new Vector2(10f, 0f) },
            Collision = ParticleCollision.Bounce(new CollisionMask("solid"), bounce: 0.3f),
            EmitOnDeath = new SubEmission(splash, count: 1),
        };

        EntityHierarchyFixtures.Node root = new(new Vector2(32f, 4f));
        root.Add(drops);
        root.Add(splash);

        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        scene.Add(root);
        SimulationHost host = new(scene);

        long bytes = StepMeasurement.Bytes(
            step =>
            {
                host.Step();
                _ = host.Simulation.View;
            },
            warmup: 100,
            measured: 1000);

        Assert.Equal(0, bytes);
        Assert.True(splash.Alive > 0);
    }

    // A pooled entity's emitter is built ahead and first starts mid-run, when the entity first joins. Starting and
    // bursting then allocate nothing.
    [Fact]
    public void AnEmitterBuiltAhead_StartsAndBurstsMidRun_AllocatingNothing()
    {
        const int Warmup = 10;
        const int Measured = 50;
        EntityHierarchyFixtures.Node[] built = new EntityHierarchyFixtures.Node[Warmup + Measured];
        for (int index = 0; index < built.Length; index++)
        {
            built[index] = new EntityHierarchyFixtures.Node(Vector2.Zero);
            built[index].Add(new ParticleEmitter(SceneFixtures.Frame(4, 4), capacity: 4)
            {
                Lifetime = new FloatRange(0.05f, 0.05f),
                RemovesEntityWhenFinished = true,
            });
        }

        SimulationHost host = new(new SceneFixtures.HookScene());

        long bytes = StepMeasurement.Bytes(
            step =>
            {
                built[step].Get<ParticleEmitter>().Emit(4);
                host.Scene.Add(built[step]);
                host.Step();
                _ = host.Simulation.View;
            },
            warmup: Warmup,
            measured: Measured);

        Assert.Equal(0, bytes);
    }
}
