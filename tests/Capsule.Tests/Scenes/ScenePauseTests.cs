using System.Numerics;
using Capsule.Animation;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tiles;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Scenes;

public sealed class ScenePauseTests
{
    // A pause written mid-step settles at the top of the next one. The step that wrote it runs
    // whole. An explicit mode beats the one its parent hands down.
    [Fact]
    public void Pausing_HoldsPausableAndInheritingEntities_AndStepsTheRest_FromTheNextStep()
    {
        List<string> log = [];
        Recorder pausable = new("pausable", log);
        Recorder inheriting = new("inheriting", log);
        Recorder always = new("always", log) { StepMode = StepMode.Always };
        Recorder never = new("never", log) { StepMode = StepMode.Never };
        Recorder menu = new("menu", log) { StepMode = StepMode.WhenPaused };
        inheriting.Parent = pausable;
        never.Parent = always;

        HookScene scene = new(step: (Scene s, in StepContext context) =>
        {
            if (context.Tick == 1)
            {
                s.Paused = true;
            }
        });
        using SceneSimulation simulation = Simulation(scene, pausable, always, menu);
        log.Clear();

        simulation.Step(Step(0));
        Assert.Equal(Stepped("pausable", "inheriting", "always"), log);

        log.Clear();
        simulation.Step(Step(1));
        Assert.Equal(Stepped("pausable", "inheriting", "always"), log);

        log.Clear();
        simulation.Step(Step(2));
        Assert.Equal(Stepped("always", "menu"), log);
    }

    // Holds resolve again only after a change, and a StepMode write is one. It reaches the subtree
    // inheriting from the entity written.
    [Fact]
    public void AStepModeWrite_TakesEffectFromTheNextStep_DownItsInheritingSubtree()
    {
        List<string> log = [];
        Recorder parent = new("parent", log);
        Recorder child = new("child", log) { Parent = parent };

        HookScene scene = new(step: (Scene s, in StepContext context) =>
        {
            if (context.Tick == 1)
            {
                parent.StepMode = StepMode.Always;
            }
        });
        using SceneSimulation simulation = Simulation(scene, parent);
        scene.Paused = true;
        log.Clear();

        simulation.Step(Step(0));
        simulation.Step(Step(1));
        Assert.Empty(log);

        simulation.Step(Step(2));
        Assert.Equal(Stepped("parent", "child"), log);
    }

    // Freeze(3) during step 0 holds steps 1 to 3. Freeze(1) during step 1, with two steps still owed,
    // changes nothing: a sum would hold step 4 too and a replacement would release step 3.
    [Fact]
    public void Freeze_HoldsExactlyItsSteps_KeepsTheLongerOfTwo_AndNeverStepsWhenPaused()
    {
        Drifter drifter = new();
        List<string> log = [];
        Recorder menu = new("menu", log) { StepMode = StepMode.WhenPaused };

        HookScene scene = new(step: (Scene s, in StepContext context) =>
        {
            if (context.Tick == 0)
            {
                s.Freeze(3);
            }
            else if (context.Tick == 1)
            {
                s.Freeze(1);
            }
        });
        using SceneSimulation simulation = Simulation(scene, drifter, menu);
        log.Clear();

        List<float> positions = [];
        for (int tick = 0; tick < 5; tick++)
        {
            simulation.Step(Step(tick));
            positions.Add(drifter.Position.X);
        }

        Assert.Equal([1f, 1f, 1f, 1f, 2f], positions);
        Assert.Empty(log);
    }

