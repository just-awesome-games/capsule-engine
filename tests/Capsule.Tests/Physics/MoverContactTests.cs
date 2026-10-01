using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

public sealed class MoverContactTests
{
    private const float Tolerance = CollisionFixtures.Tolerance;

    [Fact]
    public void MoveBox_IsBlockedByAnotherColliderAndIgnoresItsOwn()
    {
        CollisionWorld2D world = new();
        CollisionLayer body = world.Layer("body");
        ColliderHandle self = world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, body);
        world.Add(Shape2D.Box(new Vector2(40f, 0f), new Vector2(8f, 8f)), Vector2.Zero, body);

        MoveResult2D blocked = world.MoveBox(
            CollisionFixtures.Box(0f, 0f, 8f, 8f),
            new Vector2(60f, 0f),
            CollisionFilter.Of(body),
            default,
            self);

        Assert.True(blocked.Blocked);
        Assert.Equal(32f, blocked.Translation.X, Tolerance);

        MoveResult2D unfiltered = world.MoveBox(
            CollisionFixtures.Box(0f, 0f, 8f, 8f),
            new Vector2(60f, 0f),
            CollisionFilter.None,
            default,
            self);

        Assert.False(unfiltered.Blocked);
    }

    [Fact]
    public void MoveBox_LeavesAnOverlapItAlreadyStartedInsteadOfFreezingInIt()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "..#", "..#");

        MoveResult2D escaping = world.MoveBox(
            CollisionFixtures.Box(34f, 4f, 8f, 8f),
            new Vector2(-20f, 0f),
            CollisionFilter.Everything,
            default);

        Assert.Equal(-20f, escaping.Translation.X, Tolerance);
    }


    // Handles and layers never compare across worlds, so what two runs owe each other is the same
    // surfaces in the same order: cells, layer names and normals.
    [Fact]
    public void MoveBox_ProducesTheSameContactsForTheSameInputsOnAFreshWorld()
    {
        static (Vector2 Translation, (bool Tile, int X, int Y, string Layer, Vector2 Normal)[] Contacts) Run()
        {
            CollisionWorld2D world = new();
            CollisionFixtures.Paint(world, "..#", "####");
            world.Add(
                Shape2D.Circle(new Vector2(20f, 6f), 3f),
                Vector2.Zero,
                world.Layer("pickup"));

            Contact2D[] contacts = new Contact2D[8];
            MoveResult2D result = world.MoveBox(
                CollisionFixtures.Box(0f, 8f, 8f, 8f),
                new Vector2(40f, 12f),
                CollisionFilter.Everything,
                contacts);

            return (
                result.Translation,
                [.. contacts[..result.ContactCount].Select(contact => (
                    contact.Target.IsGridCell,
                    contact.Target.CellX,
                    contact.Target.CellY,
                    world.NameOf(contact.Target.Layer),
                    contact.Normal))]);
        }

        (Vector2 first, (bool, int, int, string, Vector2)[] firstContacts) = Run();
        (Vector2 second, (bool, int, int, string, Vector2)[] secondContacts) = Run();

        Assert.Equal(first, second);
        Assert.Equal(firstContacts, secondContacts);
        Assert.NotEmpty(firstContacts);
    }

    // The tree hands its proxies back in whatever shape it currently holds, so the collider half of
    // a move's contacts is ordered by handle before the caller sees it: two worlds holding the same
    // colliders under the same handles write the same contacts in the same order.
    [Fact]
    public void MoveBox_OrdersItsColliderContactsByHandleWhateverShapeTheTreeIsIn()
    {
        static int[] Run(bool churn)
        {
            CollisionWorld2D world = new();
            CollisionLayer wall = world.Layer("wall");
            for (int index = 0; index < 6; index++)
            {
                world.Add(
                    Shape2D.Box(new Vector2(index * 10f, 32f), new Vector2(8f, 8f)),
                    Vector2.Zero,
                    wall);
            }

            if (churn)
            {
                // Added after the six, so they take later slots and leave the handles above alone;
                // inserting and removing them rebalances the hierarchy over the same colliders.
                ColliderHandle[] decoys = new ColliderHandle[24];
                for (int index = 0; index < decoys.Length; index++)
                {
                    decoys[index] = world.Add(
                        Shape2D.Box(new Vector2(((index * 37) % 400) - 200f, ((index * 53) % 200) - 100f), new Vector2(6f, 6f)),
                        Vector2.Zero,
                        wall);
                }

                for (int index = decoys.Length - 1; index >= 0; index--)
                {
                    world.Remove(decoys[index]);
                }
            }

            Contact2D[] contacts = new Contact2D[8];
            MoveResult2D result = world.MoveBox(
                CollisionFixtures.Box(0f, 0f, 58f, 8f),
                new Vector2(0f, 40f),
                CollisionFilter.Everything,
                contacts);

            Assert.True(result.Blocked);

            return [.. contacts[..result.ContactCount].Select(contact => contact.Target.Collider.Index)];
        }

        int[] plain = Run(false);
        int[] churned = Run(true);

        Assert.Equal([0, 1, 2, 3, 4, 5], plain);
        Assert.Equal(plain, churned);
    }

    // A move reports every surface within a hair of the nearest one it met. The broadphase's shape
    // follows the order colliders were added and moved in, and the band does not. The far-flung
    // sweep puts the nearest two boxes within the hair of each other and the third just past it.
    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(2, 1, 0)]
    [InlineData(1, 0, 2)]
    public void MoveBox_ReportsTheSurfacesNearestItWhateverOrderTheyArrivedIn(int first, int second, int third)
    {
        CollisionWorld2D world = new();
        float[] starts = [0f, 10.005f, 13.015f];
        float[] ends = [3f, 13.005f, 13.015f];
        int[] order = [first, second, third];
        ColliderHandle[] boxes = new ColliderHandle[3];
        foreach (int box in order)
        {
            boxes[box] = world.Add(Shape2D.Box(Vector2.Zero, Vector2.One), new Vector2(starts[box], 0f), world.FindLayer(CollisionWorld2D.DefaultLayerName));
        }

        foreach (int box in order)
        {
            world.SetPosition(boxes[box], new Vector2(ends[box], 0f));
        }

        Contact2D[] contacts = new Contact2D[4];
        MoveResult2D result = world.MoveBox(
            CollisionFixtures.Box(0f, 0f, 1f, 1f),
            new Vector2(100100f, 0f),
            CollisionFilter.Everything,
            contacts);

        Assert.Equal(2, result.ContactCount);
        Assert.Equal(
            boxes[..2].OrderBy(handle => handle.Index),
            contacts[..result.ContactCount].Select(contact => contact.Target.Collider).OrderBy(handle => handle.Index));
    }
}
