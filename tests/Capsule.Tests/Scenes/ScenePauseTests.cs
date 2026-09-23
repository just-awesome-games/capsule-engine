using System.Numerics;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tiles;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Scenes;

public sealed class ScenePauseTests
{
    // A pause written mid-step settles at the top of the next one, so the step that wrote it runs
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

    // A held collider stays in the world but settles nothing, so it is owed the enter on resume.
    [Fact]
    public void AHeldCollider_RaisesNoContactEvents_AndSettlesTheEnterItIsOwedOnResume()
    {
        Body body = new(new Vector2(4f, -100f));
        body.Collider.SetFilter("solid");
        body.Collider.ReportsContacts = true;

        List<string> log = [];
        body.Collider.ContactEntered += _ => log.Add("enter");

        HookScene scene = new();
        scene.Add(new TileMap(TerrainGrid("....", "####")));
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

    // Without the collapse the host would replay each particle's last motion on every frame of the hold.
    [Fact]
    public void AHeldEmittersParticles_DrawWithTheirPreviousStateOnTheirCurrentState()
    {
        ParticleEmitter emitter = new(Frame(4, 4), capacity: 4)
        {
            Lifetime = new FloatRange(5f, 5f),
            Speed = new FloatRange(30f, 30f),
            AngularVelocity = new FloatRange(90f, 90f),
        };
        Entity root = new EntityHierarchyFixtures.Node(Vector2.Zero);
        root.Add(emitter);

        HookScene scene = new();
        using SceneSimulation simulation = Simulation(scene, root);
        emitter.Emit(4);

        simulation.Step(Step(0));
        Assert.All(simulation.View.Sprites.ToArray(), intent => Assert.NotEqual(intent.PreviousPosition, intent.Position));

        scene.Paused = true;
        simulation.Step(Step(1));

        SpriteIntent[] held = simulation.View.Sprites.ToArray();
        Assert.Equal(4, held.Length);
        Assert.All(held, intent =>
        {
            Assert.Equal(intent.Position, intent.PreviousPosition);
            Assert.Equal(intent.Rotation, intent.PreviousRotation);
        });
    }

    private static List<string> Stepped(params string[] names) => [.. names, .. names.Select(name => $"{name}.late")];
}
