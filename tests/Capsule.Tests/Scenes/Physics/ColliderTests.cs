using System.Numerics;
using Capsule.Collision;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Physics;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Scenes.Physics;

public sealed class ColliderTests
{
    [Fact]
    public void ACollider_RegistersWhenItsEntityJoinsAndUnregistersWhenItLeaves()
    {
        Scene scene = new();
        Body body = new(new Vector2(10f, 10f));

        Assert.Null(body.Collider.World);
        Assert.True(body.Collider.Handle.IsNone);

        scene.Add(body);

        Assert.Same(scene.Collision, body.Collider.World);
        Assert.True(scene.Collision.Contains(body.Collider.Handle));

        ColliderHandle held = body.Collider.Handle;
        scene.Remove(body);

        Assert.Null(body.Collider.World);
        Assert.False(scene.Collision.Contains(held));
    }

    [Fact]
    public void AColliderAttachedToAnEntityAlreadyInAScene_RegistersImmediately()
    {
        Scene scene = new();
        SceneFixtures.Drifter drifter = new(Vector2.Zero);
        scene.Add(drifter);

        BoxCollider2D collider = new(new Vector2(8f, 8f));
        drifter.Add(collider);

        Assert.Same(scene.Collision, collider.World);

        drifter.Remove(collider);

        Assert.Null(collider.World);
    }

    [Fact]
    public void AColliderFollowsItsEntity_ThroughAWriteAndThroughATeleport()
    {
        Scene scene = new();
        Body body = new(Vector2.Zero);
        scene.Add(body);

        body.Position = new Vector2(40f, 0f);
        Assert.Equal(new Vector2(40f, 0f), scene.Collision.PositionOf(body.Collider.Handle));

        body.Teleport(new Vector2(-25f, 12f));
        Assert.Equal(new Vector2(-25f, 12f), scene.Collision.PositionOf(body.Collider.Handle));
    }

    [Fact]
    public void AColliderIsPlacedByItsOffsetTheWayAQuadRendererIs()
    {
        Scene scene = new();
        Body body = new(new Vector2(100f, 100f));
        body.Collider.Offset = new Vector2(-4f, -8f);
        scene.Add(body);

        Assert.Equal(new Vector2(96f, 92f), body.Collider.Bounds.Min);
        Assert.Equal(new Vector2(104f, 100f), body.Collider.Bounds.Max);
    }

    [Fact]
    public void Move_LeavesTheEntityWhereTheSweepStoppedAndNamesWhatStoppedIt()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        Body body = new(new Vector2(8f, 8f));
        body.Mover.BlocksOn("solid");
        scene.Add(body);

        MoveResult2D result = body.Mover.Move(new Vector2(0f, 60f));

