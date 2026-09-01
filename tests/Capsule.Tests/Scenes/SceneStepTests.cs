using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Components;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;

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
        joining.Add(new QuadRenderer(Vector2.One, SceneFixtures.Solid));

        void Hook(Scene scene, in StepContext context) => scene.Add(joining);

        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(step: Hook));

        simulation.Step(SceneFixtures.Step());

        QuadIntent quad = simulation.View.Quads[^1];
        Assert.Equal(new Vector2(40, 40), quad.PreviousPosition);
        Assert.Equal(quad.PreviousPosition, quad.Position);
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

    private static SceneSimulation Simulation(Scene scene, params Entity[] entities)
    {
        foreach (Entity entity in entities)
        {
            scene.Add(entity);
        }

        return new SceneSimulation(scene);
    }
}
