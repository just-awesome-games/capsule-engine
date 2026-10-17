using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Physics;

// A body reads the velocity its floor carried it at over the last finished step, and the rider here reads it in
// its own step before it moves, as a controller about to jump does. The platform is a 32 by 8 slab at (0, 40),
// and the rider an 8 square standing on it.
public sealed class PlatformVelocityTests
{
    private const string Platform = "platform";

    private static readonly Vector2 Slide = new(60f, -30f);

    // The platform's step writes its position, which carries the rider whichever of the two steps first.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARider_ReadsItsPlatformsVelocity_InEitherStepOrder(bool platformFirst)
    {
        (Slab platform, Rider rider, SceneSimulation simulation) = Stand(platformFirst, Platform);
        platform.Velocity = Slide;

        Advance(simulation, 0, 5);

        AssertNear(Slide, rider.Read);
        AssertNear(Slide, rider.Mover.PlatformVelocity);
    }

    // A still collider with a surface velocity lends that velocity.
    [Fact]
    public void ABelt_LendsItsSurfaceVelocity()
    {
        (Slab belt, Rider rider, SceneSimulation simulation) = Stand(true, Platform);
        belt.Collider.SurfaceVelocity = new Vector2(-30f, 0f);

        Advance(simulation, 0, 5);

        AssertNear(new Vector2(-30f, 0f), rider.Read);
    }

    // A read during a step sees the step before, whichever steps first. A platform that turns and then stops
    // reads each speed one step late, and zero in the step after its still one.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APlatformThatTurnsAndStops_ReadsEachStepOneStepLate(bool platformFirst)
    {
        (Slab platform, Rider rider, SceneSimulation simulation) = Stand(platformFirst, Platform);
        Vector2 back = new(-60f, 0f);
        platform.Velocity = Slide;
        Advance(simulation, 0, 5);

        platform.Velocity = back;
        Advance(simulation, 5, 1);
        AssertNear(Slide, rider.Read);

        platform.Velocity = Vector2.Zero;
        Advance(simulation, 6, 1);
        AssertNear(back, rider.Read);

        Advance(simulation, 7, 1);
        Assert.Equal(Vector2.Zero, rider.Read);
        Assert.Equal(Vector2.Zero, rider.Mover.PlatformVelocity);
    }

    // The middle body rides the slab and is itself a belt. The top rider steps between the slab and the middle,
    // so in each step its carries arrive on both sides of its read, and it still reads the whole sum.
    [Fact]
    public void ARiderOnARider_ReadsEveryCarryOfTheStepBefore()
    {
        Scene scene = new();
        SceneSimulation simulation = new(scene);
        Slab bottom = new(new Vector2(0f, 40f)) { Velocity = new Vector2(60f, 0f) };
        Rider middle = new(new Vector2(4f, 32f), new Vector2(24f, 8f), Platform);
        middle.Collider.Layer = Platform;
        middle.Collider.SurfaceVelocity = new Vector2(30f, 0f);
        Rider top = new(new Vector2(12f, 24f), Platform);
        scene.Add(bottom);
        scene.Add(top);
        scene.Add(middle);

        Advance(simulation, 0, 5);

        AssertNear(new Vector2(90f, 0f), top.Read);
    }

    // A body its floor does not move rides nothing, and a move that leaves the floor ends the ride at once.
    [Fact]
    public void ABodyNotMovedByItsFloor_OrOffIt_ReadsZero()
    {
        (Slab platform, Rider unmoved, SceneSimulation simulation) = Stand(true);
        platform.Velocity = Slide;
        Advance(simulation, 0, 5);
        Assert.Equal(Vector2.Zero, unmoved.Read);

        (platform, Rider rider, simulation) = Stand(true, Platform);
        platform.Velocity = Slide;
        Advance(simulation, 0, 5);
        rider.Mover.Move(new Vector2(0f, -16f));
        Assert.Equal(Vector2.Zero, rider.Mover.PlatformVelocity);
    }

    private static (Slab Platform, Rider Rider, SceneSimulation Simulation) Stand(bool platformFirst, params string[] movedBy)
    {
        Scene scene = new();
        SceneSimulation simulation = new(scene);
        Slab platform = new(new Vector2(0f, 40f));
        Rider rider = new(new Vector2(12f, 32f), movedBy);
        scene.Add(platformFirst ? platform : rider);
        scene.Add(platformFirst ? rider : platform);

        return (platform, rider, simulation);
    }

    private static void Advance(SceneSimulation simulation, int from, int steps)
    {
        for (int step = from; step < from + steps; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
        }
    }

    private static void AssertNear(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, 0.01f);
        Assert.Equal(expected.Y, actual.Y, 0.01f);
    }

    /// <summary>A 32 by 8 slab on the platform layer that moves at its velocity each step.</summary>
    private sealed class Slab : Entity
    {
        internal Slab(Vector2 position)
            : base(position)
        {
            Collider = new BoxCollider2D(new Vector2(32f, 8f)) { Layer = Platform };
            Add(Collider);
        }

        internal BoxCollider2D Collider { get; }

        internal Vector2 Velocity { get; set; }

        protected internal override void OnStep(in StepContext context) => Position += Velocity * context.DeltaSeconds;
    }

    /// <summary>A body, 8 square by default, blocked by platforms and moved by the layers named, falling one unit a step.</summary>
    private sealed class Rider : Entity
    {
        internal Rider(Vector2 position, params string[] movedBy)
            : this(position, new Vector2(8f, 8f), movedBy)
        {
        }

        internal Rider(Vector2 position, Vector2 size, params string[] movedBy)
            : base(position)
        {
            Collider = new BoxCollider2D(size);
            Add(Collider);
            Mover = new KinematicBody2D(Collider) { BlockedBy = new(Platform), MovedBy = new(movedBy) };
            Add(Mover);
        }

        internal BoxCollider2D Collider { get; }

        internal KinematicBody2D Mover { get; }

        // PlatformVelocity as this step read it before the move.
        internal Vector2 Read { get; private set; }

        protected internal override void OnStep(in StepContext context)
        {
            Read = Mover.PlatformVelocity;
            Mover.Move(new Vector2(0f, 1f));
        }
    }
}
