using System.Numerics;
using Capsule.Collision;
using Capsule.Scenes;
using Capsule.Scenes.Components;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Scenes;

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

        Collider collider = new(new Vector2(8f, 8f));
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
        Scene scene = Terrain("....", "....", "####");
        Body body = new(new Vector2(8f, 8f));
        body.Mover.BlocksOn("solid");
        scene.Add(body);

        MoveResult result = body.Mover.Move(new Vector2(0f, 60f));

        Assert.True(result.BlockedY);
        Assert.Equal(24f, body.Position.Y, 2f * CollisionWorld.LinearSlop);
        Assert.NotEmpty(body.Mover.MoveContacts.ToArray());
        Assert.All(
            body.Mover.MoveContacts.ToArray(),
            contact =>
            {
                Assert.True(contact.Cell.HasValue);
                Assert.Equal("solid", contact.Tag);
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
    public void ADetectedContact_DoesNotBlockAMoverThatDoesNotBlockOnItsTag()
    {
        Scene scene = new();
        Body player = new(Vector2.Zero);
        Body enemy = new(new Vector2(10f, 0f));
        player.Collider.Tag = "player";
        player.Collider.Detects("enemy");
        player.Collider.ReportsContacts = true;
        player.Mover.BlocksOn("solid");
        enemy.Collider.Tag = "enemy";

        List<ColliderContact> entered = [];
        player.Collider.ContactEntered += entered.Add;

        scene.Add(player);
        scene.Add(enemy);
        using SceneSimulation simulation = new(scene);

        MoveResult result = player.Mover.Move(new Vector2(12f, 0f));
        simulation.Step(SceneFixtures.Step(0));

        Assert.False(result.BlockedX);
        Assert.Equal(new Vector2(12f, 0f), player.Position);
        Assert.Empty(player.Mover.MoveContacts.ToArray());

        ColliderContact contact = Assert.Single(entered);
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
        second.Collider.Tag = "other";

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
        Scene scene = Terrain("....", "....", "####");
        Body body = new(new Vector2(4f, 8f));
        body.Collider.Detects("solid");
        body.Mover.BlocksOn("solid");
        body.Collider.ReportsContacts = true;

        List<string> log = [];
        body.Collider.ContactEntered += contact => log.Add($"+{contact.Tag}({contact.Cell!.Value.X},{contact.Cell.Value.Y})");
        body.Collider.ContactExited += contact => log.Add($"-{contact.Tag}({contact.Cell!.Value.X},{contact.Cell.Value.Y})");

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
        scene.Add(new TileMap(Grid("....", "####")));
        scene.Add(body);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(["step", "enter", "late"], log);
    }

    [Fact]
    public void ContactEvents_AreNotRaisedUntilAColliderOptsIn()
    {
        Scene scene = Terrain("....", "####");
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
        Scene scene = Terrain("....", "####");
        Body body = new(new Vector2(4f, 8f));
        body.Collider.Detects("solid");
        body.Collider.ReportsContacts = true;

        List<string> log = [];
        body.Collider.ContactExited += contact => log.Add($"-{contact.Tag}");

        scene.Add(body);
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Single(body.Collider.Touching.ToArray());

        scene.Remove(body);

        Assert.Equal(["-solid"], log);
    }

    // A collider keeps tag names, not bits, so the scene it lands in is the one it filters against.
    [Fact]
    public void AColliderCarriedToAnotherScene_RebuildsItsFilterAgainstTheNewWorld()
    {
        Scene first = Terrain("....", "####");
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
        second.Add(new TileMap(Grid("....", "####")));
        second.Add(body);

        Assert.NotEqual(first.Collision.Tag("solid").Index, second.Collision.Tag("solid").Index);
        Assert.NotEqual(inFirst, body.Collider.Filter);
        Assert.Throws<ArgumentException>(
            () => second.Collision.OverlapBox(body.Collider.Bounds, inFirst, default));

        using SceneSimulation simulation = new(second);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(["solid"], body.Collider.Touching.ToArray().Select(contact => contact.Tag));
    }

    [Fact]
    public void MultipleContacts_AreAllDeliveredWhenNoHandlerTearsAnythingDown()
    {
        Scene scene = Terrain("....", "####");
        Straddler body = new(new Vector2(0f, 8f));
        scene.Add(body);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(["+(0,1)", "+(1,1)", "+(2,1)"], body.Log);
        Assert.Equal(3, body.Collider.Touching.Length);
    }

    // A handler is allowed to tear its own collider down. What it must never see is the rest of a
    // set that no longer describes anything, and an exit for a contact it was never told began.
    [Fact]
    public void AContactHandlerThatDetachesTheCollider_EndsThatStepsEventsThere()
    {
        Scene scene = Terrain("....", "####");
        Straddler body = new(new Vector2(0f, 8f));
        scene.Add(body);
        body.Collider.ContactEntered += _ => body.Remove(body.Collider);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(["+(0,1)", "-(0,1)"], body.Log);
        Assert.Null(body.Collider.World);
        Assert.True(body.Collider.Handle.IsNone);
        Assert.Empty(body.Collider.Touching.ToArray());
        Assert.Equal(0, scene.Collision.ColliderCount - scene.Collision.Grids.Length);
    }

    [Fact]
    public void AContactHandlerThatStopsReporting_EndsThatStepsEventsThere()
    {
        Scene scene = Terrain("....", "####");
        Straddler body = new(new Vector2(0f, 8f));
        scene.Add(body);
        body.Collider.ContactEntered += _ => body.Collider.ReportsContacts = false;

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        // Reporting stopped, so nothing is owed — not even an exit for what it was standing on.
        Assert.Equal(["+(0,1)"], body.Log);
        Assert.NotNull(body.Collider.World);
        Assert.Empty(body.Collider.Touching.ToArray());

        // And a later step raises nothing while it stays off.
        simulation.Step(SceneFixtures.Step(1));
        Assert.Equal(["+(0,1)"], body.Log);
    }

    // Standing the collider back up mid-dispatch leaves it live again, but registered afresh. The
    // interrupted loop belongs to the registration that ended and must not resume into the new one.
    [Fact]
    public void AContactHandlerThatReAttachesTheCollider_NeverResumesTheDepartedRegistration()
    {
        Scene scene = Terrain(new string('.', 16), new string('#', 16));
        Straddler body = new(new Vector2(0f, 8f));
        // Off the cell boundary, so the new set is three cells the way the old one was.
        Body host = new(new Vector2(164f, 8f));
        scene.Add(body);
        scene.Add(host);

        bool moved = false;
        body.Collider.ContactEntered += _ =>
        {
            if (moved)
            {
                return;
            }

            moved = true;
            body.Remove(body.Collider);
            host.Add(body.Collider);
        };

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        // Cells 1 and 2 belonged to the registration that ended, and are never announced.
        Assert.Equal(["+(0,1)", "-(0,1)"], body.Log);

        // The new one settles on its own next step, over the cells it actually stands on.
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(["+(0,1)", "-(0,1)", "+(10,1)", "+(11,1)", "+(12,1)"], body.Log);
        Assert.Equal(3, body.Collider.Touching.Length);
        Assert.All(body.Collider.Touching.ToArray(), contact => Assert.InRange(contact.Cell!.Value.X, 10, 12));
    }

    [Fact]
    public void AContactHandlerThatTogglesReportingOffAndOn_NeverResumesTheDepartedRegistration()
    {
        Scene scene = Terrain("....", "####");
        Straddler body = new(new Vector2(0f, 8f));
        scene.Add(body);

        bool toggled = false;
        body.Collider.ContactEntered += _ =>
        {
            if (toggled)
            {
                return;
            }

            toggled = true;
            body.Collider.ReportsContacts = false;
            body.Collider.ReportsContacts = true;
        };

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        // The registration that ended announced one contact and no more, though reporting was live
        // again by the time the loop looked.
        Assert.Equal(["+(0,1)"], body.Log);

        simulation.Step(SceneFixtures.Step(1));

        // The fresh one starts from nothing, so all three are new to it.
        Assert.Equal(["+(0,1)", "+(0,1)", "+(1,1)", "+(2,1)"], body.Log);
        Assert.Equal(3, body.Collider.Touching.Length);
    }

    // Removing the entity is deferred to the end of the step, so unlike detaching the component it
    // does not cut the dispatch short: the whole set is delivered, then the detach ends all of it.
    [Fact]
    public void AContactHandlerThatRemovesTheEntity_FinishesTheStepThenExitsEverything()
    {
        Scene scene = Terrain("....", "####");
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
    public void ARejectedShapeOrOffsetSet_LeavesTheColliderAndItsProxyExactlyAsTheyWere()
    {
        Scene scene = new();
        Body body = new(new Vector2(10f, 10f));
        scene.Add(body);

        Shape original = body.Collider.Shape;
        Aabb bounds = body.Collider.Bounds;

        Assert.Throws<ArgumentException>(() => body.Collider.Shape = default);
        Assert.Throws<ArgumentOutOfRangeException>(() => body.Collider.Offset = new Vector2(float.NaN, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => body.Collider.Offset = new Vector2(0f, float.PositiveInfinity));

        Assert.Equal(original, body.Collider.Shape);
        Assert.Equal(Vector2.Zero, body.Collider.Offset);
        Assert.Equal(bounds, body.Collider.Bounds);
        Assert.Equal(original, scene.Collision.ShapeOf(body.Collider.Handle));
        Assert.Equal(new Vector2(10f, 10f), scene.Collision.PositionOf(body.Collider.Handle));

        // Still exactly where the proxy said it was.
        Span<Contact> contacts = stackalloc Contact[4];
        Assert.Equal(1, scene.Collision.OverlapBox(bounds, CollisionFilter.Everything, contacts));
    }

    // The offset half of the check is world-independent, so it fires where the mistake was made
    // rather than surfacing later as a failure to join a scene.
    [Fact]
    public void AnUnplaceableOffsetOnADetachedCollider_IsRefusedAtSetTimeNotAtAttachTime()
    {
        Collider collider = new(new Vector2(8f, 8f));

        Assert.Throws<ArgumentOutOfRangeException>(() => collider.Offset = new Vector2(float.PositiveInfinity, 0f));
        Assert.Equal(Vector2.Zero, collider.Offset);

        // Finite, and still no shape: the offset carries this one's bounds off the end of the range.
        Collider wide = new(Shape.Circle(Vector2.Zero, 8e37f));
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
        Scene scene = Terrain("....", "####");
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
        Scene second = Terrain("....", "####");
        second.Add(body);

        Assert.True(body.Collider.Filter.Matches(second.Collision.Tag("solid")));
        Assert.True(body.Mover.Move(new Vector2(0f, 60f)).BlockedY);
    }

    // Registration needs room in the world's tag table, and finding that out halfway through would
    // leave a collider holding a world it never got a handle from.
    [Fact]
    public void AColliderNeedingATagTheWorldHasNoRoomFor_IsRefusedAtAttachWithNothingCommitted()
    {
        Scene scene = new();
        Body host = new(Vector2.Zero);
        scene.Add(host);
        Saturate(scene.Collision);

        int tags = scene.Collision.TagCount;
        int colliders = scene.Collision.ColliderCount;

        Collider late = new(new Vector2(8f, 8f));
        late.Detects("a name this world has never seen");

        Assert.Throws<InvalidOperationException>(() => host.Add(late));

        Assert.Null(late.Entity);
        Assert.Null(late.World);
        Assert.True(late.Handle.IsNone);
        Assert.Equal(tags, scene.Collision.TagCount);
        Assert.Equal(colliders, scene.Collision.ColliderCount);
    }

    [Fact]
    public void AnEntityWhoseColliderCannotRegister_StaysOutOfTheSceneWithNoSiblingRegistered()
    {
        Scene scene = new();
        Saturate(scene.Collision);

        int colliders = scene.Collision.ColliderCount;

        Entity carrier = new Body(Vector2.Zero);
        Collider first = new(new Vector2(8f, 8f));
        Collider second = new(new Vector2(8f, 8f));
        second.Detects("a name this world has never seen");
        carrier.Add(first);
        carrier.Add(second);

        Assert.Throws<InvalidOperationException>(() => scene.Add(carrier));

        Assert.Null(carrier.Scene);
        Assert.Null(first.World);
        Assert.True(first.Handle.IsNone);
        Assert.Null(second.World);
        Assert.Equal(colliders, scene.Collision.ColliderCount);
    }

    // Asked one at a time, two siblings each wanting the last free slot are both told yes and the
    // second finds out at commit — after its entity is published and its sibling registered.
    [Fact]
    public void TwoSiblingCollidersWantingTheLastTagBetweenThem_AreRefusedTogether()
    {
        Scene scene = new();
        Saturate(scene.Collision, spare: 1);

        int tags = scene.Collision.TagCount;
        int colliders = scene.Collision.ColliderCount;

        Entity carrier = new Body(Vector2.Zero);
        carrier.Add(Named("first new name"));
        carrier.Add(Named("second new name"));

        Assert.Throws<InvalidOperationException>(() => scene.Add(carrier));

        Assert.Null(carrier.Scene);
        Assert.Equal(tags, scene.Collision.TagCount);
        Assert.Equal(colliders, scene.Collision.ColliderCount);
    }

    // The same two wanting the same name need one slot between them, not two.
    [Fact]
    public void TwoSiblingCollidersWantingTheSameNewTag_CountItOnceAndBothRegister()
    {
        Scene scene = new();
        Saturate(scene.Collision, spare: 1);

        Entity carrier = new Body(Vector2.Zero);
        Collider first = Named("the one name they share");
        Collider second = Named("the one name they share");
        carrier.Add(first);
        carrier.Add(second);

        scene.Add(carrier);

        Assert.Same(scene, carrier.Scene);
        Assert.True(scene.Collision.Contains(first.Handle));
        Assert.True(scene.Collision.Contains(second.Handle));
        Assert.Equal(CollisionWorld.MaxTags, scene.Collision.TagCount);
    }

    [Fact]
    public void TwoSiblingCollidersWithRoomForBothTheirNames_Register()
    {
        Scene scene = new();
        Saturate(scene.Collision, spare: 2);

        Entity carrier = new Body(Vector2.Zero);
        Collider first = Named("first new name");
        Collider second = Named("second new name");
        carrier.Add(first);
        carrier.Add(second);

        scene.Add(carrier);

        Assert.True(scene.Collision.Contains(first.Handle));
        Assert.True(scene.Collision.Contains(second.Handle));
        Assert.Equal(CollisionWorld.MaxTags, scene.Collision.TagCount);
    }

    // Entry hooks run in attachment order, and one ahead of a preflighted collider may legitimately
    // want a tag of its own. Counting capacity without claiming it would let that hook take the slot
    // the collider was promised, and the collider would fail after the entity was published.
    [Fact]
    public void AnEarlierHookInterningATag_CannotTakeTheSlotALaterColliderWasPromised()
    {
        Scene scene = new();
        Saturate(scene.Collision, spare: 1);

        Greedy greedy = new();
        Collider late = Named("the name that was reserved");
        Entity carrier = new Body(Vector2.Zero);
        carrier.Add(greedy);
        carrier.Add(late);

        scene.Add(carrier);

        // The collider registered on the name reserved for it; the hook's opportunistic one is what
        // found the table full.
        Assert.Same(scene, carrier.Scene);
        Assert.True(scene.Collision.Contains(late.Handle));
        Assert.True(late.Filter.Matches(scene.Collision.Tag("the name that was reserved")));
        Assert.NotNull(greedy.Refused);
        Assert.Equal(CollisionWorld.MaxTags, scene.Collision.TagCount);
    }

    // The other way a hook can spend capacity: attaching a collider of its own mid-entry. That goes
    // through Add, which preflights against what is actually left, so it is refused on its own and
    // the admission around it still completes.
    [Fact]
    public void AnEarlierHookAttachingACollider_IsRefusedAtomicallyWithoutBreakingTheAdmission()
    {
        Scene scene = new();
        Saturate(scene.Collision, spare: 1);

        Collider late = Named("the name that was reserved");
        Grasping grasping = new();
        Entity carrier = new Body(Vector2.Zero);
        carrier.Add(grasping);
        carrier.Add(late);

        scene.Add(carrier);

        Assert.Same(scene, carrier.Scene);
        Assert.True(scene.Collision.Contains(late.Handle));
        Assert.NotNull(grasping.Refused);
        Assert.Null(grasping.Rejected.Entity);
        Assert.Null(grasping.Rejected.World);
        Assert.Equal(CollisionWorld.MaxTags, scene.Collision.TagCount);
    }

    private sealed class Greedy : Component
    {
        internal Exception? Refused { get; private set; }

        protected override void OnAddedToScene()
        {
            try
            {
                Entity!.Scene!.Collision.Tag("a name nobody reserved");
            }
            catch (InvalidOperationException refused)
            {
                Refused = refused;
            }
        }
    }

    private sealed class Grasping : Component
    {
        internal Collider Rejected { get; } = Named("another name nobody reserved");

        internal Exception? Refused { get; private set; }

        protected override void OnAddedToScene()
        {
            try
            {
                Entity!.Add(Rejected);
            }
            catch (InvalidOperationException refused)
            {
                Refused = refused;
            }
        }
    }

    private static Collider Named(string detects)
    {
        Collider collider = new(new Vector2(8f, 8f));
        collider.Detects(detects);

        return collider;
    }

    // The preflight is a promise, not a guess: a collider it passes registers.
    [Fact]
    public void AColliderTakingTheLastTagTheWorldHasRoomFor_PassesPreflightAndRegisters()
    {
        Scene scene = new();
        Body host = new(Vector2.Zero);
        scene.Add(host);

        // One short of the cap, so the collider's own tag is the last name that fits.
        Saturate(scene.Collision, spare: 1);

        Collider last = new(new Vector2(8f, 8f)) { Tag = "the last one that fits" };
        host.Add(last);

        Assert.Same(scene.Collision, last.World);
        Assert.True(scene.Collision.Contains(last.Handle));
        Assert.Equal(CollisionWorld.MaxTags, scene.Collision.TagCount);
    }

    private static void Saturate(CollisionWorld world, int spare = 0)
    {
        for (int index = world.TagCount; index < CollisionWorld.MaxTags - spare; index++)
        {
            world.Tag($"filler-{index}");
        }
    }

    [Fact]
    public void ACollider_RefusesADefaultShapeWhereItIsGivenOne()
    {
        Assert.Throws<ArgumentException>(() => new Collider(default(Shape)));
        Assert.Throws<ArgumentException>(() => new Collider(new Vector2(8f, 8f)).Shape = default);
    }

    // Sixteen and thirty-two are buffer sizes, not contact limits: a collider spanning a long floor
    // must enter every cell it stands on, and Move must name every one it landed against.
    [Fact]
    public void AColliderTouchingMoreThingsThanItsBufferHolds_ReportsEveryOneOfThem()
    {
        Scene scene = Terrain(new string('.', 48), new string('#', 48));
        Wide body = new(Vector2.Zero);
        scene.Add(body);

        using SceneSimulation simulation = new(scene);
        body.Mover.Move(new Vector2(0f, 20f));

        ColliderContact[] landed = body.Mover.MoveContacts.ToArray();
        Assert.True(landed.Length >= 40, $"the move landed on {landed.Length} cells, which does not exercise a full buffer.");
        Assert.Equal(landed.Length, landed.Select(contact => contact.Cell!.Value.X).Distinct().Count());
        Assert.Equal(8f, body.Position.Y, 2f * CollisionWorld.LinearSlop);

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
        first.Collider.Tag = "one";
        second.Collider.Tag = "two";
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

    [Fact]
    public void ATileMapRegistersOneColliderCarryingTheTileTypeAsItsTag()
    {
        Scene scene = Terrain("....", "####");
        TileMap map = scene.FindSingle<TileMap>();

        Assert.NotNull(map.Collision);
        Assert.Equal(4, map.Collision.Width);
        Assert.Equal(CellCollision.Solid, map.Collision.CollisionAt(0, 1));
        Assert.Equal("solid", scene.Collision.NameOf(map.Collision.TagAt(0, 1)));

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

    private static Scene Terrain(params string[] rows) =>
        new Scene(SceneFixtures.Content(
            new SceneDocument([new TileMapPlacement(1, Grid(rows))], 2),
            SceneFixtures.Registry()));

    private static TileGrid Grid(params string[] rows)
    {
        int width = rows[0].Length;
        int[] cells = new int[width * rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cells[(y * width) + x] = rows[y][x] == '#' ? 1 : 0;
            }
        }

        return new TileGrid(
            16,
            width,
            rows.Length,
            [TileGrid.EmptyTile, new TileDefinition("solid", SceneFixtures.Solid, TileCollision.Solid)],
            cells);
    }

    private sealed class Body : Entity
    {
        internal Body(Vector2 position)
            : base(position)
        {
            Collider = new Collider(new Vector2(8f, 8f));
            Add(Collider);
            Mover = new KinematicMover(Collider);
            Add(Mover);
        }

        internal Collider Collider { get; }

        internal KinematicMover Mover { get; }
    }

    /// <summary>A body resting across three floor cells, so a dispatch cut short is visible.</summary>
    private sealed class Straddler : Entity
    {
        internal Straddler(Vector2 position)
            : base(position)
        {
            Collider = new Collider(new Vector2(40f, 8f)) { ReportsContacts = true };
            Collider.Detects("solid");
            Collider.ContactEntered += contact => Log.Add($"+({contact.Cell!.Value.X},{contact.Cell.Value.Y})");
            Collider.ContactExited += contact => Log.Add($"-({contact.Cell!.Value.X},{contact.Cell.Value.Y})");
            Add(Collider);
        }

        internal Collider Collider { get; }

        internal List<string> Log { get; } = [];
    }

    /// <summary>A body long enough to touch far more cells than a contact buffer starts out holding.</summary>
    private sealed class Wide : Entity
    {
        internal Wide(Vector2 position)
            : base(position)
        {
            Collider = new Collider(new Vector2(45f * 16f, 8f)) { ReportsContacts = true };
            Collider.Detects("solid");
            Collider.ContactEntered += _ => Entered++;
            Add(Collider);
            Mover = new KinematicMover(Collider);
            Mover.BlocksOn("solid");
            Add(Mover);
        }

        internal Collider Collider { get; }

        internal KinematicMover Mover { get; }

        internal int Entered { get; private set; }
    }
}
