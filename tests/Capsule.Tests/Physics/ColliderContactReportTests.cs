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

    // Contacts settle between the entity steps and the late steps. A late step's move shows on the next
    // settle, and the list a late step reads is the one that settle wrote.
    [Fact]
    public void Touching_KeepsTheSettledContactsWhenALateStepMovesTheCollider()
    {
        Body sensor = Sensor();
        Body post = new(new Vector2(4f, 0f));
        bool moveAway = false;
        SceneFixtures.HookScene scene = new(lateStep: (Scene _, in StepContext _) =>
        {
            if (moveAway)
            {
                sensor.Position = new Vector2(500f, 0f);
            }
        });
        scene.Add(sensor);
        scene.Add(post);

        using SimulationHost run = new(scene);
        run.Step();
        moveAway = true;
        run.Step();

        Assert.Same(post.Collider, Assert.Single(sensor.Collider.Touching.ToArray()).OtherCollider);

        moveAway = false;
        run.Step();

        Assert.Empty(sensor.Collider.Touching.ToArray());
    }

    // Disabling empties the list without touching the storage behind a span already read. Only the
    // next settle reuses it.
    [Fact]
    public void ASpanReadBeforeItsColliderIsDisabled_KeepsItsContacts()
    {
        Scene scene = new();
        Body sensor = Sensor();
        Body post = new(new Vector2(4f, 0f));
        scene.Add(sensor);
        scene.Add(post);

        using SimulationHost run = new(scene);
        run.Step();
        ReadOnlySpan<ColliderContact2D> read = sensor.Collider.Touching;
        sensor.Collider.Enabled = false;

        Assert.Empty(sensor.Collider.Touching.ToArray());
        Assert.Same(post.Collider, Assert.Single(read.ToArray()).OtherCollider);
    }

    // A collider out of the world at the settle settled nothing that step, so enabling it in a late
    // step neither fills the list nor raises an enter before the next step's settle.
    [Fact]
    public void AColliderEnabledAfterContactsSettle_TouchesNothingUntilTheNextStep()
    {
        Body sensor = Sensor();
        sensor.Collider.Enabled = false;
        Body post = new(new Vector2(4f, 0f));
        SceneFixtures.HookScene scene = new(lateStep: (Scene _, in StepContext _) => sensor.Collider.Enabled = true);
        scene.Add(sensor);
        scene.Add(post);
        int entered = 0;
        sensor.Collider.ContactEntered += _ => entered++;

        using SimulationHost run = new(scene);
        run.Step();

        Assert.True(sensor.Collider.Enabled);
        Assert.Empty(sensor.Collider.Touching.ToArray());
        Assert.Equal(0, entered);

        run.Step();

        Assert.Same(post.Collider, Assert.Single(sensor.Collider.Touching.ToArray()).OtherCollider);
        Assert.Equal(1, entered);
    }

    // An 8x8 body at the origin reporting what it touches on the default layer.
    private static Body Sensor()
    {
        Body sensor = new(Vector2.Zero);
        sensor.Collider.Detects = new(CollisionWorld2D.DefaultLayerName);
        sensor.Collider.ReportsContacts = true;
        return sensor;
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
        scene.Add(SceneFixtures.Colliding(new TileGrid(
            16,
            3,
            3,
            [TileGrid.EmptyTile, new TileType { Name = "ledge", Layer = "platform", OneWay = true }],
            [0, 0, 0, 1, 1, 1, 0, 0, 0])));

        return scene;
    }

    [Fact]
    public void ATileMapColliderRegistersOneGridWhoseCellsCarryTheAuthoredLayer()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        TileMapCollider2D collider = scene.FindSingle<TileMap>().Get<TileMapCollider2D>();

        Assert.NotNull(collider.Grid);
        Assert.Equal(4, collider.Grid.Width);
        Assert.Equal("solid", scene.Collision.NameOf(collider.Grid.LayerAt(0, 1)!.Value));

        scene.Remove(collider.Entity!);

        Assert.Null(collider.Grid);
        Assert.Empty(scene.Collision.Grids.ToArray());
    }
}
