using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tests.Scenes;
using Capsule.Tiles;
using Body = Capsule.Tests.Scenes.SceneFixtures.Body;

namespace Capsule.Tests.Physics;

public sealed class ColliderContactReportTests
{
    [Fact]
    public void TwoCollidersReachEachOtherAndTheEntityBehindTheContact()
    {
        Scene scene = new();
        Body first = new(Vector2.Zero) { Position = Vector2.Zero };
        Body second = new(new Vector2(4f, 0f));
        first.Collider.Layer = "one";
        second.Collider.Layer = "two";
        first.Collider.Detects = new("two");
        first.Collider.ReportsContacts = true;

        Body? touched = null;
        first.Collider.ContactEntered += contact => touched = contact.OtherCollider?.Entity as Body;

        scene.Add(first);
        scene.Add(second);
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Same(second, touched);
    }

    // A contact carries the touched thing's layer as an index, and its name for a log line.
    [Fact]
    public void AContact_ReportsTheTouchedThingsLayerAndItsName()
    {
        Scene scene = new();
        Body player = new(Vector2.Zero);
        Body enemy = new(new Vector2(4f, 0f));
        player.Collider.Detects = new("enemy");
        player.Collider.ReportsContacts = true;
        enemy.Collider.Layer = "enemy";

        ColliderContact2D? seen = null;
        player.Collider.ContactEntered += contact => seen = contact;

        scene.Add(player);
        scene.Add(enemy);
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        ColliderContact2D contact = Assert.NotNull(seen);
        Assert.Equal(scene.Collision.Layer("enemy"), contact.Layer);
        Assert.Equal("enemy", contact.LayerName);
    }

    // Touching is exactly what the full overlap query finds, on every step and whatever the
    // broadphase did in between. One collider teleports in and out, escaping its fat bounds. One
    // drifts through at a crawl inside them. The reporter itself steps onto a still one.
    [Fact]
    public void Touching_MatchesTheOverlapQueryOnEveryStep()
    {
        Scene scene = new();
        Body sensor = new(Vector2.Zero);
        sensor.Collider.Size = new Vector2(32f, 32f);
        sensor.Collider.Detects = new(CollisionWorld2D.DefaultLayerName);
        sensor.Collider.ReportsContacts = true;
        Body jumper = new(new Vector2(500f, 0f));
        Body drifter = new(new Vector2(-40f, 12f));
        Body post = new(new Vector2(80f, 0f));
        scene.Add(sensor);
        scene.Add(jumper);
        scene.Add(drifter);
        scene.Add(post);

        using SimulationHost run = new(scene);
        Span<Contact2D> expected = stackalloc Contact2D[8];
        int touchingSteps = 0;
        for (int step = 0; step < 240; step++)
        {
            jumper.Teleport(step % 40 < 20 ? new Vector2(500f, 0f) : new Vector2(30f, 30f));
            drifter.Position += new Vector2(0.37f, 0f);
            sensor.Position = step < 200 ? Vector2.Zero : new Vector2(52f, 0f);
            run.Step();

            int count = sensor.Collider.OverlapAll(expected);
            ReadOnlySpan<ColliderContact2D> touching = sensor.Collider.Touching;
            Assert.Equal(count, touching.Length);
            for (int index = 0; index < count; index++)
            {
                Assert.Equal(expected[index].Target, touching[index].Target);
                Assert.Equal(expected[index].Point, touching[index].Point);
                Assert.Equal(expected[index].Normal, touching[index].Normal);
                Assert.Equal(expected[index].Depth, touching[index].Depth);
            }

            touchingSteps += count > 0 ? 1 : 0;
        }

        // Each of the three ways in was taken: the jumper's arrivals, the drifter's crossing and the
        // reporter's own step onto the post.
        Assert.True(touchingSteps > 120, $"only {touchingSteps} steps held a contact.");
        Assert.Contains(sensor.Collider.Touching.ToArray(), contact => contact.OtherCollider == post.Collider);
    }

