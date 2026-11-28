using System.Numerics;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using static Capsule.Tests.Particles.ParticleFixtures;

namespace Capsule.Tests.Particles;

public sealed class ParticleFinishTests
{
    [Fact]
    public void ABurst_RemovesItsEntityOnTheStepItsLastParticleDies()
    {
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);
        Burst burst = new();
        burst.Emitter.Emit(3);
        scene.Add(burst);

        StepUntilEmpty(simulation, burst);

        Assert.Null(burst.SceneOrNull);
    }

    // Emitting stays true on a burst. The rate is what keeps a stream unfinished, and it finishes only once
    // emitting stops and its particles die. A pooled trail keeps the false its last life ended with, and one
    // turned off before its add waits in the scene however long it is left off. Turning it off in the scene
    // changes no value but still ends its run, and it leaves for its pool.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARateDrivenEmitter_FinishesOnlyOnceEmittingStopsInTheSceneAndItsParticlesDie(bool offBeforeAdd)
    {
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);
        EntityPool<Burst> pool = new(() => new Burst(), capacity: 1);
        Burst stream = pool.Take();
        stream.Emitter.Rate = 60f;
        stream.Emitter.Emitting = !offBeforeAdd;
        scene.Add(stream);

        for (int step = 0; step < 30; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
        }

        Assert.False(stream.Emitter.IsFinished);
        Assert.Same(scene, stream.SceneOrNull);

        stream.Emitter.Emitting = false;
        if (offBeforeAdd)
        {
            simulation.Step(SceneFixtures.Step());
        }
        else
        {
            StepUntilEmpty(simulation, stream);
        }

        Assert.Null(stream.SceneOrNull);
        Assert.Same(stream, pool.Take());
    }

    // A reused burst emits before its add, as a spawner places and fires it before Scene.Add. Each life
    // draws its own stream from the start, as a fresh emitter added in the same order does.
    [Fact]
    public void APooledBurstReAdded_StartsUnfinishedAndDrawsAsAFreshBurst()
    {
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);
        EntityPool<Burst> pool = new(() => new Burst(), capacity: 1);

        Burst first = pool.Take();
        Vector2[] firstLife = FirstStep(simulation, scene, first);
        StepUntilEmpty(simulation, first);

        Burst second = pool.Take();
        Assert.Same(first, second);
        Assert.False(second.Emitter.IsFinished);

        Vector2[] secondLife = FirstStep(simulation, scene, second);
        StepUntilEmpty(simulation, second);
        Assert.Null(second.SceneOrNull);

        SceneFixtures.HookScene freshScene = new();
        using SceneSimulation freshSimulation = new(freshScene);
        Assert.Equal(FirstStep(freshSimulation, freshScene, new Burst()), firstLife);

        // The discarded burst takes the first stream, as the first life did.
        SceneFixtures.HookScene secondScene = new();
        using SceneSimulation secondSimulation = new(secondScene);
        Burst discarded = new();
        secondScene.Add(discarded);
        secondScene.Remove(discarded);
        Assert.Equal(FirstStep(secondSimulation, secondScene, new Burst()), secondLife);
    }

    // Emits before the add, then steps once and reads where the particles drew.
    private static Vector2[] FirstStep(SceneSimulation simulation, Scene scene, Burst burst)
    {
        burst.Emitter.Emit(3);
        scene.Add(burst);
        simulation.Step(SceneFixtures.Step());

        return [.. simulation.View.Sprites.ToArray().Select(static intent => intent.Position)];
    }

    // Steps until the burst's particles are gone, asserting it stays in the scene on every step one lives.
    private static void StepUntilEmpty(SceneSimulation simulation, Burst burst)
    {
        int lived = 0;
        for (int step = 0; step < 120; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
            if (burst.Emitter.Alive == 0)
            {
                Assert.True(lived > 1, "the particles never lived past their first step");

                return;
            }

            lived++;
            Assert.False(burst.Emitter.IsFinished);
            Assert.NotNull(burst.SceneOrNull);
        }

        Assert.Fail("the particles never died");
    }

    private sealed class Burst : Entity
    {
        internal ParticleEmitter Emitter { get; } = new(Tile, capacity: 8)
        {
            Lifetime = new FloatRange(0.05f, 0.1f),
            Shape = EmitShape.Circle(4f),
            Speed = new FloatRange(10f, 40f),
            Spread = 360f,
            RemovesEntityWhenFinished = true,
        };

        internal Burst()
            : base(Vector2.Zero) =>
            Add(Emitter);
    }
}