        Assert.True(result.BlockedY);
        Assert.Equal(24f, body.Position.Y, 2f * CollisionWorld2D.LinearSlop);
        Assert.NotEmpty(body.Mover.MoveContacts.ToArray());
        Assert.All(
            body.Mover.MoveContacts.ToArray(),
            contact =>
            {
                Assert.True(contact.Cell.HasValue);
                Assert.Equal("solid", contact.LayerName);
                Assert.Equal(new Vector2(0f, -1f), contact.Normal);
            });
    }

    [Fact]
    public void Move_OnAColliderInNoScene_SaysSo()
    {
        Body body = new(Vector2.Zero);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => body.Mover.Move(Vector2.UnitX));

        Assert.Contains("registered in a scene", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADetectedContact_DoesNotBlockAMoverThatDoesNotBlockOnItsLayer()
    {
        Scene scene = new();
        Body player = new(Vector2.Zero);
        Body enemy = new(new Vector2(10f, 0f));
        player.Collider.Layer = "player";
        player.Collider.Detects("enemy");
        player.Collider.ReportsContacts = true;
        player.Mover.BlocksOn("solid");
        enemy.Collider.Layer = "enemy";

        List<ColliderContact2D> entered = [];
        player.Collider.ContactEntered += entered.Add;

        scene.Add(player);
        scene.Add(enemy);
        using SceneSimulation simulation = new(scene);

        MoveResult2D result = player.Mover.Move(new Vector2(12f, 0f));
        simulation.Step(SceneFixtures.Step(0));

        Assert.False(result.BlockedX);
        Assert.Equal(new Vector2(12f, 0f), player.Position);
        Assert.Empty(player.Mover.MoveContacts.ToArray());

        ColliderContact2D contact = Assert.Single(entered);
        Assert.Same(enemy, contact.OtherEntity);
        Assert.Same(enemy.Collider, contact.OtherCollider);
        Assert.Null(contact.Cell);
        Assert.True(float.IsFinite(contact.Point.X));
        Assert.True(float.IsFinite(contact.Point.Y));
    }

    [Fact]
    public void ADisabledCollider_LeavesTheWorldAndCanBeEnabledAgain()
    {
        Scene scene = new();
        Body first = new(Vector2.Zero);
        Body second = new(new Vector2(4f, 0f));
        first.Collider.Detects("other");
        first.Collider.ReportsContacts = true;
        second.Collider.Layer = "other";

        int entered = 0;
        int exited = 0;
        first.Collider.ContactEntered += _ => entered++;
        first.Collider.ContactExited += _ => exited++;

        scene.Add(first);
        scene.Add(second);
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        ColliderHandle original = first.Collider.Handle;
        Assert.Equal(1, entered);

        first.Collider.Enabled = false;

        Assert.False(scene.Collision.Contains(original));
        Assert.Null(first.Collider.World);
        Assert.True(first.Collider.Handle.IsNone);
        Assert.Empty(first.Collider.Touching.ToArray());
        Assert.Equal(1, exited);
        Assert.Throws<InvalidOperationException>(() => first.Mover.Move(Vector2.UnitX));

        first.Collider.Enabled = true;
        simulation.Step(SceneFixtures.Step(1));

        Assert.Same(scene.Collision, first.Collider.World);
        Assert.Equal(2, entered);
    }

    [Fact]
    public void ContactEvents_FireOnceOnEnterAndOnceOnExit()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        Body body = new(new Vector2(4f, 8f));
        body.Collider.Detects("solid");
        body.Mover.BlocksOn("solid");
        body.Collider.ReportsContacts = true;

        List<string> log = [];
        body.Collider.ContactEntered += contact => log.Add($"+{contact.LayerName}({contact.Cell!.Value.X},{contact.Cell.Value.Y})");
        body.Collider.ContactExited += contact => log.Add($"-{contact.LayerName}({contact.Cell!.Value.X},{contact.Cell.Value.Y})");

        scene.Add(body);
        using SceneSimulation simulation = new(scene);

        // Falling onto the floor, resting on it, then being lifted off it.
        simulation.Step(SceneFixtures.Step(0));
        Assert.Empty(log);

        body.Mover.Move(new Vector2(0f, 60f));
        simulation.Step(SceneFixtures.Step(1));
        Assert.Equal(["+solid(0,2)"], log);

        simulation.Step(SceneFixtures.Step(2));
        Assert.Equal(["+solid(0,2)"], log);

        body.Teleport(new Vector2(4f, -100f));
        simulation.Step(SceneFixtures.Step(3));
        Assert.Equal(["+solid(0,2)", "-solid(0,2)"], log);
    }

    [Fact]
    public void ContactEvents_SettleBeforeTheSceneLateStepRuns()
    {
        Body body = new(new Vector2(4f, 8f));
        body.Collider.Detects("solid");
        body.Collider.ReportsContacts = true;

        List<string> log = [];
        body.Collider.ContactEntered += _ => log.Add("enter");

        SceneFixtures.HookScene scene = new(
            step: (Scene _, in StepContext _) => log.Add("step"),
            lateStep: (Scene _, in StepContext _) => log.Add("late"));
        scene.Add(new TileMap(SceneFixtures.TerrainGrid("....", "####")));
        scene.Add(body);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(["step", "enter", "late"], log);
    }

    [Fact]
    public void ContactEvents_AreNotRaisedUntilAColliderOptsIn()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Body body = new(new Vector2(4f, 8f));
        body.Collider.Detects("solid");

        int entered = 0;
        body.Collider.ContactEntered += _ => entered++;

        scene.Add(body);
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(0, entered);
        Assert.Empty(body.Collider.Touching.ToArray());
    }

    [Fact]
    public void AColliderLeavingItsScene_ReportsThatItIsTouchingNothing()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Body body = new(new Vector2(4f, 8f));
        body.Collider.Detects("solid");
        body.Collider.ReportsContacts = true;

        List<string> log = [];
        body.Collider.ContactExited += contact => log.Add($"-{contact.LayerName}");

        scene.Add(body);
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Single(body.Collider.Touching.ToArray());

        scene.Remove(body);

        Assert.Equal(["-solid"], log);
    }

    // A collider keeps layer names, not bits, so the scene it lands in is the one it filters against.
    [Fact]
    public void AColliderCarriedToAnotherScene_RebuildsItsFilterAgainstTheNewWorld()
    {
        Scene first = SceneFixtures.Terrain("....", "####");
        Body body = new(new Vector2(4f, 8f));
        body.Collider.Detects("solid");
        body.Collider.ReportsContacts = true;
        first.Add(body);

        CollisionFilter inFirst = body.Collider.Filter;
        first.Remove(body);

        Assert.Equal(CollisionFilter.None, body.Collider.Filter);

        // Names interned ahead of 'solid' land it on a different bit, so a filter carried over
        // would match some other tile type rather than simply matching nothing.
        Scene second = new();
        second.Collision.Filter("hazard", "water", "ladder");
        second.Add(new TileMap(SceneFixtures.TerrainGrid("....", "####")));
        second.Add(body);

        Assert.NotEqual(first.Collision.Layer("solid").Index, second.Collision.Layer("solid").Index);
        Assert.NotEqual(inFirst, body.Collider.Filter);
        Assert.Throws<ArgumentException>(
            () => second.Collision.OverlapBox(body.Collider.Bounds, inFirst, default));

        using SceneSimulation simulation = new(second);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(["solid"], body.Collider.Touching.ToArray().Select(contact => contact.LayerName));
    }

    [Fact]
    public void MultipleContacts_AreAllDeliveredWhenNoHandlerTearsAnythingDown()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Straddler body = new(new Vector2(0f, 8f));
        scene.Add(body);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(["+(0,1)", "+(1,1)", "+(2,1)"], body.Log);
        Assert.Equal(3, body.Collider.Touching.Length);
    }

    // What a handler is being told about must not change underneath it, so the setters that would
    // change it refuse for as long as the dispatch runs.
    [Fact]
    public void AContactHandlerThatReconfiguresItsOwnCollider_IsRefused()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Straddler body = new(new Vector2(0f, 8f));
        scene.Add(body);
        body.Collider.ContactEntered += _ => body.Collider.Enabled = false;

        using SceneSimulation simulation = new(scene);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => simulation.Step(SceneFixtures.Step(0)));

        Assert.Contains("dispatched", refused.Message, StringComparison.Ordinal);
        Assert.True(body.Collider.Enabled);
    }

    [Fact]
    public void AContactHandlerThatRemovesTheCollider_IsRefused()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Straddler body = new(new Vector2(0f, 8f));
        scene.Add(body);
        body.Collider.ContactEntered += _ => body.Remove(body.Collider);

        using SceneSimulation simulation = new(scene);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => simulation.Step(SceneFixtures.Step(0)));
    }

    // Removing the entity is deferred to the end of the step, so it does not cut the dispatch
    // short: the whole set is delivered, then the detach ends all of it.
    [Fact]
    public void AContactHandlerThatRemovesTheEntity_FinishesTheStepThenExitsEverything()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Straddler body = new(new Vector2(0f, 8f));
        scene.Add(body);
        body.Collider.ContactEntered += _ => scene.Remove(body);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(["+(0,1)", "+(1,1)", "+(2,1)", "-(0,1)", "-(1,1)", "-(2,1)"], body.Log);
        Assert.Null(body.Collider.World);
        Assert.Empty(body.Collider.Touching.ToArray());
    }

    // A rejected set must leave the component, its entity and the world identical — not commit the
    // field and then fail on the way to the broadphase.
    [Fact]
    public void ARejectedSizeOrOffsetSet_LeavesTheColliderAndItsProxyExactlyAsTheyWere()
    {
        Scene scene = new();
        Body body = new(new Vector2(10f, 10f));
        scene.Add(body);

        Shape2D original = body.Collider.Shape;
        Aabb2D bounds = body.Collider.Bounds;

        Assert.Throws<ArgumentException>(() => body.Collider.Size = new Vector2(0f, 8f));
        Assert.Throws<ArgumentOutOfRangeException>(() => body.Collider.Size = new Vector2(-8f, 8f));
        Assert.Throws<ArgumentOutOfRangeException>(() => body.Collider.Offset = new Vector2(float.NaN, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => body.Collider.Offset = new Vector2(0f, float.PositiveInfinity));

        Assert.Equal(new Vector2(8f, 8f), body.Collider.Size);
        Assert.Equal(original, body.Collider.Shape);
        Assert.Equal(Vector2.Zero, body.Collider.Offset);
        Assert.Equal(bounds, body.Collider.Bounds);
        Assert.Equal(original, scene.Collision.ShapeOf(body.Collider.Handle));
        Assert.Equal(new Vector2(10f, 10f), scene.Collision.PositionOf(body.Collider.Handle));

        // Still exactly where the proxy said it was.
        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Assert.Equal(1, scene.Collision.OverlapBox(bounds, CollisionFilter.Everything, contacts));
    }

    // The typed setters are the only way a shape changes now, and a registered collider has to be
    // queried as its new shape from the moment one returns.
    [Fact]
    public void ATypedSetterOnARegisteredCollider_ResyncsTheShapeTheWorldQueriesBy()
    {
        Scene scene = new();
        Body body = new(new Vector2(100f, 100f));
        scene.Add(body);

        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Aabb2D reach = Aabb2D.FromCorner(new Vector2(120f, 100f), new Vector2(8f, 8f));
        Assert.Equal(0, scene.Collision.OverlapBox(reach, CollisionFilter.Everything, contacts));

        body.Collider.Size = new Vector2(64f, 8f);

        Assert.Equal(new Vector2(64f, 8f), body.Collider.Size);
        Assert.Equal(1, scene.Collision.OverlapBox(reach, CollisionFilter.Everything, contacts));
        Assert.Equal(body.Collider.Handle, contacts[0].Target.Collider);
    }

    // The offset half of the check is world-independent, so it fires where the mistake was made
    // rather than surfacing later as a failure to join a scene.
    [Fact]
    public void AnUnplaceableOffsetOnADetachedCollider_IsRefusedAtSetTimeNotAtAttachTime()
    {
        BoxCollider2D collider = new(new Vector2(8f, 8f));

        Assert.Throws<ArgumentOutOfRangeException>(() => collider.Offset = new Vector2(float.PositiveInfinity, 0f));
        Assert.Equal(Vector2.Zero, collider.Offset);

        // Finite, and still no shape: the offset carries this one's bounds off the end of the range.
        CircleCollider2D wide = new(8e37f);
        Assert.Throws<ArgumentException>(() => wide.Offset = new Vector2(3e38f, 0f));
        Assert.Equal(Vector2.Zero, wide.Offset);

        Scene scene = new();
        SceneFixtures.Drifter drifter = new(Vector2.Zero);
        scene.Add(drifter);
        drifter.Add(collider);

        Assert.Same(scene.Collision, collider.World);
    }

    [Fact]
    public void ARejectedDetectsCall_LeavesTheColliderFilteringAsItDid()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Body body = new(new Vector2(4f, 8f));
        body.Collider.Detects("solid");
        body.Mover.BlocksOn("solid");
        scene.Add(body);

        CollisionFilter before = body.Collider.Filter;

        // The bad name is second, so a call that committed as it went would already have thrown the
        // old list away and kept the first.
        Assert.Throws<ArgumentException>(() => body.Collider.Detects("wall", " "));

        Assert.Equal(before, body.Collider.Filter);
        Assert.Equal(before, scene.Collision.FilterOf(body.Collider.Handle));

        // The stored names are what the next scene rebuilds the filter from, which is the only
        // place a half-applied list would ever show itself.
        scene.Remove(body);
        Scene second = SceneFixtures.Terrain("....", "####");
        second.Add(body);

        Assert.True(body.Collider.Filter.Matches(second.Collision.Layer("solid")));
        Assert.True(body.Mover.Move(new Vector2(0f, 60f)).BlockedY);
    }

    // Layer names are interned where they are first needed, and the world's table is the whole of
    // the contract: the sixty-fifth name has nowhere to go, whoever asks for it.
    [Fact]
    public void AColliderNeedingALayerTheWorldHasNoRoomFor_IsRefusedWhereTheNameIsInterned()
    {
        Scene scene = new();
        Body host = new(Vector2.Zero);
        scene.Add(host);
        Saturate(scene.Collision);

        BoxCollider2D late = new(new Vector2(8f, 8f));
        late.Detects("a name this world has never seen");

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => host.Add(late));

        Assert.Contains($"{CollisionWorld2D.MaxLayers} layers", refused.Message, StringComparison.Ordinal);
        Assert.Null(late.World);
        Assert.True(late.Handle.IsNone);
        Assert.Equal(CollisionWorld2D.MaxLayers, scene.Collision.LayerCount);
    }

    [Fact]
    public void AColliderTakingTheLastLayerTheWorldHasRoomFor_Registers()
    {
        Scene scene = new();
        Body host = new(Vector2.Zero);
        scene.Add(host);

        // One short of the cap, so the collider's own layer is the last name that fits.
        Saturate(scene.Collision, spare: 1);

        BoxCollider2D last = new(new Vector2(8f, 8f)) { Layer = "the last one that fits" };
        host.Add(last);

        Assert.Same(scene.Collision, last.World);
        Assert.True(scene.Collision.Contains(last.Handle));
        Assert.Equal(CollisionWorld2D.MaxLayers, scene.Collision.LayerCount);
    }

    private static void Saturate(CollisionWorld2D world, int spare = 0)
    {
        for (int index = world.LayerCount; index < CollisionWorld2D.MaxLayers - spare; index++)
        {
            world.Layer($"filler-{index}");
        }
    }

    // Every typed collider validates through the shape factory it builds with, at construction and
    // at every set, so a shape the queries would refuse never reaches one.
    [Fact]
    public void ATypedCollider_RefusesAShapeItsFactoryWould()
    {
        Assert.Throws<ArgumentException>(() => new BoxCollider2D(new Vector2(0f, 8f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircleCollider2D(0f));
        Assert.Throws<ArgumentException>(() => new CapsuleCollider2D(Vector2.Zero, Vector2.Zero, 4f));
        Assert.Throws<ArgumentException>(() => new PolygonCollider2D([Vector2.Zero, Vector2.UnitX]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircleCollider2D(4f) { Radius = float.NaN });
    }

    // Sixteen and thirty-two are buffer sizes, not contact limits: a collider spanning a long floor
    // must enter every cell it stands on, and Move must name every one it landed against.
    [Fact]
    public void AColliderTouchingMoreThingsThanItsBufferHolds_ReportsEveryOneOfThem()
    {
        Scene scene = SceneFixtures.Terrain(new string('.', 48), new string('#', 48));
        Wide body = new(Vector2.Zero);
        scene.Add(body);

        using SceneSimulation simulation = new(scene);
        body.Mover.Move(new Vector2(0f, 20f));

        ColliderContact2D[] landed = body.Mover.MoveContacts.ToArray();
        Assert.True(landed.Length >= 40, $"the move landed on {landed.Length} cells, which does not exercise a full buffer.");
        Assert.Equal(landed.Length, landed.Select(contact => contact.Cell!.Value.X).Distinct().Count());
        Assert.Equal(8f, body.Position.Y, 2f * CollisionWorld2D.LinearSlop);

        simulation.Step(SceneFixtures.Step(0));

        Assert.True(
            body.Collider.Touching.Length >= 40,
            $"the collider settled on {body.Collider.Touching.Length} cells, which does not exercise a full buffer.");
        Assert.Equal(body.Collider.Touching.Length, body.Entered);
    }

    [Fact]
    public void TwoCollidersReachEachOtherAndTheEntityBehindTheContact()
    {
        Scene scene = new();
        Body first = new(Vector2.Zero) { Position = Vector2.Zero };
        Body second = new(new Vector2(4f, 0f));
        first.Collider.Layer = "one";
        second.Collider.Layer = "two";
        first.Collider.Detects("two");
        first.Collider.ReportsContacts = true;

        Body? touched = null;
        first.Collider.ContactEntered += contact => touched = contact.OtherCollider?.Entity as Body;

        scene.Add(first);
        scene.Add(second);
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Same(second, touched);
    }

    // A collider is on the default layer until it is told otherwise, which is what makes things
    // collide out of the box.
    [Fact]
    public void ACollider_StartsOnTheDefaultLayer()
    {
        Scene scene = new();
        Body body = new(Vector2.Zero);

        Assert.Equal(CollisionWorld2D.DefaultLayerName, body.Collider.Layer);

        scene.Add(body);

        Assert.Equal(
            scene.Collision.Layer(CollisionWorld2D.DefaultLayerName),
            scene.Collision.LayerOf(body.Collider.Handle));
    }

    // Setting the layer of a registered collider has to reach the world at once: a query on the very
    // next line filters by what it is on now, not by what it was on.
    [Fact]
    public void SettingTheLayerOfARegisteredCollider_ReFiltersImmediately()
    {
        Scene scene = new();
        Body body = new(Vector2.Zero);
        scene.Add(body);

        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Aabb2D probe = Aabb2D.FromCorner(Vector2.Zero, new Vector2(8f, 8f));

        Assert.Equal(0, scene.Collision.OverlapBox(probe, scene.Collision.Filter("hazard"), contacts));

        body.Collider.Layer = "hazard";

        Assert.Equal("hazard", body.Collider.Layer);
        Assert.Equal(scene.Collision.Layer("hazard"), scene.Collision.LayerOf(body.Collider.Handle));
        Assert.Equal(1, scene.Collision.OverlapBox(probe, scene.Collision.Filter("hazard"), contacts));
    }

    // A contact carries the touched thing's layer as an index, and its name for a log line.
    [Fact]
    public void AContact_ReportsTheTouchedThingsLayerAndItsName()
    {
        Scene scene = new();
        Body player = new(Vector2.Zero);
        Body enemy = new(new Vector2(4f, 0f));
        player.Collider.Detects("enemy");
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

    // The per-move filter is for the step, not for the mover: what it blocks on afterwards is
    // whatever BlocksOn last said.
    [Fact]
    public void Move_WithABlockingFilter_HonoursItOverBlocksOnAndLeavesTheStandingFilterAlone()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        Body body = new(new Vector2(8f, 8f));
        body.Mover.BlocksOn("solid");
        scene.Add(body);

        CollisionFilter standing = body.Mover.Filter;

        // Blocking on nothing for this call alone: the floor the mover normally stops on is not
        // there as far as this move is concerned.
        MoveResult2D through = body.Mover.Move(new Vector2(0f, 60f), CollisionFilter.None);

        Assert.False(through.BlockedY);
        Assert.Equal(68f, body.Position.Y, 2f * CollisionWorld2D.LinearSlop);
        Assert.Equal(standing, body.Mover.Filter);

        // And the next plain move resolves against the standing filter again.
        body.Teleport(new Vector2(8f, 8f));
        Assert.True(body.Mover.Move(new Vector2(0f, 60f)).BlockedY);
    }

    [Fact]
    public void Move_WithABlockingFilterFromAnotherWorld_IsRefused()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Body body = new(new Vector2(8f, 8f));
        scene.Add(body);

        CollisionFilter foreign = new Scene().Collision.Filter("solid");

        Assert.Throws<ArgumentException>(() => body.Mover.Move(Vector2.UnitY, foreign));
    }

    // A face is a surface only from the side it faces, and this is where a game reads that: a body
    // rising through a ledge is not standing on it on the way up, and is the moment it settles on
    // top. An enter while passing would fire a landing in mid-air.
    [Fact]
    public void AColliderRisingThroughATopFaceCell_EntersNoContactUntilItRestsOnTop()
    {
        Scene scene = Ledge();
        Body body = new(new Vector2(20f, 16f + (0.5f * CollisionWorld2D.ContactSkin)));
        body.Collider.Detects("platform");
        body.Collider.ReportsContacts = true;
        body.Mover.BlocksOn("platform");

        List<ColliderContact2D> entered = [];
        body.Collider.ContactEntered += entered.Add;
        scene.Add(body);

        using SceneSimulation simulation = new(scene);

        // Just under the face, inside the contact skin and on the far side of it: nothing touched.
        simulation.Step(SceneFixtures.Step(0));
        Assert.Empty(entered);

        // Rising through it is not blocked, and meets nothing on the way.
        Assert.False(body.Mover.Move(new Vector2(0f, -20f)).BlockedY);
        simulation.Step(SceneFixtures.Step(1));
        Assert.Empty(entered);

        // Falling back onto it lands, and the contact carries the face's own normal.
        Assert.True(body.Mover.Move(new Vector2(0f, 20f)).BlockedY);
        simulation.Step(SceneFixtures.Step(2));

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
            [TileGrid.EmptyTile, new TileDefinition("ledge", null, "platform", CellFaces2D.Top)],
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
        Assert.Equal(CellFaces2D.All, map.Collision.FacesAt(0, 1));
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

    private sealed class Body : Entity
    {
        internal Body(Vector2 position)
            : base(position)
        {
            Collider = new BoxCollider2D(new Vector2(8f, 8f));
            Add(Collider);
            Mover = new KinematicBody2D(Collider);
            Add(Mover);
        }

        internal BoxCollider2D Collider { get; }

        internal KinematicBody2D Mover { get; }
    }

    /// <summary>A body resting across three floor cells, so a dispatch cut short is visible.</summary>
    private sealed class Straddler : Entity
    {
        internal Straddler(Vector2 position)
            : base(position)
        {
            Collider = new BoxCollider2D(new Vector2(40f, 8f)) { ReportsContacts = true };
            Collider.Detects("solid");
            Collider.ContactEntered += contact => Log.Add($"+({contact.Cell!.Value.X},{contact.Cell.Value.Y})");
            Collider.ContactExited += contact => Log.Add($"-({contact.Cell!.Value.X},{contact.Cell.Value.Y})");
            Add(Collider);
        }

        internal BoxCollider2D Collider { get; }

        internal List<string> Log { get; } = [];
    }

    /// <summary>A body long enough to touch far more cells than a contact buffer starts out holding.</summary>
    private sealed class Wide : Entity
    {
        internal Wide(Vector2 position)
            : base(position)
        {
            Collider = new BoxCollider2D(new Vector2(45f * 16f, 8f)) { ReportsContacts = true };
            Collider.Detects("solid");
            Collider.ContactEntered += _ => Entered++;
            Add(Collider);
            Mover = new KinematicBody2D(Collider);
            Mover.BlocksOn("solid");
            Add(Mover);
        }

        internal BoxCollider2D Collider { get; }

        internal KinematicBody2D Mover { get; }

        internal int Entered { get; private set; }
    }
}