    // A collider removed and replaced in its own slot, at the same place, within one step. The
    // reporter exits the removed one and enters its replacement, whose handle differs only by
    // generation.
    [Fact]
    public void AColliderReplacedInItsOwnSlot_ExitsTheOldAndEntersTheNew()
    {
        Scene scene = new();
        Body sensor = new(Vector2.Zero);
        sensor.Collider.Detects = new(CollisionWorld2D.DefaultLayerName);
        sensor.Collider.ReportsContacts = true;
        Body old = new(new Vector2(4f, 0f));
        scene.Add(sensor);
        scene.Add(old);

        List<string> log = [];
        sensor.Collider.ContactEntered += contact => log.Add(contact.OtherCollider == old.Collider ? "+old" : "+new");
        sensor.Collider.ContactExited += contact => log.Add(contact.OtherCollider == old.Collider ? "-old" : "-new");

        using SimulationHost run = new(scene);
        run.Step();

        ColliderHandle held = old.Collider.Handle;
        scene.Remove(old);
        Body replacement = new(new Vector2(4f, 0f));
        scene.Add(replacement);
        Assert.Equal(held.Index, replacement.Collider.Handle.Index);

        run.Step();

        Assert.Equal(["+old", "-old", "+new"], log);
        Assert.Same(replacement.Collider, Assert.Single(sensor.Collider.Touching.ToArray()).OtherCollider);
    }

    // A face is a surface only from the side it faces, and this is where a game reads that: a body
    // rising through a ledge is not standing on it on the way up, and is the moment it settles on
    // top. An enter while passing would fire a landing in mid-air.
    [Fact]
    public void AColliderRisingThroughATopFaceCell_EntersNoContactUntilItRestsOnTop()
    {
        Scene scene = Ledge();
        Body body = new(new Vector2(20f, 16f + (0.5f * CollisionTolerance.ContactSkin)));
        body.Collider.Detects = new("platform");
        body.Collider.ReportsContacts = true;
        body.Mover.BlockedBy = new("platform");

        List<ColliderContact2D> entered = [];
        body.Collider.ContactEntered += entered.Add;
        scene.Add(body);

        using SimulationHost run = new(scene);

        // Just under the face, inside the contact skin and on the far side of it: nothing touched.
        run.Step();
        Assert.Empty(entered);

        // Rising through it is not blocked, and meets nothing on the way.
        Assert.False(body.Mover.Move(new Vector2(0f, -20f)).Blocked);
        run.Step();
        Assert.Empty(entered);

        // Falling back onto it lands, and the contact carries the face's own normal.
        Assert.True(body.Mover.Move(new Vector2(0f, 20f)).Blocked);
        run.Step();

        ColliderContact2D contact = Assert.Single(entered);
        Assert.Equal(new Vector2(0f, -1f), contact.Normal);
        Assert.Equal("platform", contact.LayerName);
    }

    // One row of top-face-only tiles across the middle, so the face plane is y = 16.
    private static Scene Ledge()
    {
        Scene scene = new();
        scene.Add(new TileMap(new TileGrid(
            16,
            3,
            3,
            [TileGrid.EmptyTile, new TileType { Name = "ledge", Layer = "platform", OneWay = true }],
            [0, 0, 0, 1, 1, 1, 0, 0, 0])));

        return scene;
    }

    [Fact]
    public void ATileMapRegistersOneColliderWhoseCellsCarryTheAuthoredLayer()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        TileMap map = scene.FindSingle<TileMap>();

        Assert.NotNull(map.Collision);
        Assert.Equal(4, map.Collision.Width);
        Assert.Equal("solid", scene.Collision.NameOf(map.Collision.LayerAt(0, 1)!.Value));

        scene.Remove(map);

        Assert.Null(map.Collision);
        Assert.Empty(scene.Collision.Grids.ToArray());
    }

    [Fact]
    public void ATileMapWhosePaletteCollidesWithNothing_RegistersNoCollider()
    {
        Scene scene = new();
        TileMap map = new(new TileGrid(
            16,
            2,
            1,
            [TileGrid.EmptyTile, new TileType { Name = "decor" }],
            [0, 1]));
        scene.Add(map);

        Assert.Null(map.Collision);
        Assert.Empty(scene.Collision.Grids.ToArray());
    }
}
