using System.Numerics;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Particles;

public sealed class ParticleEmitterMotionTests
{
    private static readonly Sprite Tile = SceneFixtures.Frame(4, 4);

    // An entity's move counts wherever in the step it lands, and a teleport never counts.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RateOverDistance_CountsAMoveFromEitherStep(bool late)
    {
        Traveller traveller = new(late);
        ParticleEmitter emitter = traveller.Add(new ParticleEmitter(Tile, capacity: 64) { RateOverDistance = 0.5f, Lifetime = 5f });
        using SimulationHost host = SceneFixtures.Simulate(traveller);

        host.Step(30);

        Assert.InRange(emitter.Alive, 58, 60);
    }

    // A parent change keeps the entity where it stands. The move before it still counts.
    [Fact]
    public void RateOverDistance_CountsAMoveAcrossAParentChange()
    {
        Traveller traveller = new(late: true);
        ParticleEmitter emitter = traveller.Add(new ParticleEmitter(Tile, capacity: 64) { RateOverDistance = 0.5f, Lifetime = 5f });
        EntityHierarchyFixtures.Node anchor = new(Vector2.Zero);
        using SimulationHost host = SceneFixtures.Simulate(traveller, anchor);

        for (int step = 0; step < 30; step++)
        {
            traveller.Parent = traveller.Parent is null ? anchor : null;
            host.Step();
        }

        Assert.InRange(emitter.Alive, 58, 60);
    }

    [Fact]
    public void RateOverDistance_SpawnsNothingAcrossATeleport()
    {
        (EntityHierarchyFixtures.Node entity, ParticleEmitter emitter, SimulationHost host) =
            ParticleEmitterTests.Build(new ParticleEmitter(Tile, capacity: 64) { RateOverDistance = 0.5f, Lifetime = 5f });
        host.Step(2);

        entity.Teleport(new Vector2(100f, 0f));
        host.Step(2);

        Assert.Equal(0, emitter.Alive);
    }

    // A trail turned off before it travelled far enough to spawn has nothing to wait for, and its entity still leaves.
    [Fact]
    public void AnEmitterStoppedBeforeItsFirstSpawn_Finishes_AndRemovesItsEntity()
    {
        (EntityHierarchyFixtures.Node entity, ParticleEmitter emitter, SimulationHost host) =
            ParticleEmitterTests.Build(new ParticleEmitter(Tile, capacity: 4) { RateOverDistance = 0.5f, RemovesEntityWhenFinished = true });
        host.Step();
        Assert.False(emitter.IsFinished);

        emitter.Emitting = false;
        host.Step();

        Assert.Null(entity.SceneOrNull);
    }

    // Built off, an emitter waits to be started or fed, however long that takes.
    [Fact]
    public void AnEmitterBuiltOff_DoesNotFinish_BeforeItSpawns()
    {
        (EntityHierarchyFixtures.Node entity, ParticleEmitter emitter, SimulationHost host) =
            ParticleEmitterTests.Build(new ParticleEmitter(Tile, capacity: 4) { Rate = 10f, Emitting = false, RemovesEntityWhenFinished = true });

        host.Step(3);

        Assert.False(emitter.IsFinished);
        Assert.NotNull(entity.SceneOrNull);
    }

    // A trail detached from what it rides keeps its particles through that entity's removal, stops, and leaves when they die.
    [Fact]
    public void ATrailDetachedBeforeItsParentLeaves_OutlivesIt_ThenLeaves()
    {
        (EntityHierarchyFixtures.Node trail, ParticleEmitter emitter, SimulationHost host) =
            ParticleEmitterTests.Build(new ParticleEmitter(Tile, capacity: 8) { Rate = 60f, Lifetime = 0.1f, RemovesEntityWhenFinished = true });
        EntityHierarchyFixtures.Node source = new(Vector2.Zero);
        host.Scene.Add(source);
        trail.Parent = source;
        host.Step(2);
        Assert.True(emitter.Alive > 0);

        trail.Parent = null;
        emitter.Emitting = false;
        source.Scene.Remove(source);
        host.Step();

        Assert.Null(source.SceneOrNull);
        Assert.True(emitter.Alive > 0, "its particles left with its parent");

        host.Step(10);

        Assert.Null(trail.SceneOrNull);
    }

    private sealed class Traveller(bool late) : Entity(Vector2.Zero)
    {
        private static readonly Vector2 Move = new(4f, 0f);

        protected internal override void OnStep(in StepContext context)
        {
            if (!late)
            {
                Position += Move;
            }
        }

        protected internal override void OnLateStep(in StepContext context)
        {
            if (late)
            {
                Position += Move;
            }
        }
    }
}
