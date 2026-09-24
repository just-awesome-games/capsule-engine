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
        first.Collider.SetFilter("two");
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
        player.Collider.SetFilter("enemy");
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

    // A face is a surface only from the side it faces, and this is where a game reads that: a body
    // rising through a ledge is not standing on it on the way up, and is the moment it settles on
    // top. An enter while passing would fire a landing in mid-air.
    [Fact]
    public void AColliderRisingThroughATopFaceCell_EntersNoContactUntilItRestsOnTop()
    {
        Scene scene = Ledge();
        Body body = new(new Vector2(20f, 16f + (0.5f * CollisionTolerance.ContactSkin)));
        body.Collider.SetFilter("platform");
        body.Collider.ReportsContacts = true;
        body.Mover.BlocksOn("platform");

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
            [TileGrid.EmptyTile, new TileDefinition("ledge", null, "platform", OneWay: true)],
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
            [TileGrid.EmptyTile, new TileDefinition("decor", null)],
            [0, 1]));
        scene.Add(map);

        Assert.Null(map.Collision);
        Assert.Empty(scene.Collision.Grids.ToArray());
    }
}
