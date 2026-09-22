using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

public sealed class SceneVisibilityTests
{
    private static readonly Vector2 Span = new(10f, 10f);

    [Fact]
    public void AnUnconfinedCamera_SeesItsSpanAroundItsCentre()
    {
        SceneSimulation simulation = Run(SceneFixtures.Opens(new Vector2(100f, 50f), Span));

        Assert.Equal(new Rect(95f, 45f, 105f, 55f), simulation.Scene.Camera.VisibleRegion);
    }

    [Fact]
    public void ACameraPushedPastItsBounds_StopsAtTheEdgeItReached()
    {
        SceneSimulation simulation = Run(scene =>
        {
            SceneFixtures.Open(scene, new Vector2(100f, 0f), Span);
            scene.Camera.Bounds = new Rect(0f, -20f, 40f, 20f);
        });

        // Confined on X to the right edge, free on Y, and the centre itself is left as framed.
        Assert.Equal(new Rect(30f, -5f, 40f, 5f), simulation.Scene.Camera.VisibleRegion);
        Assert.Equal(new Vector2(100f, 0f), simulation.Scene.Camera.Center);
    }

    [Fact]
    public void ABoundsNarrowerThanTheSpan_CentresTheRegionOnItRatherThanPinningAnEdge()
    {
        SceneSimulation simulation = Run(scene =>
        {
            SceneFixtures.Open(scene, new Vector2(100f, 0f), Span);
            scene.Camera.Bounds = new Rect(0f, -20f, 4f, 20f);
        });

        Assert.Equal(new Rect(-3f, -5f, 7f, 5f), simulation.Scene.Camera.VisibleRegion);
    }

    [Fact]
    public void WithNoOutputOnTheStep_EveryFitResolvesToTheDeclaredSpan()
    {
        Rect letterboxed = Run(scene =>
        {
            SceneFixtures.Open(scene, Vector2.Zero, new Vector2(32f, 18f));
            scene.Camera.Fit = ViewportFit.Letterbox;
        }).Scene.Camera.VisibleRegion;

        Rect expanded = Run(scene =>
        {
            SceneFixtures.Open(scene, Vector2.Zero, new Vector2(32f, 18f));
            scene.Camera.Fit = ViewportFit.Expand;
        }).Scene.Camera.VisibleRegion;

        Assert.Equal(new Rect(-16f, -9f, 16f, 9f), letterboxed);
        Assert.Equal(letterboxed, expanded);
    }

    [Fact]
    public void WithAnOutputOnTheStep_ExpandWidensToItWhileLetterboxHoldsTheDeclaredSpan()
    {
        Vector2 output = new(1000f, 500f);

        Rect letterboxed = Run(scene =>
        {
            SceneFixtures.Open(scene, Vector2.Zero, new Vector2(32f, 18f));
            scene.Camera.Fit = ViewportFit.Letterbox;
        }, output).Scene.Camera.VisibleRegion;

        Rect expanded = Run(scene =>
        {
            SceneFixtures.Open(scene, Vector2.Zero, new Vector2(32f, 18f));
            scene.Camera.Fit = ViewportFit.Expand;
        }, output).Scene.Camera.VisibleRegion;

        Assert.Equal(new Rect(-16f, -9f, 16f, 9f), letterboxed);
        Assert.Equal(new Rect(-18f, -9f, 18f, 9f), expanded);
    }

    [Fact]
    public void ACameraThatHasNotLateStepped_SeesNothingYet()
    {
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        SceneSimulation simulation = new(scene);

        Assert.True(scene.Camera.VisibleRegion.IsEmpty);

        simulation.Step(SceneFixtures.Step());

        Assert.False(scene.Camera.VisibleRegion.IsEmpty);
    }

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

        // The camera reaches 16, so the region runs 11..21 and meets the marker's 20..21.
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

