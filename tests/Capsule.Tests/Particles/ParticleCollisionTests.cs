using System.Numerics;
using Capsule.Particles;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Particles;

public sealed class ParticleCollisionTests
{
    private const float Dt = 1f / 60f;

    private static readonly Sprite Tile = SceneFixtures.Frame(4, 4);
    private static readonly CollisionMask Solid = new("solid");

    // A step carries a particle from above the one-row floor, whose top is y = 32, to below it: at 2 400 units a second
    // its first whole step, and at 6 000 the half step it spawns with, which ends it before it takes a slot.
    [Theory]
    [InlineData(2400f)]
    [InlineData(6000f)]
    public void AKill_EndsAParticleWhereItsMoveMeetsAFloorThinnerThanTheStep_BeforeItDrawsPast(float speed)
    {
        (ParticleEmitter emitter, SimulationHost host) = Launch(
            ["....", "....", "####", "....", "...."],
            new Vector2(24f, 8f),
            new Vector2(0f, speed),
            ParticleCollision.Kill(Solid));

        for (int step = 0; step < 4; step++)
        {
            host.Step();
            Assert.All(Drawn(host), intent => Assert.True(intent.Position.Y < 32f, $"drew at {intent.Position}"));
        }

        Assert.Equal(0, emitter.Alive);
    }

    [Fact]
    public void ABounce_KeepsItsFractionOfTheSpeedIntoTheSurface_AndLosesItsFrictionAlongIt()
    {
        (_, SimulationHost host) = Launch(
            ["....", "....", "####"],
            new Vector2(8f, 8f),
            new Vector2(60f, 120f),
            ParticleCollision.Bounce(Solid, bounce: 0.5f, friction: 0.25f));

        Vector2 move;
        do
        {
            host.Step();
            move = Move(host);
        }
        while (move.Y >= 0f);

        host.Step();

        AssertClose(new Vector2(45f, -60f) * Dt, Move(host));
    }

    [Fact]
    public void ALanding_RestsOnTheSurfaceUnderGravity_WithoutSinkingOrCreeping()
    {
        (ParticleEmitter emitter, SimulationHost host) = Launch(
            ["....", "....", "####"],
            new Vector2(8f, 8f),
            new Vector2(60f, 0f),
            ParticleCollision.Bounce(Solid, bounce: 0f, friction: 1f));
        emitter.Gravity = new Vector2(0f, 600f);

        host.Step(60);

        for (int step = 0; step < 30; step++)
        {
            host.Step();
            SpriteIntent intent = Assert.Single(Drawn(host));
            Assert.InRange(intent.Position.Y, 32f - (2f * CollisionTolerance.ContactSkin), 32f);
            AssertClose(Vector2.Zero, intent.Position - intent.PreviousPosition);
        }
    }

    [Fact]
    public void AOneWayLedge_StopsAParticleFromAboveAndPassesOneFromBelow()
    {
        string[] rows = ["....", "----", "....", "...."];
        Scene scene = SceneFixtures.Terrain(rows);
        ParticleEmitter falling = Emitter(new Vector2(0f, 300f), ParticleCollision.Kill(Solid));
        ParticleEmitter rising = Emitter(new Vector2(0f, -300f), ParticleCollision.Kill(Solid));
        scene.Add(Holding(falling, new Vector2(8f, 4f)));
        scene.Add(Holding(rising, new Vector2(40f, 56f)));
        using SimulationHost host = new(scene);

        host.Step(12);

        Assert.Equal(0, falling.Alive);
        Assert.Equal(1, rising.Alive);
        Assert.True(Assert.Single(Drawn(host)).Position.Y < 16f);
    }

    [Fact]
    public void ACollisionInLocalSpace_Throws_WhicheverIsSetFirst()
    {
        Assert.Throws<InvalidOperationException>(() => new ParticleEmitter(Tile, 1) { Space = ParticleSpace.Local, Collision = ParticleCollision.Kill(Solid) });
        Assert.Throws<InvalidOperationException>(() => new ParticleEmitter(Tile, 1) { Collision = ParticleCollision.Kill(Solid), Space = ParticleSpace.Local });
    }

    internal static ParticleEmitter Emitter(Vector2 velocity, ParticleCollision collision)
    {
        ParticleEmitter emitter = new(Tile, capacity: 4)
        {
            Direction = velocity,
            Speed = new FloatRange(velocity.Length(), velocity.Length()),
            Lifetime = new FloatRange(10f, 10f),
            Collision = collision,
        };
        emitter.Emit(1);

        return emitter;
    }

    internal static EntityHierarchyFixtures.Node Holding(Renderer renderer, Vector2 position)
    {
        EntityHierarchyFixtures.Node node = new(position);
        node.Add(renderer);

        return node;
    }

    // The particles of one size the view holds, past the terrain's tiles.
    internal static SpriteIntent[] Drawn(SimulationHost host, float size = 4f) =>
        [.. host.Simulation.View.Sprites.ToArray().Where(intent => intent.Size == new Vector2(size))];

    private static (ParticleEmitter Emitter, SimulationHost Host) Launch(string[] rows, Vector2 at, Vector2 velocity, ParticleCollision collision)
    {
        Scene scene = SceneFixtures.Terrain(rows);
        ParticleEmitter emitter = Emitter(velocity, collision);
        scene.Add(Holding(emitter, at));

        return (emitter, new SimulationHost(scene));
    }

    private static Vector2 Move(SimulationHost host)
    {
        SpriteIntent intent = Assert.Single(Drawn(host));

        return intent.Position - intent.PreviousPosition;
    }

    private static void AssertClose(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) <= 1e-3f, $"expected {expected}, got {actual}");
}
