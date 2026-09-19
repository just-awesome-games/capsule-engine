using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;
using Body = Capsule.Tests.Scenes.SceneFixtures.Body;

namespace Capsule.Tests.Physics;

public sealed class ColliderContactEventTests
{
    [Fact]
    public void ContactEvents_FireOnceOnEnterAndOnceOnExit()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        Body body = new(new Vector2(4f, 8f));
        body.Collider.SetFilter("solid");
        body.Mover.BlocksOn("solid");
        body.Collider.ReportsContacts = true;

        List<string> log = [];
        body.Collider.ContactEntered += contact => log.Add($"+{contact.LayerName}({contact.Cell!.Value.X},{contact.Cell.Value.Y})");
        body.Collider.ContactExited += contact => log.Add($"-{contact.LayerName}({contact.Cell!.Value.X},{contact.Cell.Value.Y})");

        scene.Add(body);
        using SimulationHost run = new(scene);

        // Falling onto the floor, resting on it, then being lifted off it.
        run.Step();
        Assert.Empty(log);

        body.Mover.Move(new Vector2(0f, 60f));
        run.Step();
        Assert.Equal(["+solid(0,2)"], log);

        run.Step();
        Assert.Equal(["+solid(0,2)"], log);

        body.Teleport(new Vector2(4f, -100f));
        run.Step();
        Assert.Equal(["+solid(0,2)", "-solid(0,2)"], log);
    }

    [Fact]
    public void ContactEvents_SettleBeforeEveryLateStepRuns()
    {
        Body body = new(new Vector2(4f, 8f));
        body.Collider.SetFilter("solid");
        body.Collider.ReportsContacts = true;

        List<string> log = [];
        body.Collider.ContactEntered += _ => log.Add("enter");

        SceneFixtures.HookScene scene = new(
            step: (Scene _, in StepContext _) => log.Add("step"),
            lateStep: (Scene _, in StepContext _) => log.Add("late"));
        scene.Add(new TileMap(SceneFixtures.TerrainGrid("....", "####")));
        scene.Add(body);

        // An entity's own late step is between the two: what it reads there is the health a contact
        // just spent, and the scene's late step still runs after every one of them.
        scene.Add(new SceneFixtures.Recorder("entity", log));
        log.Clear();

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(["step", "entity", "enter", "entity.late", "late"], log);
    }

    [Fact]
    public void ContactEvents_AreNotRaisedUntilAColliderOptsIn()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Body body = new(new Vector2(4f, 8f));
        body.Collider.SetFilter("solid");

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
        body.Collider.SetFilter("solid");
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

    // Turning reporting off is the other way to stop reporting contacts, and owes the same exits as
    // disabling: a handler holding "I am standing on this" is told it no longer is, rather than
    // being left permanently wrong. Turning it back on resumes from an empty set at the next
    // settle, not at the setter.
    [Fact]
    public void AColliderThatStopsAndResumesReportingContacts_EndsWhatItAnnouncedThenReannouncesAfresh()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Straddler settled = new(new Vector2(0f, 8f));
        scene.Add(settled);

        using SimulationHost run = new(scene);
        run.Step();

        // Overlapping the same floor, but never settled, so it has announced nothing to end.
        Straddler unannounced = new(new Vector2(0f, 8f));
        scene.Add(unannounced);

        Assert.Equal(["+(0,1)", "+(1,1)", "+(2,1)"], settled.Log);
        Assert.NotNull(unannounced.Collider.World);

        settled.Collider.ReportsContacts = false;
        unannounced.Collider.ReportsContacts = false;

        Assert.Equal(["+(0,1)", "+(1,1)", "+(2,1)", "-(0,1)", "-(1,1)", "-(2,1)"], settled.Log);
        Assert.Empty(unannounced.Log);

        // Still in the world: it stopped reporting, it did not leave.
        Assert.NotNull(settled.Collider.World);
        Assert.Empty(settled.Collider.Touching.ToArray());

        // Nothing raised by the on setter, and the next settle announces all three afresh rather
        // than carrying them over as already announced.
        settled.Collider.ReportsContacts = true;

        Assert.Equal(["+(0,1)", "+(1,1)", "+(2,1)", "-(0,1)", "-(1,1)", "-(2,1)"], settled.Log);

        run.Step();

        Assert.Equal(
            ["+(0,1)", "+(1,1)", "+(2,1)", "-(0,1)", "-(1,1)", "-(2,1)", "+(0,1)", "+(1,1)", "+(2,1)"],
            settled.Log);
        Assert.Equal(3, settled.Collider.Touching.Length);
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

        Assert.Throws<InvalidOperationException>(() => simulation.Step(SceneFixtures.Step(0)));

        Assert.True(body.Collider.Enabled);
    }

    // The enemy that dies on contact. Detaching the collider from inside its own enter handler ends
    // the dispatch: what it announced is exited, and the rest of the settled set — which it never
    // announced — is dropped without an exit rather than entered on a collider out of the world.
    [Fact]
    public void AContactEnteredHandlerThatDetachesItsCollider_ExitsWhatItAnnouncedAndNothingElse()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Straddler body = new(new Vector2(0f, 8f));
        scene.Add(body);
        body.Collider.ContactEntered += _ => body.Remove(body.Collider);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(["+(0,1)", "-(0,1)"], body.Log);
        Assert.Null(body.Collider.Entity);
        Assert.Null(body.Collider.World);
        Assert.Empty(body.Collider.Touching.ToArray());
    }

    // A step holding both groups is the only place the deviation from the world's order is
    // observable, so Touching is read there and the set is left from there, which is the order the
    // exits come in.
    [Fact]
    public void ContactEvents_PutCarriedContactsAheadOfNewOnesAndHoldTheWorldsOrderWithinEach()
    {
        Scene scene = SceneFixtures.Terrain("....", "###.");
        Straddler body = new(new Vector2(36f, 8f));
        scene.Add(body);

        using SimulationHost run = new(scene);
        run.Step();

        // Clear of the first two cells of the row: only the third is under it.
        Assert.Equal(["+(2,1)"], body.Log);

        // Sliding left picks up two cells the world reports ahead of the carried one, and they are
        // announced in that order rather than reversed by the partition.
        body.Teleport(new Vector2(0f, 8f));
        run.Step();

        Assert.Equal(["+(2,1)", "+(0,1)", "+(1,1)"], body.Log);

        // The world would report these three as (0,1), (1,1), (2,1); the carried one is held ahead
        // of the two new ones instead, each group in that world order.
        Assert.Equal(
            ["(2,1)", "(0,1)", "(1,1)"],
            body.Collider.Touching.ToArray().Select(contact => $"({contact.Cell!.Value.X},{contact.Cell.Value.Y})"));

        // Leaving from that mixed set rather than from a settled one, so the exits are ordered by a
        // Touching that still deviates from the world's order.
        body.Teleport(new Vector2(0f, -100f));
        run.Step();

        Assert.Equal(
            ["+(2,1)", "+(0,1)", "+(1,1)", "-(2,1)", "-(0,1)", "-(1,1)"],
            body.Log);
    }

    // The canonical throw during a settle: the failure reaches the caller of the step rather than
    // being swallowed by it.
    [Fact]
    public void AContactEnteredHandlerThatThrows_FailsTheStep()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Straddler body = new(new Vector2(0f, 8f));
        scene.Add(body);

        body.Collider.ContactEntered += _ => throw new InvalidOperationException("a handler of the consuming game's own.");

        using SimulationHost run = new(scene);

        Assert.Throws<InvalidOperationException>(() => run.Step());
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
        Assert.Equal(8f, body.Position.Y, CollisionFixtures.Tolerance);

        simulation.Step(SceneFixtures.Step(0));

        Assert.True(
            body.Collider.Touching.Length >= 40,
            $"the collider settled on {body.Collider.Touching.Length} cells, which does not exercise a full buffer.");
        Assert.Equal(body.Collider.Touching.Length, body.Entered);
    }

    /// <summary>A body resting across three floor cells, so a dispatch cut short is visible.</summary>
    private sealed class Straddler : Entity
    {
        internal Straddler(Vector2 position)
            : base(position)
        {
            Collider = new BoxCollider2D(new Vector2(40f, 8f)) { ReportsContacts = true };
            Collider.SetFilter("solid");
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
            Collider.SetFilter("solid");
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
