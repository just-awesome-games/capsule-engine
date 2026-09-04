using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Rendering;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Performance;

namespace Capsule.Tests.Scenes;

public sealed class SceneStepTests
{
    [Fact]
    public void PositionsAreRetained_BeforeAnythingMoves()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(5, 5));
        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(), drifter);

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(new Vector2(5, 5), drifter.PreviousPosition);
        Assert.Equal(new Vector2(6, 5), drifter.Position);

        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(new Vector2(6, 5), drifter.PreviousPosition);
        Assert.Equal(new Vector2(7, 5), drifter.Position);
    }

    [Fact]
    public void TheScenesStepRunsFirst_ThenEachEntityWithItsComponents_ThenItsLateStep()
    {
        List<string> log = [];
        SceneFixtures.Recorder first = new("first", log);
        first.Add(new SceneFixtures.RecordingComponent("first.component", log));
        SceneFixtures.Recorder second = new("second", log);

        void Early(Scene scene, in StepContext context) => log.Add("scene");
        void Late(Scene scene, in StepContext context) => log.Add("scene.late");

        SceneSimulation simulation = Simulation(
            new SceneFixtures.HookScene(step: Early, lateStep: Late),
            first,
            second);
        log.Clear();

        simulation.Step(SceneFixtures.Step());

        string[] expected = ["scene", "first", "first.component", "second", "scene.late"];
        Assert.Equal(expected, log);
    }

    [Fact]
    public void TheLateStepReadsThisStepsPositions_WhereTheStepReadsTheOneBeforeIt()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(5, 5));
        Vector2 seenEarly = Vector2.Zero;
        Vector2 seenLate = Vector2.Zero;

        void Early(Scene scene, in StepContext context) => seenEarly = drifter.Position;
        void Late(Scene scene, in StepContext context) => seenLate = drifter.Position;

        SceneSimulation simulation = Simulation(
            new SceneFixtures.HookScene(step: Early, lateStep: Late),
            drifter);

        simulation.Step(SceneFixtures.Step());
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(new Vector2(6, 5), seenEarly);
        Assert.Equal(new Vector2(7, 5), seenLate);
    }

    [Fact]
    public void AnAddOrRemoveIssuedByTheLateStep_LandsAtTheEndOfTheStepLikeAnyOther()
    {
        List<string> log = [];
        SceneFixtures.Recorder leaving = new("leaving", log);
        SceneFixtures.Drifter joining = new(new Vector2(3, 3));
        int heldDuringTheLateStep = 0;

        void Late(Scene scene, in StepContext context)
        {
            if (context.Tick != 0)
            {
                return;
            }

            scene.Add(joining);
            scene.Remove(leaving);
            heldDuringTheLateStep = scene.Entities.Length;
        }

        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(lateStep: Late), leaving);
        log.Clear();

        simulation.Step(SceneFixtures.Step());

        string[] expected = ["leaving", "leaving-"];
        Assert.Equal(1, heldDuringTheLateStep);
        Assert.Equal(expected, log);
        Assert.Same(joining, Assert.Single(simulation.Scene.Entities.ToArray()));

        Assert.Equal(new Vector2(3, 3), joining.Position);

        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(new Vector2(4, 3), joining.Position);
    }

    [Fact]
    public void AStartRunsExactlyOnce_BeforeTheFirstFrame()
    {
        SceneFixtures.HookScene scene = new(start: static started => started.Camera.ViewportSize = new Vector2(320, 180));
        SceneSimulation simulation = new(scene);

        Assert.Equal(new Vector2(320, 180), simulation.View.Camera.Size);

        simulation.Step(SceneFixtures.Step());
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(1, scene.Starts);
    }

    [Fact]
    public void AnEntityAddedDuringAStep_JoinsAtTheEndOfIt()
    {
        SceneFixtures.Drifter joining = new();
        int seenDuringTheStep = 0;

        void Hook(Scene scene, in StepContext context) => scene.Add(joining);

        SceneFixtures.Watcher watcher = new(scene => seenDuringTheStep = scene.Entities.Length);
        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(step: Hook), watcher);

        simulation.Step(SceneFixtures.Step());

        Entity[] expected = [watcher, joining];
        Assert.Equal(1, seenDuringTheStep);
        Assert.Equal(expected, simulation.Scene.Entities.ToArray());
        Assert.Same(simulation.Scene, joining.Scene);

        Assert.Equal(Vector2.Zero, joining.Position);
    }

    [Fact]
    public void AnEntityRemovedDuringAStep_UpdatesOnceMoreAndLeavesAtTheEndOfIt()
    {
        List<string> log = [];
        SceneFixtures.Recorder leaving = new("leaving", log);

        void Hook(Scene scene, in StepContext context) => scene.Remove(leaving);

        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(step: Hook), leaving);
        log.Clear();

        simulation.Step(SceneFixtures.Step());

        string[] expected = ["leaving", "leaving-"];
        Assert.Equal(expected, log);
        Assert.Empty(simulation.Scene.Entities.ToArray());
        Assert.Null(leaving.Scene);
    }

    [Fact]
    public void AnEntityRemovedTwiceInOneStep_StillUpdatesAndDetachesExactlyOnce()
    {
        List<string> log = [];
        SceneFixtures.Recorder leaving = new("leaving", log);

        void Hook(Scene scene, in StepContext context)
        {
            scene.Remove(leaving);
            scene.Remove(leaving);
        }

        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(step: Hook), leaving);
        log.Clear();

        simulation.Step(SceneFixtures.Step());

        string[] expected = ["leaving", "leaving-"];
        Assert.Equal(expected, log);
        Assert.Empty(simulation.Scene.Entities.ToArray());
        Assert.Null(leaving.Scene);
    }

    [Fact]
    public void ALifecycleHookRemovingAnAlreadyQueuedEntity_DetachesItExactlyOnce()
    {
        List<string> log = [];
        SceneFixtures.Recorder leaving = new("leaving", log);

        SceneFixtures.Meddler meddler = new(scene => scene.Remove(leaving));

        void Hook(Scene scene, in StepContext context)
        {
            scene.Remove(leaving);
            scene.Add(meddler);
        }

        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(step: Hook), leaving);
        log.Clear();

        simulation.Step(SceneFixtures.Step());

        string[] expected = ["leaving", "leaving-"];
        Assert.Equal(expected, log);
        Assert.Same(meddler, Assert.Single(simulation.Scene.Entities.ToArray()));
    }

    [Fact]
    public void AnEntityAddedByALifecycleHook_JoinsInTheSameDrainAndUpdatesNextStep()
    {
        SceneFixtures.Drifter grandchild = new(new Vector2(9, 9));
        SceneFixtures.Meddler meddler = new(scene => scene.Add(grandchild));

        void Hook(Scene scene, in StepContext context)
        {
            if (context.Tick == 0)
            {
                scene.Add(meddler);
            }
        }

        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(step: Hook));

        simulation.Step(SceneFixtures.Step());

        Entity[] expected = [meddler, grandchild];
        Assert.Equal(expected, simulation.Scene.Entities.ToArray());

        Assert.Equal(new Vector2(9, 9), grandchild.Position);

        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(new Vector2(10, 9), grandchild.Position);
    }

    // Spawn and despawn in one step: the drain attaches it and then detaches it, so its hooks run
    // in pairs rather than the remove being refused for an entity the scene does not yet hold.
    [Fact]
    public void AnEntityAddedAndRemovedInOneStep_AttachesThenDetachesInTheSameDrain()
    {
        List<string> log = [];
        SceneFixtures.Recorder fleeting = new("fleeting", log);

        void Hook(Scene scene, in StepContext context)
        {
            scene.Add(fleeting);
            scene.Remove(fleeting);
        }

        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(step: Hook));

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["fleeting+", "fleeting-"], log);
        Assert.Empty(simulation.Scene.Entities.ToArray());
        Assert.Null(fleeting.Scene);
    }

    [Fact]
    public void AStartedScene_CannotBeGivenToASecondSimulation()
    {
        SceneFixtures.HookScene scene = new();
        SceneSimulation first = new(scene);

        Assert.Throws<InvalidOperationException>(() => new SceneSimulation(scene));
        Assert.Equal(1, scene.Starts);
        Assert.Same(scene, first.Scene);
    }

    [Fact]
    public void AnEntitySpawnedDuringAStep_DrawsWhereItIsRatherThanSlidingIn()
    {
        SceneFixtures.Drifter joining = new(new Vector2(40, 40));
        joining.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));

        void Hook(Scene scene, in StepContext context) => scene.Add(joining);

        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(step: Hook));

        simulation.Step(SceneFixtures.Step());

        SpriteIntent sprite = simulation.View.Sprites[^1];
        Assert.Equal(new Vector2(40, 40), sprite.PreviousPosition);
        Assert.Equal(sprite.PreviousPosition, sprite.Position);
    }

    [Fact]
    public void AnEntityAlreadyInAScene_CannotBeAddedTwice()
    {
        SceneFixtures.Drifter drifter = new();
        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(), drifter);

        Assert.Throws<InvalidOperationException>(() => simulation.Scene.Add(drifter));
    }

    [Fact]
    public void RequestingExit_ReachesTheHost()
    {
        void Hook(Scene scene, in StepContext context) => scene.RequestExit();

        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(step: Hook));

        Assert.False(simulation.ExitRequested);

        simulation.Step(SceneFixtures.Step());

        Assert.True(simulation.ExitRequested);
    }

    // The deferred queues answer the same way. Indexed by value, one twin would be read as the
    // other already queued, and the second of them would be silently dropped.
    [Fact]
    public void QueueMembershipIsReferenceIdentityToo_NeverAnEntityEqualsOverride()
    {
        List<string> log = [];
        SceneFixtures.Twin first = new("first", log);
        SceneFixtures.Twin second = new("second", log);
        SceneFixtures.HookScene scene = new(step: (Scene joined, in StepContext _) =>
        {
            joined.Add(first);
            joined.Add(second);
        });

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(2, scene.Entities.Length);
        Assert.Same(first, scene.Entities[0]);
        Assert.Same(second, scene.Entities[1]);

        // And the same for the remove queue, drained the step after.
        scene.Remove(first);
        scene.Remove(second);

        Assert.Empty(scene.Entities.ToArray());
        Assert.Equal(["first-", "second-"], log);
    }

    [Fact]
    public void MembershipIsReferenceIdentity_NeverAnEntityEqualsOverride()
    {
        List<string> log = [];
        SceneFixtures.Twin kept = new("kept", log);
        SceneFixtures.Twin removed = new("removed", log);
        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(), kept, removed);

        simulation.Scene.Remove(removed);

        Assert.Equal(1, simulation.Scene.Entities.Length);
        Assert.Same(kept, simulation.Scene.Entities[0]);
        Assert.Same(simulation.Scene, kept.Scene);
        Assert.Null(removed.Scene);
        Assert.Equal(["removed-"], log);
    }

    [Fact]
    public void DeterministicSteps_ProduceDeterministicResults()
    {
        using SceneSimulation first = new(StageWorkload.Compose(StageWorkload.Build()));
        using SceneSimulation second = new(StageWorkload.Compose(StageWorkload.Build()));

        InputState input1 = new(new ActionBindings());
        InputState input2 = new(new ActionBindings());

        const int Steps = 120;
        for (int step = 0; step < Steps; step++)
        {
            input1.Advance(DeviceSnapshot.Empty);
            input2.Advance(DeviceSnapshot.Empty);

            StepContext context = new(StageWorkload.StepSeconds, input1, step);
            first.Step(context);
            context = new(StageWorkload.StepSeconds, input2, step);
            second.Step(context);

            Entity[] entities1 = first.Scene.Entities.ToArray();
            Entity[] entities2 = second.Scene.Entities.ToArray();

            Assert.Equal(entities1.Length, entities2.Length);

            for (int i = 0; i < entities1.Length; i++)
            {
                Assert.Equal(entities1[i].Position, entities2[i].Position);
                Assert.Equal(entities1[i].PreviousPosition, entities2[i].PreviousPosition);
            }

            ReadOnlySpan<SpriteIntent> sprites1 = first.View.Sprites;
            ReadOnlySpan<SpriteIntent> sprites2 = second.View.Sprites;

            Assert.Equal(sprites1.Length, sprites2.Length);
            for (int i = 0; i < sprites1.Length; i++)
            {
                Assert.Equal(sprites1[i], sprites2[i]);
            }
        }
    }

    // The whole reason the structural and temporal hooks are separate: composition adds one entry
    // at a time, so an entry notified as it attaches cannot see the ones after it.
    [Fact]
    public void AnEntityStarting_SeesEveryEntryTheDocumentComposedAlongsideIt()
    {
        List<string> found = [];

        EntityRegistry registry = SceneFixtures.Registry(
            ("seeker", spawn => new Seeker(spawn, found)),
            ("placed", spawn => new SceneFixtures.Placed(spawn)));

        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.RoomWithoutTerrain(
                new EntityPlacement(1, "seeker", 0, 0),
                new EntityPlacement(2, "placed", 16, 0)),
            registry);

        using SceneSimulation simulation = new(scene);

        Assert.Equal(["placed"], found);
    }

    // A wave spawned together is one batch: the drain attaches all of it before any of it starts,
    // so no member of the wave sees a half-built scene.
    [Fact]
    public void ABatchSpawnedInOneStep_StartsOnlyOnceAllOfItHasAttached()
    {
        List<int> peers = [];

        void Count(Scene scene) => peers.Add(scene.Entities.Length);

        SceneFixtures.Starter first = new(Count);
        SceneFixtures.Starter second = new(Count);

        void Hook(Scene scene, in StepContext context)
        {
            if (context.Tick == 0)
            {
                scene.Add(first);
                scene.Add(second);
            }
        }

        using SceneSimulation simulation = new(new SceneFixtures.HookScene(step: Hook));

        simulation.Step(SceneFixtures.Step());

        Assert.Equal([2, 2], peers);
    }

    [Fact]
    public void AComponentAttachedToAStartedEntity_StartsAsItIsAttached()
    {
        List<string> log = [];
        SceneFixtures.Drifter host = new();

        using SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(), host);

        host.Add(new Tracker("late", log));

        Assert.Equal(["late!"], log);
    }

    // Removal is deferred, so an entity queued to leave still names its scene while the drain runs.
    // It has no step left in it, and starting is once for a component's lifetime, so one taken on
    // here must wait: started now it would have searched a scene it never steps, and could never
    // start again on the add that does step it.
    [Fact]
    public void AComponentAttachedToAnEntityQueuedForRemoval_WaitsForTheNextAddToStart()
    {
        List<string> log = [];
        SceneFixtures.Drifter host = new();
        Tracker late = new("late", log);

        SceneFixtures.Starter remover = new(scene =>
        {
            scene.Remove(host);
            host.Add(late);
        });

        void Hook(Scene scene, in StepContext context)
        {
            if (context.Tick == 0)
            {
                scene.Add(remover);
            }
        }

        using SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(step: Hook), host);

        simulation.Step(SceneFixtures.Step());

        Assert.Empty(log);
        Assert.Null(host.Scene);

        simulation.Step(SceneFixtures.Step(1));

        Assert.Empty(log);

        simulation.Scene.Add(host);
        simulation.Step(SceneFixtures.Step(2));

        Assert.Equal(["late!", "late"], log);
    }

    // The drain starts a batch after the removes, so a peer that started first may already have
    // queued one of them to leave. It never steps, so time must never begin for it either.
    [Fact]
    public void AnEntityQueuedForRemovalByAPeersStart_NeverStarts()
    {
        List<string> log = [];
        Lifecycle doomed = new("doomed", log);
        SceneFixtures.Starter remover = new(scene => scene.Remove(doomed));

        void Hook(Scene scene, in StepContext context)
        {
            if (context.Tick == 0)
            {
                scene.Add(remover);
                scene.Add(doomed);
            }
        }

        using SceneSimulation simulation = new(new SceneFixtures.HookScene(step: Hook));

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["doomed+", "doomed-"], log);
        Assert.Same(remover, Assert.Single(simulation.Scene.Entities.ToArray()));
        Assert.Null(doomed.Scene);
    }

    [Fact]
    public void AnEntityLeavingTheSceneFromItsOwnStart_StartsNoneOfItsComponents()
    {
        List<string> log = [];
        SceneFixtures.Starter? host = null;
        host = new SceneFixtures.Starter(scene => scene.Remove(host!));
        host.Add(new Tracker("component", log));

        SceneFixtures.HookScene scene = new();
        scene.Add(host);

        using SceneSimulation simulation = new(scene);

        Assert.Empty(log);
        Assert.Empty(simulation.Scene.Entities.ToArray());
    }

    // An entity that left from its own start holds components that never started, and its own
    // start does not run twice. Added again it steps them, so the second start must reach them.
    [Fact]
    public void AComponentOnAnEntityAddedBackToTheScene_StartsBeforeItSteps()
    {
        List<string> log = [];
        SceneFixtures.Starter? host = null;
        host = new SceneFixtures.Starter(scene => scene.Remove(host!));
        host.Add(new Tracker("component", log));

        SceneFixtures.HookScene scene = new();
        scene.Add(host);

        using SceneSimulation simulation = new(scene);

        Assert.Empty(log);

        scene.Add(host);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["component!", "component"], log);
    }

    // The scene's own step runs before the entity pass, so an entity queued to leave there is
    // still walked for the rest of the step. A component taken on in that window cannot start —
    // its entity has no step left to give it — so it must take no step either.
    [Fact]
    public void AComponentAttachedMidStepToAnEntityQueuedForRemoval_TakesNoStepBeforeItStarts()
    {
        List<string> log = [];
        SceneFixtures.Drifter host = new();
        Tracker late = new("late", log);

        void Hook(Scene scene, in StepContext context)
        {
            if (context.Tick == 0)
            {
                scene.Remove(host);
                host.Add(late);
            }
        }

        using SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(step: Hook), host);

        simulation.Step(SceneFixtures.Step());

        Assert.Empty(log);
        Assert.Null(host.Scene);

        simulation.Scene.Add(host);
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(["late!", "late"], log);
    }

    // A start that throws leaves the entities queued behind it stranded: the scene holds them and
    // the next step would otherwise walk them. Nothing steps before it has started, so they wait
    // for the drain that starts them.
    [Fact]
    public void AnEntityStrandedByAPeersFailedStart_TakesNoStepUntilItHasStarted()
    {
        List<string> log = [];
        Lifecycle stranded = new("stranded", log);

        void Hook(Scene scene, in StepContext context)
        {
            if (context.Tick == 0)
            {
                scene.Add(new Thrower());
                scene.Add(stranded);
            }
        }

        using SceneSimulation simulation = new(new SceneFixtures.HookScene(step: Hook));

        Assert.Throws<InvalidOperationException>(() => simulation.Step(SceneFixtures.Step()));
        Assert.Equal(["stranded+"], log);

        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(["stranded+", "stranded!"], log);

        simulation.Step(SceneFixtures.Step(2));

        Assert.Equal(["stranded+", "stranded!", "stranded"], log);
    }

    // A component's start may detach a sibling, which shifts the rest of the list left. The one
    // shifted into the vacated slot still steps, so it must still start.
    [Fact]
    public void AComponentDetachingASiblingFromItsStart_LeavesNoLaterSiblingUnstarted()
    {
        List<string> log = [];
        SceneFixtures.Drifter host = new();
        Tracker first = new("first", log);
        host.Add(first);
        host.Add(new SiblingRemover(first, log));
        host.Add(new Tracker("third", log));

        using SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(), host);

        Assert.Equal(["first!", "remover!", "third!"], log);

        log.Clear();
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["remover", "third"], log);
    }

    private static SceneSimulation Simulation(Scene scene, params Entity[] entities)
    {
        foreach (Entity entity in entities)
        {
            scene.Add(entity);
        }

        return new SceneSimulation(scene);
    }

    private sealed class Seeker(EntitySpawn spawn, List<string> found) : Entity(spawn.Position)
    {
        protected internal override void OnStart() =>
            found.Add(Scene!.FindSingle<SceneFixtures.Placed>().Spawn.Type);
    }

    private sealed class Lifecycle(string name, List<string> log) : Entity(Vector2.Zero)
    {
        protected internal override void OnAddedToScene() => log.Add($"{name}+");

        protected internal override void OnStart() => log.Add($"{name}!");

        protected internal override void OnStep(in StepContext context) => log.Add(name);

        protected internal override void OnRemovedFromScene() => log.Add($"{name}-");
    }

    private sealed class Thrower() : Entity(Vector2.Zero)
    {
        protected internal override void OnStart() =>
            throw new InvalidOperationException("This entity refuses to start.");
    }

    private sealed class Tracker(string name, List<string> log) : Component
    {
        protected internal override void OnStart() => log.Add($"{name}!");

        protected internal override void OnStep(in StepContext context) => log.Add(name);
    }

    private sealed class SiblingRemover(Component sibling, List<string> log) : Component
    {
        protected internal override void OnStart()
        {
            log.Add("remover!");
            Entity!.Remove(sibling);
        }

        protected internal override void OnStep(in StepContext context) => log.Add("remover");
    }
}
