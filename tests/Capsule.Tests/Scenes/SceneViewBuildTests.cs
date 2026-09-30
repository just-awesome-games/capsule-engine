using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

// The view is built on the first read after a step. A run that reads nothing draws nothing, and
// Draw is a pure write onto the view it is handed.
public sealed class SceneViewBuildTests
{
    [Fact]
    public void TheView_BuildsOnlyOnTheFirstReadAfterAStep()
    {
        Counting counting = new();
        SceneFixtures.Drifter drifter = new(Vector2.Zero);
        drifter.Add(counting);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero));
        scene.Add(drifter);
        using SceneSimulation simulation = new(scene);

        for (int tick = 0; tick < 5; tick++)
        {
            simulation.Step(SceneFixtures.Step(tick));
        }

        Assert.Equal(0, counting.Draws);

        FrameView first = simulation.View;
        Assert.Same(first, simulation.View);
        Assert.Equal(1, counting.Draws);

        simulation.Step(SceneFixtures.Step(5));
        Assert.Equal(1, counting.Draws);
        Assert.Equal(new Vector2(6f, 0f), Assert.Single(simulation.View.Sprites.ToArray()).Position);
        Assert.Equal(2, counting.Draws);
    }

    // Entities cross in Y under YSort, so the order a frame keeps moves every step. A view built only
    // at the end must equal one built after every step, as a presenting host builds it.
    [Fact]
    public void AViewReadOnlyAtTheEnd_EqualsOneReadAfterEveryStep()
    {
        using SceneSimulation everyStep = new(CrossingScene());
        using SceneSimulation atTheEnd = new(CrossingScene());

        for (int tick = 0; tick < 12; tick++)
        {
            everyStep.Step(SceneFixtures.Step(tick));
            _ = everyStep.View;
            atTheEnd.Step(SceneFixtures.Step(tick));
        }

        SpriteIntent[] expected = everyStep.View.Sprites.ToArray();
        Assert.Equal(3, expected.Length);
        Assert.Equal(expected, atTheEnd.View.Sprites.ToArray());
        Assert.Equal(everyStep.View.Metrics, atTheEnd.View.Metrics);
    }

    // A step after the last read leaves the view stale. Disposal keeps the frame last built and never
    // rebuilds it, and a simulation that built none has no frame to keep.
    [Fact]
    public void ADisposedSimulation_KeepsItsLastBuiltFrame_AndThrowsWhenItBuiltNone()
    {
        Counting counting = new();
        SceneFixtures.Drifter drifter = new(Vector2.Zero);
        drifter.Add(counting);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero));
        scene.Add(drifter);
        SceneSimulation built = new(scene);
        FrameView view = built.View;
        built.Step(SceneFixtures.Step());
        built.Dispose();

        Assert.Same(view, built.View);
        Assert.Equal(1, counting.Draws);

        SceneSimulation unread = new(new SceneFixtures.HookScene());
        unread.Dispose();

        ObjectDisposedException exception = Assert.Throws<ObjectDisposedException>(() => unread.View);
        Assert.Contains("Read View before Dispose", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingTheViewInsideADraw_Throws()
    {
        Reading reading = new();
        SceneFixtures.Drifter drifter = new(Vector2.Zero);
        drifter.Add(reading);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero));
        scene.Add(drifter);
        using SceneSimulation simulation = new(scene);
        reading.Simulation = simulation;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => simulation.View);
        Assert.Contains("inside a Renderer.Draw", exception.Message, StringComparison.Ordinal);
    }

    // A step reads the scene, never the frame. The frame shows a completed step, and none is
    // complete while one runs, whether or not a frame was built before it.
    [Fact]
    public void ReadingTheViewDuringAStep_Throws()
    {
        SceneSimulation? simulation = null;
        SceneFixtures.HookScene scene = new();
        scene.Add(new SceneFixtures.Watcher(observed => _ = simulation!.View));
        simulation = new SceneSimulation(scene);
        _ = simulation.View;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => simulation.Step(SceneFixtures.Step()));
        Assert.Contains("during a step", exception.Message, StringComparison.Ordinal);
    }

    // A Draw runs once per built frame, which is never in a headless run and once per presented
    // frame in a window. A change made there would make the two runs differ.
    [Theory]
    [InlineData(Change.OwnZIndex, "set a renderer's ZIndex")]
    [InlineData(Change.EntityZIndex, "set an entity's ZIndex")]
    [InlineData(Change.Attach, "attached a component")]
    [InlineData(Change.Detach, "detached a component")]
    [InlineData(Change.AddEntity, "added an entity")]
    [InlineData(Change.RemoveEntity, "removed an entity")]
    [InlineData(Change.YSort, "changed YSort")]
    public void ADrawThatChangesTheScene_ThrowsAndNamesTheFix(Change change, string named)
    {
        SceneFixtures.Drifter drifter = new(Vector2.Zero);
        drifter.Add(new Meddling(change));
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero));
        scene.Add(drifter);
        using SceneSimulation simulation = new(scene);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => simulation.View);
        Assert.Contains(named, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Make the change in a Step or LateStep", exception.Message, StringComparison.Ordinal);
    }

    public enum Change
    {
        OwnZIndex,
        EntityZIndex,
        Attach,
        Detach,
        AddEntity,
        RemoveEntity,
        YSort,
    }

    private static SceneFixtures.HookScene CrossingScene()
    {
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(new Vector2(0f, 20f)));
        scene.YSort = true;
        scene.Add(Crossing(0f, 3f, ColorRgba.Black));
        scene.Add(Crossing(20f, 0f, ColorRgba.White));
        scene.Add(Crossing(40f, -3f, new ColorRgba(255, 0, 0, 255)));

        return scene;
    }

    private static Climber Crossing(float y, float speed, ColorRgba color)
    {
        Climber climber = new(new Vector2(0f, y), speed);
        climber.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)) { Color = color });

        return climber;
    }

    private sealed class Climber(Vector2 position, float speed) : Entity(position)
    {
        protected internal override void OnStep(in StepContext context) => Position += new Vector2(0f, speed);
    }

    private sealed class Counting : Renderer
    {
        internal int Draws { get; private set; }

        protected internal override void Draw(FrameView view)
        {
            Draws++;
            Entity entity = Entity!;
            view.Add(new SpriteIntent(SceneFixtures.Frame(1, 1), entity.PreviousTransform.Position, entity.Position, 0f, 0f, Vector2.One, false, false, ColorRgba.White));
        }
    }

    private sealed class Reading : Renderer
    {
        internal SceneSimulation? Simulation { get; set; }

        protected internal override void Draw(FrameView view) => _ = Simulation!.View;
    }

    private sealed class Meddling(Change change) : Renderer
    {
        protected internal override void Draw(FrameView view)
        {
            Entity entity = Entity!;
            Scene scene = entity.Scene!;

            switch (change)
            {
                case Change.OwnZIndex:
                    ZIndex = 1;
                    break;
                case Change.EntityZIndex:
                    entity.ZIndex = 1;
                    break;
                case Change.Attach:
                    entity.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));
                    break;
                case Change.Detach:
                    entity.Remove(entity.Components[^1]);
                    break;
                case Change.AddEntity:
                    scene.Add(new SceneFixtures.Drifter(Vector2.Zero));
                    break;
                case Change.RemoveEntity:
                    scene.Remove(entity);
                    break;
                case Change.YSort:
                    scene.YSort = true;
                    break;
            }
        }
    }
}
