using System.Numerics;
using Capsule.Particles;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using static Capsule.Tests.Particles.ParticleCollisionTests;

namespace Capsule.Tests.Particles;

public sealed class SubEmissionTests
{
    public enum Death
    {
        Expiry,
        KillZone,
        Collision,
        CollisionAtSpawn,
    }

    // The burst's emitter sits on a moved, mirrored entity and mirrors its own spawns. A burst lands on the death
    // point only if both mirrors and the move are undone.
    [Theory]
    [InlineData(Death.Expiry)]
    [InlineData(Death.KillZone)]
    [InlineData(Death.Collision)]
    [InlineData(Death.CollisionAtSpawn)]
    public void ADeath_BurstsTheCountCentredOnTheWorldPointItDiedAt(Death death)
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        ParticleEmitter burst = new(SceneFixtures.Frame(2, 2), capacity: 8) { FlipX = true, Lifetime = new FloatRange(1f, 1f) };
        EntityHierarchyFixtures.Node mirrored = Holding(burst, new Vector2(50f, 20f));
        mirrored.Scale = new Vector2(-2f, 1f);

        ParticleEmitter source = death switch
        {
            Death.Expiry => Still(lifetime: 0.1f),
            Death.KillZone => Zoned(Still(lifetime: 10f)),
            Death.Collision => Emitter(new Vector2(0f, 300f), ParticleCollision.Kill(new CollisionMask("solid"))),
            _ => Emitter(new Vector2(0f, 6000f), ParticleCollision.Kill(new CollisionMask("solid"))),
        };
        source.EmitOnDeath = new SubEmission(burst, count: 3);

        // The burst's emitter starts first. A death as the source starts then has somewhere to land.
        scene.Add(mirrored);
        scene.Add(Holding(source, new Vector2(24f, 8f)));
        using SimulationHost host = new(scene);

        host.Step(20);

        Vector2 expected = death is Death.Collision or Death.CollisionAtSpawn ? new Vector2(24f, 32f) : new Vector2(24f, 8f);
        Assert.Equal(0, source.Alive);
        Assert.Equal(3, burst.Alive);
        Assert.All(Drawn(host, size: 2f), intent => Assert.True(Vector2.Distance(expected, intent.Position) <= 1e-3f, $"burst at {intent.Position}"));
    }

    // A burst into an emitter out of the scene is dropped, not held for a rejoin that would place it elsewhere.
    [Fact]
    public void ClearAFullPoolsReplacementAndATargetOutOfTheScene_BurstNothing()
    {
        ParticleEmitter burst = new(SceneFixtures.Frame(2, 2), capacity: 8) { Lifetime = new FloatRange(1f, 1f) };
        ParticleEmitter source = Still(lifetime: 10f);
        source.EmitOnDeath = new SubEmission(burst, count: 1);
        EntityHierarchyFixtures.Node target = Holding(burst, Vector2.Zero);
        SceneFixtures.HookScene scene = new();
        scene.Add(Holding(source, Vector2.Zero));
        scene.Add(target);
        using SimulationHost host = new(scene);

        host.Step();
        source.Emit(1);
        host.Step();
        source.Clear();
        host.Step();
        Assert.Equal(0, burst.Alive);

        scene.Remove(target);
        source.Lifetime = new FloatRange(0.05f, 0.05f);
        source.Emit(1);
        host.Step(5);
        scene.Add(target);
        host.Step();
        Assert.Equal(0, source.Alive);
        Assert.Equal(0, burst.Alive);
    }

    [Fact]
    public void ABurstIntoItsOwnEmitter_Throws()
    {
        ParticleEmitter emitter = new(SceneFixtures.Frame(2, 2), capacity: 1);

        Assert.Throws<ArgumentException>(() => emitter.EmitOnDeath = new SubEmission(emitter, count: 1));
    }

    // The source's last particle dies long before the burst it spawned. Removing the entity then would cut the burst short.
    [Fact]
    public void AnEmitterBurstingIntoOneOnItsOwnEntity_FinishesOnlyOnceThatOneIsEmptyToo()
    {
        ParticleEmitter burst = new(SceneFixtures.Frame(2, 2), capacity: 8) { Lifetime = new FloatRange(0.5f, 0.5f) };
        ParticleEmitter source = Still(lifetime: 0.05f);
        source.RemovesEntityWhenFinished = true;
        source.EmitOnDeath = new SubEmission(burst, count: 2);
        EntityHierarchyFixtures.Node effect = Holding(source, Vector2.Zero);
        effect.Add(burst);
        SceneFixtures.HookScene scene = new();
        scene.Add(effect);
        using SimulationHost host = new(scene);

        host.Step(10);
        Assert.Equal(0, source.Alive);
        Assert.Equal(2, burst.Alive);
        Assert.NotNull(effect.SceneOrNull);

        host.Step(40);
        Assert.Null(effect.SceneOrNull);
    }

    private static ParticleEmitter Still(float lifetime)
    {
        ParticleEmitter emitter = new(SceneFixtures.Frame(4, 4), capacity: 1) { Lifetime = new FloatRange(lifetime, lifetime) };
        emitter.Emit(1);

        return emitter;
    }

    // Dies on its first step, where it spawned.
    private static ParticleEmitter Zoned(ParticleEmitter emitter)
    {
        emitter.KillZone = Shape2D.Circle(Vector2.Zero, 4f);

        return emitter;
    }
}
