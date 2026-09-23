using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Physics;

public sealed class RidingTests
{
    private const string Platform = "platform";
    private const string Crate = "crate";

    // The platform's own position write carries its rider, so the rider sits flush on it whether
    // the platform moved before the rider's step or after it.
    [Theory]
    [InlineData(true, 0f, -1.5f)]
    [InlineData(true, 0f, 1.5f)]
    [InlineData(true, 1.5f, 0f)]
    [InlineData(false, 0f, -1.5f)]
    [InlineData(false, 0f, 1.5f)]
    [InlineData(false, 1.5f, 0f)]
    public void ARider_EndsEveryStepFlushOnItsPlatform_InEitherStepOrder(bool platformFirst, float x, float y)
    {
        Scene scene = new();
        Slab platform = new(new Vector2(0f, 40f), Platform);
        Rider rider = new(new Vector2(12f, 20f), Platform);
        scene.Add(platform);
        scene.Add(rider);
        Land(rider);

        Vector2 motion = new(x, y);
        for (int step = 0; step < 20; step++)
        {
            if (platformFirst)
            {
                platform.Position += motion;
                rider.Mover.Move(Gravity);
            }
            else
            {
                rider.Mover.Move(Gravity);
                platform.Position += motion;
            }

            Assert.True(rider.Mover.IsOnFloor);
            AssertOnTop(rider, platform);
            Assert.Equal(12f + (motion.X * (step + 1)), rider.Position.X, CollisionFixtures.Tolerance);
        }
    }

    [Fact]
    public void RidersStackedOnARider_AreCarriedExactly()
    {
        Scene scene = new();
        Rider upper = new(new Vector2(12f, 0f), Platform, Crate);
        Rider lower = new(new Vector2(10f, 20f), Platform) { Collider = { Layer = Crate } };
        Slab platform = new(new Vector2(0f, 40f), Platform);
        scene.Add(upper);
        scene.Add(lower);
        scene.Add(platform);
        Land(lower);
        Land(upper);

        for (int step = 0; step < 20; step++)
        {
            upper.Mover.Move(Gravity);
            lower.Mover.Move(Gravity);
            platform.Position += new Vector2(0.5f, -1f);

            AssertOnTop(lower, platform);
            AssertOnTop(upper, lower);
        }

        Assert.Equal(22f, upper.Position.X, CollisionFixtures.Tolerance);
    }

    // The bystander names no layer and stays put while the platform moves under and into it. The
    // other two name the layer in MovedBy alone, which also makes it block them: one lands on the
    // platform and rides it, and the one it rises into is shoved.
    [Fact]
    public void ABodyNotMovedByAPlatformsLayer_IsNeitherCarriedNorShoved_AndMovedByAloneBlocksCarriesAndShoves()
    {
        Scene scene = new();
        Slab platform = new(new Vector2(0f, 40f), Platform);
        Rider bystander = new(new Vector2(12f, 20f));
        Rider rider = new(new Vector2(2f, 20f), Platform);
        Rider shoved = new(new Vector2(24f, 34f), Platform);
        rider.Mover.BlocksOn(CollisionFixtures.Solid);
        shoved.Mover.BlocksOn(CollisionFixtures.Solid);
        scene.Add(platform);
        scene.Add(bystander);
        scene.Add(rider);
        scene.Add(shoved);
        Land(bystander);
        Land(rider);
        Vector2 landed = bystander.Position;

        platform.Position += new Vector2(3f, 0f);
        platform.Position += new Vector2(0f, -4f);

        Assert.Equal(landed, bystander.Position);
        Assert.Equal(5f, rider.Position.X, CollisionFixtures.Tolerance);
        AssertOnTop(rider, platform);
        Assert.Equal(new Vector2(24f, 30f), shoved.Position);
    }

    // Dropping the layer in play ends the ride at once, and naming it again rides from the next Move.
    [Fact]
    public void ChangingMovedByInPlay_EndsTheRideAtOnce_AndTheNextMoveRidesAgain()
    {
        Scene scene = new();
        Slab platform = new(new Vector2(0f, 40f), Platform);
        Rider rider = new(new Vector2(12f, 20f), Platform);
        scene.Add(platform);
        scene.Add(rider);
        Land(rider);

        rider.Mover.MovedBy();
        platform.Position += new Vector2(2f, 0f);
        Assert.Equal(12f, rider.Position.X);

        rider.Mover.MovedBy(Platform);
        rider.Mover.Move(Gravity);
        platform.Position += new Vector2(2f, 0f);
        Assert.Equal(14f, rider.Position.X, CollisionFixtures.Tolerance);
    }

