using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Allocation;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Scenes;

public sealed class SceneMembershipTests
{
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
    public void AnEntityAlreadyInAScene_CannotBeAddedTwice()
    {
        SceneFixtures.Drifter drifter = new();
        SceneSimulation simulation = Simulation(new SceneFixtures.HookScene(), drifter);

        Assert.Throws<InvalidOperationException>(() => simulation.Scene.Add(drifter));
    }

    // Indexed by value, one twin would be read as the other already queued or held.
    [Fact]
    public void MembershipIsReferenceIdentity_NeverAnEntityEqualsOverride()
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

        scene.Remove(second);

        Assert.Same(first, Assert.Single(scene.Entities.ToArray()));
        Assert.Same(scene, first.Scene);
        Assert.Equal(["second-"], log);
    }

    [Fact]
    public void TwoRunsWithTheSameInput_EndInIdenticalEntityPositionsAndSprites()
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
                Assert.Equal(entities1[i].PreviousTransform.Position, entities2[i].PreviousTransform.Position);
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
}