    // The entity is drawn by the frame the step it landed in rewrote, so its first step must read
    // that frame rather than the emptiness that preceded it.
    [Fact]
    public void ANotifierLandingWithTheStepsDeferredAdds_AnswersForTheFrameThatStepDrew()
    {
        List<string> log = [];
        List<bool> seen = [];
        ArrivingWatcher marker = new(Vector2.Zero, log, seen);

        void Spawn(Scene host, in StepContext context)
        {
            if (context.Tick == 0)
            {
                host.Add(marker);
            }
        }

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span), step: Spawn);
        SimulationHost run = new(scene);

        // It attaches after the settle the step ran, so the arrival settle is what enters it.
        run.Step();
        Assert.Equal(["entered"], log);
        Assert.Empty(seen);

        run.Step();
        Assert.Equal([true], seen);
        Assert.Equal(["entered"], log);
    }

    [Fact]
    public void ANotifierLandingOffScreen_ReadsFalseAndIsOwedNothing()
    {
        List<string> log = [];
        List<bool> seen = [];
        ArrivingWatcher marker = new(new Vector2(200f, 0f), log, seen);

        void Spawn(Scene host, in StepContext context)
        {
            if (context.Tick == 0)
            {
                host.Add(marker);
            }
        }

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span), step: Spawn);
        SimulationHost run = new(scene);

        run.Step();
        run.Step();

        Assert.Equal([false], seen);
        Assert.Empty(log);
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
    public void ANotifiersOffset_PlacesTheRectItWatches()
    {
        List<string> log = [];
        Watched marker = new(new Vector2(20f, 0f), log) { Notifier = { Offset = new Vector2(-16f, 0f) } };

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        scene.Add(marker);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        // The entity stands well outside the region; the offset rect at 4..5 does not.
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
    public void AHandlerThatResizesOrMovesItsOwnNotifier_IsRefused()
    {
        List<string> log = [];
        Watched watched = new(Vector2.Zero, log);
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        scene.Add(watched);

        Exception? sizeFailure = null;
        Exception? offsetFailure = null;
        watched.Notifier.ScreenEntered += () =>
        {
            sizeFailure = Record.Exception(() => watched.Notifier.Size = new Vector2(2f, 2f));
            offsetFailure = Record.Exception(() => watched.Notifier.Offset = new Vector2(1f, 0f));
        };

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.IsType<InvalidOperationException>(sizeFailure);
        Assert.IsType<InvalidOperationException>(offsetFailure);
        Assert.Equal(Vector2.One, watched.Notifier.Size);
        Assert.Equal(Vector2.Zero, watched.Notifier.Offset);
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

    [Fact]
    public void ANotifierSizedToNothing_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VisibleOnScreenNotifier2D(new Vector2(1f, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VisibleOnScreenNotifier2D(new Vector2(float.NaN, 1f)));
    }

    private static SceneSimulation Run(Action<Scene> open, Vector2 output = default)
    {
        SceneSimulation simulation = new(new SceneFixtures.HookScene(start: open));
        simulation.Step(SceneFixtures.Step(output: output));
        return simulation;
    }

    private class Watched : Entity
    {
        internal Watched(Vector2 position, List<string> log)
            : base(position)
        {
            Notifier = new VisibleOnScreenNotifier2D(Vector2.One);
            Notifier.ScreenEntered += () => log.Add("entered");
            Notifier.ScreenExited += () => log.Add("exited");
            Add(Notifier);
        }

        internal VisibleOnScreenNotifier2D Notifier { get; }
    }

    /// <summary>Records what its own notifier reads on each of its steps.</summary>
    private sealed class ArrivingWatcher(Vector2 position, List<string> log, List<bool> seen, Action<Scene>? onStart = null)
        : Watched(position, log)
    {
        protected internal override void OnStart() => onStart?.Invoke(Scene!);

        protected internal override void OnStep(in StepContext context) => seen.Add(Notifier.IsOnScreen);
    }

    private sealed class DriftingWatcher(Vector2 position, List<string> log) : Watched(position, log)
    {
        protected internal override void OnStep(in StepContext context) => Position += Vector2.UnitX;
    }
}
