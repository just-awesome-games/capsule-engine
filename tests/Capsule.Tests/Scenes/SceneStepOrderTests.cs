using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Tests.Allocation;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Scenes;

public sealed class SceneStepOrderTests
{
    [Fact]
    public void PositionsAreRetained_BeforeAnythingMoves()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(5, 5));
        SimulationHost run = new(Simulation(new SceneFixtures.HookScene(), drifter));

        run.Step();

        Assert.Equal(new Vector2(5, 5), drifter.PreviousTransform.Position);
        Assert.Equal(new Vector2(6, 5), drifter.Position);

        run.Step();

        Assert.Equal(new Vector2(6, 5), drifter.PreviousTransform.Position);
        Assert.Equal(new Vector2(7, 5), drifter.Position);
    }

    [Fact]
    public void TheScenesStepRunsFirst_ThenEachEntityWithItsComponents_ThenEveryLateStepInThatSameOrder()
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

        string[] expected =
        [
            "scene",
            "first",
            "first.component",
            "second",
            "first.late",
            "first.component.late",
            "second.late",
            "scene.late",
        ];
        Assert.Equal(expected, log);
    }

    // An entity skips its component walk only while none of its components steps.
    [Fact]
    public void AGameComponent_AddedBesideASteplessRenderer_StepsFromTheNextStep()
    {
        List<string> log = [];
        SceneFixtures.Recorder holder = new("holder", log);
        holder.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));
        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(), holder);
        simulation.Step(SceneFixtures.Step(0));

        holder.Add(new SceneFixtures.RecordingComponent("game", log));
        log.Clear();
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(["holder", "game", "holder.late", "game.late"], log);
    }

    // A stepless type that a game could subclass, or that steps, would have its hooks skipped.
    [Fact]
    public void EverySteplessEngineComponent_IsSealed_AndOverridesNeitherStepHook()
    {
        const BindingFlags Hook = BindingFlags.Instance | BindingFlags.NonPublic;
        Type[] stepless =
        [
            .. new[] { typeof(Component).Assembly, typeof(CapsuleGame).Assembly }
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsSubclassOf(typeof(Component)) && !type.IsAbstract && !type.ContainsGenericParameters)
                .Where(type => !((Component)RuntimeHelpers.GetUninitializedObject(type)).Steps),
        ];

        Assert.Contains(typeof(SpriteRenderer), stepless);
        Assert.All(stepless, type =>
        {
            Assert.True(type.IsSealed, type.Name);
            Assert.Equal(typeof(Component), type.GetMethod("OnStep", Hook)!.DeclaringType);
            Assert.Equal(typeof(Component), type.GetMethod("OnLateStep", Hook)!.DeclaringType);
        });
    }

    [Fact]
    public void TheLateStepReadsThisStepsPositions_WhereTheStepReadsTheOneBeforeIt()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(5, 5));
        Vector2 seenEarly = Vector2.Zero;
        Vector2 seenLate = Vector2.Zero;

        void Early(Scene scene, in StepContext context) => seenEarly = drifter.Position;
        void Late(Scene scene, in StepContext context) => seenLate = drifter.Position;

        SimulationHost run = new(Simulation(
            new SceneFixtures.HookScene(step: Early, lateStep: Late),
            drifter));

        run.Step();
        run.Step();

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

        SimulationHost run = new(Simulation(new SceneFixtures.HookScene(lateStep: Late), leaving));
        log.Clear();

        run.Step();

        string[] expected = ["leaving", "leaving.late", "leaving-"];
        Assert.Equal(1, heldDuringTheLateStep);
        Assert.Equal(expected, log);
        Assert.Same(joining, Assert.Single(run.Scene.Entities.ToArray()));

        Assert.Equal(new Vector2(3, 3), joining.Position);

        run.Step();

        Assert.Equal(new Vector2(4, 3), joining.Position);
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

        string[] expected = ["leaving", "leaving.late", "leaving-"];
        Assert.Equal(expected, log);
        Assert.Empty(simulation.Scene.Entities.ToArray());
        Assert.Null(leaving.SceneOrNull);
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

        string[] expected = ["leaving", "leaving.late", "leaving-"];
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

        SimulationHost run = new(Simulation(new SceneFixtures.HookScene(step: Hook)));

        run.Step();

        Entity[] expected = [meddler, grandchild];
        Assert.Equal(expected, run.Scene.Entities.ToArray());

        Assert.Equal(new Vector2(9, 9), grandchild.Position);

        run.Step();

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
        Assert.Null(fleeting.SceneOrNull);
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
}
