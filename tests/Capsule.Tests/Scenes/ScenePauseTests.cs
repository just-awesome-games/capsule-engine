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
        Recorder unlessPaused = new("unlessPaused", log) { StepMode = StepMode.UnlessPaused };
        inheriting.Parent = pausable;
        never.Parent = always;

        HookScene scene = new(step: (Scene s, in StepContext context) =>
        {
            if (context.Tick == 1)
            {
                s.Paused = true;
            }
        });
        using SceneSimulation simulation = Simulation(scene, pausable, always, menu, unlessPaused);
        log.Clear();

        simulation.Step(Step(0));
        Assert.Equal(Stepped("pausable", "inheriting", "always", "unlessPaused"), log);

        log.Clear();
        simulation.Step(Step(1));
        Assert.Equal(Stepped("pausable", "inheriting", "always", "unlessPaused"), log);

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
    // changes nothing: a sum would hold step 4 too and a replacement would release step 3. An entity
    // stepping unless paused steps through it.
    [Fact]
    public void Freeze_HoldsExactlyItsSteps_KeepsTheLongerOfTwo_AndNeverStepsWhenPausedOrUnlessPaused()
    {
        Drifter drifter = new();
        Drifter unlessPaused = new() { StepMode = StepMode.UnlessPaused };
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
        using SceneSimulation simulation = Simulation(scene, drifter, menu, unlessPaused);
        log.Clear();

        List<float> positions = [];
        for (int tick = 0; tick < 5; tick++)
        {
            simulation.Step(Step(tick));
            positions.Add(drifter.Position.X);
        }

        Assert.Equal([1f, 1f, 1f, 1f, 2f], positions);
        Assert.Equal(5f, unlessPaused.Position.X);
        Assert.Empty(log);
    }

    // A pause or a freeze over steps 4 to 7 holds the camera by its StepMode, where null leaves the
    // default. A held camera skips its late step and draws step 3's shake still, and the shake then
    // resumes where it stopped.
    [Theory]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    [InlineData(StepMode.Pausable, false, true)]
    [InlineData(StepMode.Always, true, false)]
    public void ACamera_HoldsItsLateStepAndShake_WhereItsStepModeHoldsAPauseOrAFreeze(StepMode? mode, bool pause, bool holds)
    {
        (Rect[] twin, _) = Shaken(null, pause: false, freeze: false);
        (Rect[] regions, int lateSteps) = Shaken(mode, pause, freeze: !pause);

        if (holds)
        {
            Assert.All(regions[4..8], region => Assert.Equal(twin[3], region));
            Assert.Equal(twin[4..8], regions[8..]);
            Assert.Equal(8, lateSteps);
        }
        else
        {
            Assert.Equal(twin, regions);
            Assert.Equal(12, lateSteps);
        }

        Assert.NotEqual(twin[3], twin[4]);
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

        async Routine Hold(Scene s)
        {
            s.Freeze(2);
            await Wait.Until(() => !s.IsFrozen);
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

    // A camera's hold settles at the top of a step, as an entity's does. A mode written during step 0
    // waits for step 1, and so does one written during step 1 before the camera cuts by reinstalling
    // itself. A camera installed during step 2 holds by its own mode on that step.
    [Fact]
    public void ACameraModeWrittenMidStep_WaitsForTheNextStep_AndACameraInstalledMidStepHoldsByItsOwn()
    {
        CountingCamera first = new();
        CountingCamera second = new() { StepMode = StepMode.Always };
        HookScene scene = new(start: s => ((HookScene)s).Install(first), step: (Scene s, in StepContext context) =>
        {
            if (context.Tick == 0)
            {
                first.StepMode = StepMode.Never;
            }
            else if (context.Tick == 1)
            {
                first.StepMode = StepMode.Always;
                ((HookScene)s).Install(first);
            }
            else if (context.Tick == 2)
            {
                ((HookScene)s).Install(second);
            }
        });
        using SceneSimulation simulation = new(scene);

        for (int step = 0; step < 3; step++)
        {
            simulation.Step(Step(step));
        }

        Assert.Equal(1, first.LateSteps);
        Assert.Equal(1, second.LateSteps);
    }

    // A camera follows a drifter into bounds opening ahead of it from step 1, by an ease or a chase
    // slower than the follow, and the view rides the opening edge. Paused over steps 4 to 7, it draws
    // step 3's view still and then each view its unpaused twin drew.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APausedFollowThroughABoundsChange_ResumesAsItsUnpausedTwin(bool chase)
    {
        Rect[] twin = Followed(chase, pause: false);
        Rect[] regions = Followed(chase, pause: true);

        Assert.All(regions[4..8], region => Assert.Equal(twin[3], region));
        Assert.Equal(twin[4..12], regions[8..]);
        Assert.NotEqual(twin[3], twin[4]);
    }

    private static Rect[] Followed(bool chase, bool pause)
    {
        Drifter drifter = new(Viewport / 2f, new Vector2(4f, 0f));
        (SceneSimulation simulation, Camera camera) = Following(drifter, c =>
        {
            c.Bounds = OneScreen;
            c.SmoothTime = 0.1f;
        });
        using (simulation)
        {
            camera.Shake(1f);

            Rect[] regions = new Rect[pause ? 16 : 12];
            for (int step = 0; step < regions.Length; step++)
            {
                if (step == 1)
                {
                    camera.SetBounds(
                        new Rect(Vector2.Zero, Viewport + new Vector2(64f, 0f)),
                        chase ? BoundsTransition.Smooth(0.25f, maxSpeed: 30f) : BoundsTransition.Eased(1f, Ease.Linear));
                }

                simulation.Scene.Paused = pause && step is >= 4 and < 8;
                simulation.Step(Step(step));
                regions[step] = camera.VisibleRegion;
            }

            return regions;
        }
    }

    // Twelve steps of a camera shaking from the start, held by a pause or a four-step freeze from step 4.
    private static (Rect[] Regions, int LateSteps) Shaken(StepMode? mode, bool pause, bool freeze)
    {
        CountingCamera camera = new();
        if (mode is { } set)
        {
            camera.StepMode = set;
        }

        HookScene scene = new(start: s =>
        {
            ((HookScene)s).Install(camera);
            Open(s, Viewport / 2f, Viewport);
            s.Camera.Bounds = OneScreen;
        });
        using SceneSimulation simulation = new(scene);
        camera.Shake(1f);

        Rect[] regions = new Rect[12];
        for (int step = 0; step < regions.Length; step++)
        {
            if (step == 4)
            {
                scene.Paused = pause;
                scene.Freeze(freeze ? 4 : 0);
            }
            else if (step == 8)
            {
                scene.Paused = false;
            }

            simulation.Step(Step(step));
            regions[step] = camera.VisibleRegion;
        }

        return (regions, camera.LateSteps);
    }

    private static void AssertStill(SpriteIntent intent)
    {
        Assert.Equal(intent.Position, intent.PreviousPosition);
        Assert.Equal(intent.Rotation, intent.PreviousRotation);
    }

    private static List<string> Stepped(params string[] names) => [.. names, .. names.Select(name => $"{name}.late")];

    private sealed class CountingCamera : Camera
    {
        internal int LateSteps { get; private set; }

        protected internal override void OnLateStep(in StepContext context) => LateSteps++;
    }
}
