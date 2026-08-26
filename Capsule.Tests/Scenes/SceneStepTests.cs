using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Components;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

/// <summary>
/// The step choreography and the deferral rule that makes it safe: everything a game may do
/// mid-step lands where the contract says it does, in an order two runs cannot disagree on.
/// </summary>
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
    public void TheScenesStepRunsFirst_ThenEachEntityWithItsComponentsAfterIt()
    {
        List<string> log = [];
        SceneFixtures.Recorder first = new("first", log);
        first.Add(new SceneFixtures.RecordingComponent("first.component", log));
        SceneFixtures.Recorder second = new("second", log);

        void Hook(Scene scene, in StepContext context) => log.Add("scene");

        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(step: Hook), first, second);
        log.Clear();

        simulation.Step(SceneFixtures.Step());

        string[] expected = ["scene", "first", "first.component", "second"];
        Assert.Equal(expected, log);
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

        // It joined after the step's updates, so it has not run one yet.
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

        // Attached partway through the drain, its hook removes what the same drain is about to.
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

        // The step's updates are long past, so it starts running on the next one.
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

    [Fact]
    public void MembershipIsReferenceIdentity_NeverAnEntityEqualsOverride()
    {
        List<string> log = [];
        SceneFixtures.Twin kept = new("kept", log);
        SceneFixtures.Twin removed = new("removed", log);
        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(), kept, removed);

        simulation.Scene.Remove(removed);

        // Same, not Equal: every Twin compares equal, which is the very confusion under test.
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
