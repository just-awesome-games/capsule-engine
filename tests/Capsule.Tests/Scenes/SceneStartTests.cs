using System.Numerics;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Allocation;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Scenes;

public sealed class SceneStartTests
{
    [Fact]
    public void AStartRunsExactlyOnce_BeforeTheFirstFrame()
    {
        SceneFixtures.HookScene scene = new(start: static started => started.Camera.ViewportSize = new Vector2(320, 180));
        SimulationHost run = new(scene);

        Assert.Equal(new Vector2(320, 180), run.Simulation.View.Camera.Size);

        run.Step();
        run.Step();

        Assert.Equal(1, scene.Starts);
    }

    // Composition adds one entry at a time. An entry starting sees the entries after it.
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

    // The drain attaches a whole wave before any of it starts.
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

    // An entity queued to leave still names its scene but has no step left. A component starts once
    // in its life, and one attached there waits for the add that steps it. The removal may come from a
    // peer's start in the drain or from the scene's own step.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AComponentAttachedToAnEntityQueuedForRemoval_WaitsForTheNextAddToStart(bool fromAStart)
    {
        List<string> log = [];
        SceneFixtures.Drifter host = new();
        Tracker late = new("late", log);

        void Queue(Scene scene)
        {
            scene.Remove(host);
            host.Add(late);
        }

        void Hook(Scene scene, in StepContext context)
        {
            if (context.Tick != 0)
            {
                return;
            }

            if (fromAStart)
            {
                scene.Add(new SceneFixtures.Starter(Queue));
            }
            else
            {
                Queue(scene);
            }
        }

        using SimulationHost run = new(Simulation(new SceneFixtures.HookScene(step: Hook), host));

        run.Step();

        Assert.Empty(log);
        Assert.Null(host.SceneOrNull);

        run.Step();

        Assert.Empty(log);

        run.Scene.Add(host);
        run.Step();

        Assert.Equal(["late!", "late"], log);
    }

    // A peer's start can queue a batch member to leave before its own start. It never starts.
    [Fact]
    public void AnEntityQueuedForRemovalByAPeersStart_NeverStarts()
    {
        List<string> log = [];
        SceneFixtures.Recorder doomed = new("doomed", log, logsStart: true);
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
        Assert.Null(doomed.SceneOrNull);
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

    // An entity that left from its own start does not start twice. Added again, its components start.
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

    // A start that throws strands the entities behind it in the scene. They take no step until started.
    [Fact]
    public void AnEntityStrandedByAPeersFailedStart_TakesNoStepUntilItHasStarted()
    {
        List<string> log = [];
        SceneFixtures.Recorder stranded = new("stranded", log, logsStart: true);

        void Hook(Scene scene, in StepContext context)
        {
            if (context.Tick == 0)
            {
                scene.Add(new Thrower());
                scene.Add(stranded);
            }
        }

        using SimulationHost run = new(new SceneFixtures.HookScene(step: Hook));

        Assert.Throws<InvalidOperationException>(() => run.Step());
        Assert.Equal(["stranded+"], log);

        run.Step();

        Assert.Equal(["stranded+", "stranded!"], log);

        run.Step();

        Assert.Equal(["stranded+", "stranded!", "stranded", "stranded.late"], log);
    }

    // A component's start may detach a sibling. The one shifted into its slot still starts.
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

    private sealed class Seeker(EntitySpawn spawn, List<string> found) : Entity(spawn)
    {
        protected internal override void OnStart() =>
            found.Add(Scene!.FindSingle<SceneFixtures.Placed>().Spawn.Type!);
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
