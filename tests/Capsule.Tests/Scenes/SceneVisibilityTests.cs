using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

public sealed class SceneVisibilityTests
{
    private static readonly Vector2 Span = new(10f, 10f);

    [Fact]
    public void ANotifierTheCameraSweepsOnto_EntersTheScreenOnTheStepThatFramedIt()
    {
        List<string> log = [];
        Watched marker = new(new Vector2(20f, 0f), log);

        static void Pan(Scene scene, in StepContext context) => scene.Camera.Center += new Vector2(8f, 0f);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span), lateStep: Pan);
        scene.Add(marker);

        SimulationHost run = new(scene);

        run.Step();
        Assert.Empty(log);
        Assert.False(marker.Notifier.IsOnScreen);

        // The camera reaches 16. The region runs 11..21 and meets the marker's 20..21.
        run.Step();
        Assert.Equal(["entered"], log);
        Assert.True(marker.Notifier.IsOnScreen);
    }

    [Fact]
    public void ANotifierReadDuringItsOwnStep_DescribesTheFrameThatWasDrawn()
    {
        List<bool> seen = [];
        Watched marker = new(new Vector2(20f, 0f), []);

        static void Pan(Scene scene, in StepContext context) => scene.Camera.Center += new Vector2(8f, 0f);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span), lateStep: Pan);
        scene.Add(marker);
        scene.Add(new SceneFixtures.Watcher(_ => seen.Add(marker.Notifier.IsOnScreen)));

        using SimulationHost run = new(scene);
        run.Step(3);

        // The step that brought the marker on screen reports it to the step after it, never to its own.
        Assert.Equal([false, false, true], seen);
    }

    // Its first step reads the frame the step it landed in drew, or the last one when it joins between steps. A held
    // landing, in a step that began paused and lifts the pause as a cutscene handing control back does, settles
    // before its entity first steps.
    [Theory]
    [InlineData(0f, true, false, false)]
    [InlineData(200f, false, false, false)]
    [InlineData(0f, true, true, false)]
    [InlineData(200f, false, true, false)]
    [InlineData(0f, true, false, true)]
    public void ANotifierLandingWithTheStepsDeferredAdds_AnswersForTheFrameThatStepDrew(float x, bool onScreen, bool betweenSteps, bool held)
    {
        List<string> log = [];
        List<bool> seen = [];
        ArrivingWatcher marker = new(new Vector2(x, 0f), log, seen);

        void Spawn(Scene host, in StepContext context)
        {
            if (context.Tick == 0 && !betweenSteps)
            {
                host.Add(marker);
                host.Paused = false;
            }
        }

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span), step: Spawn) { Paused = held };
        SimulationHost run = new(scene);

        string[] entered = onScreen ? ["entered"] : [];

        // Its arrival settle enters it, after the step's drain or, between steps or held, as the next step begins.
        run.Step();
        if (betweenSteps)
        {
            scene.Add(marker);
        }

        Assert.Equal(betweenSteps || held ? [] : entered, log);
        Assert.Empty(seen);

        run.Step();
        Assert.Equal([onScreen], seen);
        Assert.Equal(entered, log);
    }

    [Fact]
    public void ANotifierDriftingOffTheEdge_ExitsTheScreen()
    {
        List<string> log = [];
        DriftingWatcher marker = new(new Vector2(2f, 0f), log);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        scene.Add(marker);

        SimulationHost run = new(scene);

        run.Step();
        Assert.Equal(["entered"], log);

        // The rect ends flush with the region's right edge, still inside it.
        run.Step();
        Assert.Equal(["entered"], log);

        // And now it starts on that edge, which the open region excludes.
        run.Step();
        Assert.Equal(["entered", "exited"], log);
        Assert.False(marker.Notifier.IsOnScreen);
    }

    [Fact]
    public void ANotifierTakenOutOfItsSceneWhileOnScreen_IsOwedItsExit()
    {
        List<string> log = [];
        Watched marker = new(Vector2.Zero, log);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        scene.Add(marker);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["entered"], log);

        scene.Remove(marker);

        Assert.Equal(["entered", "exited"], log);
        Assert.False(marker.Notifier.IsOnScreen);
    }

    [Fact]
    public void ANotifiersRect_PlacesWhatItWatches()
    {
        List<string> log = [];
        Watched marker = new(new Vector2(20f, 0f), log) { Notifier = { Rect = new Rect(new Vector2(-16f, 0f), Vector2.One) } };

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        scene.Add(marker);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        // The entity stands well outside the region. The rect, at 4..5, does not.
        Assert.Equal(["entered"], log);
    }

    [Fact]
    public void AMirroredAncestor_MirrorsTheRectAboutTheEntity()
    {
        List<string> log = [];
        EntityHierarchyFixtures.Node facingLeft = new(new Vector2(6f, 0f)) { Scale = new Vector2(-1f, 1f) };
        Watched marker = new(Vector2.Zero, log) { Notifier = { Rect = new Rect(new Vector2(2f, 0f), Vector2.One) }, Parent = facingLeft };

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        scene.Add(facingLeft);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        // Unmirrored, the rect would sit at 8..9 past the region's edge at 5. Mirrored, it spans 3..4.
        Assert.Equal(["entered"], log);
    }

    // The canonical detach during a settle: a handler takes a notifier the walk has not reached out
    // of the scene, which must neither settle nor cost the one behind it its turn.
    [Fact]
    public void ANotifierHandlerThatDetachesALaterNotifier_SettlesTheRestExactlyOnce()
    {
        List<string> firstLog = [];
        List<string> secondLog = [];
        List<string> thirdLog = [];

        Watched first = new(Vector2.Zero, firstLog);
        Watched second = new(Vector2.Zero, secondLog);
        Watched third = new(Vector2.Zero, thirdLog);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        scene.Add(first);
        scene.Add(second);
        scene.Add(third);

        first.Notifier.ScreenEntered += () => second.Remove(second.Notifier);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["entered"], firstLog);
        Assert.Empty(secondLog);
        Assert.False(second.Notifier.IsOnScreen);
        Assert.Equal(["entered"], thirdLog);
    }

    // A handler sees the new state and may not reconfigure the notifier it is running for.
    [Fact]
    public void AHandlerThatChangesItsOwnNotifier_IsRefused()
    {
        List<string> log = [];
        Watched watched = new(Vector2.Zero, log);
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        scene.Add(watched);

        Exception? rectFailure = null;
        Exception? marginFailure = null;
        watched.Notifier.ScreenEntered += () =>
        {
            rectFailure = Record.Exception(() => watched.Notifier.Rect = new Rect(Vector2.One, Vector2.One));
            marginFailure = Record.Exception(() => watched.Notifier.Margin = Vector2.One);
        };

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.IsType<InvalidOperationException>(rectFailure);
        Assert.IsType<InvalidOperationException>(marginFailure);
        Assert.Equal(new Rect(Vector2.Zero, Vector2.One), watched.Notifier.Rect);
        Assert.Equal(Vector2.Zero, watched.Notifier.Margin);
        Assert.Equal(["entered"], log);
    }

    [Fact]
    public void ANotifierPastTheDeclaredSpan_EntersOnlyOnceTheStepWidensTheOutput()
    {
        List<string> log = [];
        Watched marker = new(new Vector2(17f, 0f), log);

        SceneFixtures.HookScene scene = new(start: scene =>
        {
            SceneFixtures.Open(scene, Vector2.Zero, new Vector2(32f, 18f));
            scene.Camera.Fit = ViewportFit.Expand;
        });
        scene.Add(marker);

        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());
        Assert.Empty(log);
        Assert.False(marker.Notifier.IsOnScreen);

        simulation.Step(SceneFixtures.Step(output: new Vector2(1000f, 500f)));
        Assert.Equal(["entered"], log);
        Assert.True(marker.Notifier.IsOnScreen);
    }

    // A rect with no extent on an axis is a line there, a point on both, on screen only strictly inside the region.
    [Theory]
    [InlineData(4.5f, 0f, true)]
    [InlineData(5f, 0f, false)]
    [InlineData(-5f, 0f, false)]
    [InlineData(4.5f, 20f, true)]
    [InlineData(5f, 20f, false)]
    public void AnAxisWithNoExtent_IsOnScreenOnlyStrictlyInside(float x, float height, bool onScreen)
    {
        VisibleOnScreenNotifier2D notifier = new() { Rect = new Rect(new Vector2(0f, -height / 2f), new Vector2(0f, height)) };

        Assert.Equal(onScreen, Settles(new Vector2(x, 0f), notifier, Span));
    }

    // The margin grows the region in world units, by its X on the left and right and its Y above and below. It never
    // scales with the entity, and it never makes an empty region visible.
    [Theory]
    [InlineData(-8f, 0f, 3f, 0f, 1f, 10f, false)]
    [InlineData(-8f, 0f, 3.5f, 0f, 1f, 10f, true)]
    [InlineData(-8f, 0f, 0f, 10f, 1f, 10f, false)]
    [InlineData(0f, -8f, 0f, 3.5f, 1f, 10f, true)]
    [InlineData(-8f, 0f, 2f, 0f, 2f, 10f, false)]
    [InlineData(0f, 0f, 100f, 100f, 1f, 0f, false)]
    public void AMargin_GrowsTheRegionTheRectMeets(float x, float y, float horizontal, float vertical, float scale, float view, bool onScreen)
    {
        VisibleOnScreenNotifier2D notifier = new() { Margin = new Vector2(horizontal, vertical) };

        Assert.Equal(onScreen, Settles(new Vector2(x, y), notifier, new Vector2(view), scale));
    }

    [Fact]
    public void ACrossedOrNonFiniteRect_OrANegativeMargin_IsRefused()
    {
        VisibleOnScreenNotifier2D notifier = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => notifier.Rect = Rect.FromEdges(1f, 0f, 0f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => notifier.Rect = Rect.FromEdges(float.NaN, 0f, 1f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => notifier.Margin = new Vector2(0f, -1f));
    }

    // Whether the notifier is on screen after a step on an entity at at, scaled, under a camera at the origin.
    private static bool Settles(Vector2 at, VisibleOnScreenNotifier2D notifier, Vector2 view, float scale = 1f)
    {
        EntityHierarchyFixtures.Node entity = new(at) { Scale = new Vector2(scale) };
        entity.Add(notifier);
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, view));
        scene.Add(entity);
        new SceneSimulation(scene).Step(SceneFixtures.Step());

        return notifier.IsOnScreen;
    }

    private class Watched : Entity
    {
        internal VisibleOnScreenNotifier2D Notifier { get; }

        internal Watched(Vector2 position, List<string> log)
            : base(position)
        {
            Notifier = new VisibleOnScreenNotifier2D { Rect = new Rect(Vector2.Zero, Vector2.One) };
            Notifier.ScreenEntered += () => log.Add("entered");
            Notifier.ScreenExited += () => log.Add("exited");
            Add(Notifier);
        }
    }

    /// <summary>Records what its own notifier reads on each of its steps.</summary>
    private sealed class ArrivingWatcher(Vector2 position, List<string> log, List<bool> seen) : Watched(position, log)
    {
        protected internal override void OnStep(in StepContext context) => seen.Add(Notifier.IsOnScreen);
    }

    private sealed class DriftingWatcher(Vector2 position, List<string> log) : Watched(position, log)
    {
        protected internal override void OnStep(in StepContext context) => Position += Vector2.UnitX;
    }
}
