using System.Globalization;
using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

using Body = Capsule.Tests.Scenes.SceneFixtures.Body;

namespace Capsule.Tests.Physics;

public sealed class CollisionMaskTests
{
    // Two worlds that interned the same names in opposite orders hold them at swapped indices. A
    // filter resolved in one and reused in the other would hit the wrong wall.
    [Fact]
    public void AMaskBuiltBeforeAnyWorld_HitsItsOwnLayersInWorldsThatIndexThemDifferently()
    {
        CollisionMask climbable = new(CollisionFixtures.Climb);
        CollisionWorld2D first = TwoWalls(CollisionFixtures.Climb, CollisionFixtures.Solid);
        CollisionWorld2D second = TwoWalls(CollisionFixtures.Solid, CollisionFixtures.Climb);
        Assert.NotEqual(first.Layer(CollisionFixtures.Climb).Index, second.Layer(CollisionFixtures.Climb).Index);

        foreach (CollisionWorld2D world in (CollisionWorld2D[])[first, second, first, second])
        {
            Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, climbable, out RayHit2D hit));
            Assert.Equal(world.Layer(CollisionFixtures.Climb), hit.Target.Layer);
            Assert.Equal(40f, hit.Distance, 3);
        }
    }

    // A mask interns its names as SetFilter does, so a layer named before any collider is on it is
    // still hit once one is. A world with no room left refuses the name the way Layer does.
    [Fact]
    public void AMaskNamingAnUndeclaredLayer_InternsItOrThrowsAsSetFilterDoesWhenTheWorldIsFull()
    {
        CollisionMask climbable = new(CollisionFixtures.Climb);
        CollisionWorld2D world = new();
        Assert.False(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, climbable, out _));

        world.Add(Shape2D.Box(new Vector2(40f, -8f), new Vector2(8f, 16f)), Vector2.Zero, world.Layer(CollisionFixtures.Climb));
        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, climbable, out _));

        CollisionWorld2D full = new();
        for (int index = 1; index < CollisionWorld2D.MaxLayers; index++)
        {
            full.Layer(index.ToString(CultureInfo.InvariantCulture));
        }

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => full.Raycast(Vector2.Zero, Vector2.UnitX, 200f, climbable, out _));
        Assert.Contains($"its {CollisionWorld2D.MaxLayers} layers", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new CollisionMask(CollisionFixtures.Climb, " "));
    }

    // The game's wall probe: a collider sweeping against a mask passes the solid body in front and
    // stops at the climbable one, though its own filter detects nothing.
    [Fact]
    public void AColliderCastByMask_StopsAtTheMaskedLayerAndPassesTheRest()
    {
        CollisionMask climbable = new(CollisionFixtures.Climb);
        Scene scene = new();
        Body player = new(Vector2.Zero);
        Body crate = new(new Vector2(12f, 0f));
        Body wall = new(new Vector2(30f, 0f));
        crate.Collider.Layer = CollisionFixtures.Solid;
        wall.Collider.Layer = CollisionFixtures.Climb;
        scene.Add(player);
        scene.Add(crate);
        scene.Add(wall);

        Assert.True(player.Collider.Cast(new Vector2(40f, 0f), climbable, out ShapeCastHit2D hit));

        Assert.Equal(wall.Collider.Handle, hit.Target.Collider);
        Assert.Equal(22f / 40f, hit.Fraction, 3);
    }

    // A refused query throws what its filter form throws and leaves the world's layers alone, even
    // in a world with no room left for the mask's name.
    [Fact]
    public void AnInvalidQueryByMask_ThrowsItsArgumentErrorAndInternsNothing()
    {
        CollisionMask climbable = new(CollisionFixtures.Climb);
        CollisionWorld2D full = new();
        for (int index = 1; index < CollisionWorld2D.MaxLayers; index++)
        {
            full.Layer(index.ToString(CultureInfo.InvariantCulture));
        }

        Contact2D[] contacts = new Contact2D[4];
        Assert.ThrowsAny<ArgumentException>(() => full.Raycast(Vector2.Zero, Vector2.Zero, 10f, climbable, out _));
        Assert.ThrowsAny<ArgumentException>(() => full.ShapeCast(default, Vector2.Zero, Vector2.UnitX, climbable, out _));
        Assert.ThrowsAny<ArgumentException>(() => full.OverlapColliderAll(default, climbable, contacts));
        Assert.ThrowsAny<ArgumentException>(() => full.Move(Shape2D.Circle(Vector2.Zero, 4f), Vector2.Zero, new Vector2(float.NaN, 0f), climbable, contacts));

        Scene scene = new();
        Body player = new(Vector2.Zero);
        scene.Add(player);
        int layers = scene.Collision.LayerCount;
        Assert.Throws<ArgumentOutOfRangeException>(() => player.Collider.Raycast(Vector2.UnitX, 0f, climbable, out _));

        Assert.Equal(CollisionWorld2D.MaxLayers, full.LayerCount);
        Assert.Equal(layers, scene.Collision.LayerCount);
    }

    // A world declaring two layers in the given order, with a solid wall at x 10 and a climbable
    // wall behind it at x 40.
    private static CollisionWorld2D TwoWalls(string first, string second)
    {
        CollisionWorld2D world = new();
        world.Layer(first);
        world.Layer(second);
        world.Add(Shape2D.Box(new Vector2(10f, -8f), new Vector2(8f, 16f)), Vector2.Zero, world.Layer(CollisionFixtures.Solid));
        world.Add(Shape2D.Box(new Vector2(40f, -8f), new Vector2(8f, 16f)), Vector2.Zero, world.Layer(CollisionFixtures.Climb));

        return world;
    }
}