    // The platform passes into the wall, since nothing stops a collider moved by a position write,
    // and the rider it carries is scraped off against the wall's face.
    [Fact]
    public void AWall_StopsACarry()
    {
        Scene scene = SceneFixtures.Terrain("....#", "....#", "....#");
        Slab platform = new(new Vector2(8f, 40f), Platform);
        Rider rider = new(new Vector2(30f, 20f), Platform);
        scene.Add(platform);
        scene.Add(rider);
        Land(rider);

        for (int step = 0; step < 10; step++)
        {
            platform.Position += new Vector2(4f, 0f);
        }

        Assert.Equal(48f, platform.Position.X);
        Assert.Equal(56f, rider.Position.X, CollisionFixtures.Tolerance);
    }

    [Fact]
    public void AShove_ClearsABodyFromThePushersPath_AndABodyPinnedAgainstAWallIsCrushed()
    {
        Scene scene = SceneFixtures.Terrain("#....", "#....", "#....");
        Slab pusher = new(new Vector2(60f, 20f), Platform, new Vector2(8f, 16f));
        Rider body = new(new Vector2(50f, 24f), Platform);
        List<ColliderContact2D> crushes = [];
        body.Mover.Crushed += crushes.Add;
        scene.Add(pusher);
        scene.Add(body);

        // Twenty units at a body two units away, ending wholly past where the body stood. The body is
        // met all the same and shoved the eighteen left, flush ahead of the pusher.
        pusher.Position += new Vector2(-20f, 0f);

        Assert.Equal(32f, body.Position.X, CollisionFixtures.Tolerance);
        Assert.Empty(crushes);

        // Flush against the wall at 16, the body has nowhere to go, and the pusher ends 2 units into it.
        body.Mover.Move(new Vector2(-20f, 0f));
        float flush = body.Position.X;
        pusher.Position = new Vector2(flush + 8f - 2f, 20f);

        ColliderContact2D crushed = Assert.Single(crushes);
        Assert.Same(pusher.Collider, crushed.OtherCollider);
        Assert.Equal(new Vector2(-1f, 0f), crushed.Normal);
        Assert.Equal(2f, crushed.Depth, CollisionFixtures.Tolerance);
        Assert.Equal(flush, body.Position.X, CollisionFixtures.Tolerance);
        Assert.True(body.Position.X >= 16f);

        // Back out, then one write that carries the pusher from 40 wholly past the pinned body to 4. It
        // met the body's face at flush + 8 and fell short of everything after that.
        pusher.Position = new Vector2(40f, 20f);
        pusher.Position = new Vector2(4f, 20f);

        Assert.Equal(2, crushes.Count);
        Assert.Same(pusher.Collider, crushes[1].OtherCollider);
        Assert.Equal(new Vector2(-1f, 0f), crushes[1].Normal);
        Assert.Equal(flush + 8f - 4f, crushes[1].Depth, CollisionFixtures.Tolerance);
        Assert.Equal(flush, body.Position.X, CollisionFixtures.Tolerance);
    }

    private static readonly Vector2 Gravity = new(0f, 1f);

    private static void Land(Rider rider)
    {
        rider.Mover.Move(new Vector2(0f, 40f));
        Assert.True(rider.Mover.IsOnFloor);
    }

    private static void AssertOnTop(Entity upper, Entity lower)
    {
        float gap = lower.Position.Y - (upper.Position.Y + Rider.Edge);
        Assert.Equal(0f, gap, CollisionFixtures.Tolerance);
    }

    /// <summary>A box on a layer, moved only by writes to its position.</summary>
    private sealed class Slab : Entity
    {
        internal Slab(Vector2 position, string layer, Vector2? size = null)
            : base(position)
        {
            Collider = new BoxCollider2D(size ?? new Vector2(32f, 8f)) { Layer = layer };
            Add(Collider);
        }

        internal BoxCollider2D Collider { get; }
    }

    /// <summary>An 8x8 body blocked by solid ground and platforms, moved by the layers named.</summary>
    private sealed class Rider : Entity
    {
        internal const float Edge = 8f;

        internal Rider(Vector2 position, params string[] movedBy)
            : base(position)
        {
            Collider = new BoxCollider2D(new Vector2(Edge, Edge));
            Add(Collider);
            Mover = new KinematicBody2D(Collider);
            Mover.BlocksOn(CollisionFixtures.Solid, Platform, Crate);
            Mover.MovedBy(movedBy);
            Add(Mover);
        }

        internal BoxCollider2D Collider { get; }

        internal KinematicBody2D Mover { get; }
    }
}