    // Freeze(2) during step 0, stretched by Freeze(3) during step 1, holds steps 1 to 4. The wait the
    // freezing beat yields resumes on step 5, the step the drifter moves in again.
    [Fact]
    public void IsFrozen_ReadsTrueThroughTheLastHeldStep_AndAWaitOnItResumesWithTheHeldEntities()
    {
        Drifter drifter = new();
        Sequence beats = new();
        long tick = 0;
        long? resumedOn = null;

        IEnumerable<Wait> Hold(Scene s)
        {
            s.Freeze(2);
            yield return Wait.Until(() => !s.IsFrozen);
            resumedOn = tick;
        }

        HookScene scene = new(step: (Scene s, in StepContext context) =>
        {
            tick = context.Tick;
            if (context.Tick == 0)
            {
                beats.Start(Hold(s));
                return;
            }

            if (context.Tick == 1)
            {
                s.Freeze(3);
            }

            beats.Step();
        });
        using SceneSimulation simulation = Simulation(scene, drifter);

        List<bool> frozen = [];
        List<float> positions = [];
        for (int step = 0; step < 6; step++)
        {
            simulation.Step(Step(step));
            frozen.Add(scene.IsFrozen);
            positions.Add(drifter.Position.X);
        }

        Assert.Equal([true, true, true, true, true, false], frozen);
        Assert.Equal([1f, 1f, 1f, 1f, 1f, 2f], positions);
        Assert.Equal(5, resumedOn);
    }

    // A held collider stays in the world but settles nothing. It is owed the enter on resume.
    [Fact]
    public void AHeldCollider_RaisesNoContactEvents_AndSettlesTheEnterItIsOwedOnResume()
    {
        Body body = new(new Vector2(4f, -100f));
        body.Collider.Detects = new("solid");
        body.Collider.ReportsContacts = true;

        List<string> log = [];
        body.Collider.ContactEntered += _ => log.Add("enter");

        HookScene scene = new();
        scene.Add(Colliding(TerrainGrid("....", "####")));
        using SceneSimulation simulation = Simulation(scene, body);

        scene.Paused = true;
        simulation.Step(Step(0));
        body.Teleport(new Vector2(4f, 12f));
        simulation.Step(Step(1));
        simulation.Step(Step(2));
        Assert.Empty(log);

        scene.Paused = false;
        simulation.Step(Step(3));
        Assert.Equal(["enter"], log);
    }

    // A held emitter's older particles draw still, or the host would replay their last motion on every
    // frame of the hold. A particle spawned during the held step keeps its spawn interpolation for that
    // step only.
    [Fact]
    public void AHeldEmitter_DrawsOlderParticlesStill_AndInterpolatesOneSpawnedThisStep()
    {
        ParticleEmitter emitter = new(Frame(4, 4), capacity: 4)
        {
            Lifetime = new FloatRange(5f, 5f),
            Speed = new FloatRange(60f, 60f),
            AngularVelocity = new FloatRange(90f, 90f),
        };
        Entity root = new EntityHierarchyFixtures.Node(new Vector2(100f, 0f));
        root.Add(emitter);

        HookScene scene = new(step: (Scene s, in StepContext context) =>
        {
            if (context.Tick == 1)
            {
                emitter.Emit(1);
            }
        });
        using SceneSimulation simulation = Simulation(scene, root);
        emitter.Emit(2);

        simulation.Step(Step(0));
        Assert.All(simulation.View.Sprites.ToArray(), intent => Assert.NotEqual(intent.PreviousPosition, intent.Position));

        scene.Paused = true;
        simulation.Step(Step(1));

        SpriteIntent[] held = simulation.View.Sprites.ToArray();
        Assert.Equal(3, held.Length);
        Assert.All(held[..2], AssertStill);
        Assert.Equal(99.5f, held[2].PreviousPosition.X, 4);
        Assert.Equal(100.5f, held[2].Position.X, 4);

        simulation.Step(Step(2));
        Assert.All(simulation.View.Sprites.ToArray(), AssertStill);
    }

    private static void AssertStill(SpriteIntent intent)
    {
        Assert.Equal(intent.Position, intent.PreviousPosition);
        Assert.Equal(intent.Rotation, intent.PreviousRotation);
    }

    private static List<string> Stepped(params string[] names) => [.. names, .. names.Select(name => $"{name}.late")];
}
