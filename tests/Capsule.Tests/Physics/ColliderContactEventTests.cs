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
        body.Collider.Detects = new("solid");
        body.Mover.BlockedBy = new("solid");
        body.Collider.ReportsContacts = true;

        List<string> log = [];
        body.Collider.ContactEntered += contact => log.Add($"+{contact.LayerName}({contact.Tile!.Value.X},{contact.Tile.Value.Y})");
        body.Collider.ContactExited += contact => log.Add($"-{contact.LayerName}({contact.Tile!.Value.X},{contact.Tile.Value.Y})");

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
        body.Collider.Detects = new("solid");
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
        body.Collider.Detects = new("solid");

        int entered = 0;
        body.Collider.ContactEntered += _ => entered++;

        scene.Add(body);
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(0, entered);
        Assert.Empty(body.Collider.Touching.ToArray());
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
    // the dispatch: what it announced is exited. The rest of the settled set was never announced. It
    // is dropped without an exit rather than entered on a collider out of the world.
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

    // A settle that loses contacts at both ends and gains them at one keeps every group in the
    // world's order.
    [Fact]
    public void ContactEvents_KeepTheSameOrderForAColliderTouchingManyThings()
    {
        Scene scene = SceneFixtures.Terrain(new string('.', 20), new string('#', 20));
        Straddler body = new(new Vector2(68f, 8f), 184f);
        scene.Add(body);

        using SimulationHost run = new(scene);
        run.Step();

        Assert.Equal([.. Enumerable.Range(4, 12).Select(static x => $"+({x},1)")], body.Log);

        // Two cells to the left: cells 2 to 13.
        body.Log.Clear();
        body.Teleport(new Vector2(36f, 8f));
        run.Step();

        Assert.Equal(["-(14,1)", "-(15,1)", "+(2,1)", "+(3,1)"], body.Log);
        Assert.Equal(
            Enumerable.Range(2, 12),
            body.Collider.Touching.ToArray().Select(static contact => contact.Tile!.Value.X));

        // Four cells to the right of the start: cells 6 to 17.
        body.Log.Clear();
        body.Teleport(new Vector2(100f, 8f));
        run.Step();

        Assert.Equal(["-(2,1)", "-(3,1)", "-(4,1)", "-(5,1)", "+(14,1)", "+(15,1)", "+(16,1)", "+(17,1)"], body.Log);
    }

    // A carried contact sits behind a new one in the world's order. Detaching from the new one's
    // enter exits both announced contacts in Touching order, and the new one the loop had not reached
    // goes unannounced.
    [Fact]
    public void AContactEnteredHandlerThatDetaches_ExitsTheCarriedContactsBehindIt()
    {
        Scene scene = SceneFixtures.Terrain("....", "###.");
        Straddler body = new(new Vector2(36f, 8f));
        scene.Add(body);

        using SimulationHost run = new(scene);
        run.Step();

        body.Collider.ContactEntered += _ => body.Remove(body.Collider);
        body.Teleport(new Vector2(0f, 8f));
        run.Step();

        Assert.Equal(["+(2,1)", "+(0,1)", "-(0,1)", "-(2,1)"], body.Log);
        Assert.Null(body.Collider.World);
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
        Assert.Equal(landed.Length, landed.Select(contact => contact.Tile!.Value.X).Distinct().Count());
        Assert.Equal(8f, body.Position.Y, CollisionFixtures.Tolerance);

        simulation.Step(SceneFixtures.Step(0));

        Assert.True(
            body.Collider.Touching.Length >= 40,
            $"the collider settled on {body.Collider.Touching.Length} cells, which does not exercise a full buffer.");
        Assert.Equal(body.Collider.Touching.Length, body.Entered);
    }

    /// <summary>A body resting across floor cells, three at its default width, so a dispatch cut short is visible.</summary>
    private sealed class Straddler : Entity
    {
        internal Straddler(Vector2 position, float width = 40f)
            : base(position)
        {
            Collider = new BoxCollider2D(new Vector2(width, 8f)) { ReportsContacts = true };
            Collider.Detects = new("solid");
            Collider.ContactEntered += contact => Log.Add($"+({contact.Tile!.Value.X},{contact.Tile.Value.Y})");
            Collider.ContactExited += contact => Log.Add($"-({contact.Tile!.Value.X},{contact.Tile.Value.Y})");
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
            Collider.Detects = new("solid");
            Collider.ContactEntered += _ => Entered++;
            Add(Collider);
            Mover = new KinematicBody2D(Collider);
            Mover.BlockedBy = new("solid");
            Add(Mover);
        }

        internal BoxCollider2D Collider { get; }

        internal KinematicBody2D Mover { get; }

        internal int Entered { get; private set; }
    }
}
