using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Rendering;

namespace Capsule.Tests.Scenes;

public sealed class SceneVisibilityTests
{
    private static readonly Vector2 Span = new(10f, 10f);

    [Fact]
    public void AnUnconfinedCamera_SeesItsSpanAroundItsCentre()
    {
        SceneSimulation simulation = Run(SceneFixtures.Opens(new Vector2(100f, 50f), Span));

        Assert.Equal(new ViewBounds(95f, 45f, 105f, 55f), simulation.Scene.Camera.VisibleRegion);
    }

    [Fact]
    public void ACameraPushedPastItsBounds_StopsAtTheEdgeItReached()
    {
        SceneSimulation simulation = Run(scene =>
        {
            SceneFixtures.Open(scene, new Vector2(100f, 0f), Span);
            scene.Camera.Bounds = new ViewBounds(0f, -20f, 40f, 20f);
        });

        // Confined on X to the right edge, free on Y, and the centre itself is left as framed.
        Assert.Equal(new ViewBounds(30f, -5f, 40f, 5f), simulation.Scene.Camera.VisibleRegion);
        Assert.Equal(new Vector2(100f, 0f), simulation.Scene.Camera.Center);
    }

    [Fact]
    public void ABoundsNarrowerThanTheSpan_CentresTheRegionOnItRatherThanPinningAnEdge()
    {
        SceneSimulation simulation = Run(scene =>
        {
            SceneFixtures.Open(scene, new Vector2(100f, 0f), Span);
            scene.Camera.Bounds = new ViewBounds(0f, -20f, 4f, 20f);
        });

        Assert.Equal(new ViewBounds(-3f, -5f, 7f, 5f), simulation.Scene.Camera.VisibleRegion);
    }

    [Fact]
    public void EveryFit_ResolvesToTheDeclaredSpan_BecauseNoOutputReachesTheSimulation()
    {
        ViewBounds letterboxed = Run(scene =>
        {
            SceneFixtures.Open(scene, Vector2.Zero, new Vector2(32f, 18f));
            scene.Camera.Fit = ViewportFit.Letterbox;
        }).Scene.Camera.VisibleRegion;

        ViewBounds expanded = Run(scene =>
        {
            SceneFixtures.Open(scene, Vector2.Zero, new Vector2(32f, 18f));
            scene.Camera.Fit = ViewportFit.Expand;
        }).Scene.Camera.VisibleRegion;

        Assert.Equal(new ViewBounds(-16f, -9f, 16f, 9f), letterboxed);
        Assert.Equal(letterboxed, expanded);
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
    public void ACameraSpanningNothing_SeesNothing()
    {
        SceneSimulation simulation = Run(scene => scene.Camera.Center = new Vector2(100f, 50f));

        Assert.True(simulation.Scene.Camera.VisibleRegion.IsEmpty);
    }

    [Fact]
    public void ANotifierTheCameraSweepsOnto_EntersTheScreenOnTheStepThatFramedIt()
    {
        List<string> log = [];
        Marker marker = new(new Vector2(20f, 0f), log);

        static void Pan(Scene scene, in StepContext context) => scene.Camera.Center += new Vector2(8f, 0f);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span), lateStep: Pan);
        scene.Add(marker);

        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());
        Assert.Empty(log);
        Assert.False(marker.Notifier.IsOnScreen);

        // The camera reaches 16, so the region runs 11..21 and meets the marker's 20..21.
        simulation.Step(SceneFixtures.Step(1));
        Assert.Equal(["entered"], log);
        Assert.True(marker.Notifier.IsOnScreen);
    }

    [Fact]
    public void ANotifierReadDuringItsOwnStep_DescribesTheFrameThatWasDrawn()
    {
        List<bool> seen = [];
        Marker marker = new(new Vector2(20f, 0f), []);

        static void Pan(Scene scene, in StepContext context) => scene.Camera.Center += new Vector2(8f, 0f);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span), lateStep: Pan);
        scene.Add(marker);
        scene.Add(new SceneFixtures.Watcher(_ => seen.Add(marker.Notifier.IsOnScreen)));

        SceneSimulation simulation = new(scene);
        for (long tick = 0; tick < 3; tick++)
        {
            simulation.Step(SceneFixtures.Step(tick));
        }

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
        ArrivingMarker marker = new(Vector2.Zero, log, seen);

        void Spawn(Scene host, in StepContext context)
        {
            if (context.Tick == 0)
            {
                host.Add(marker);
            }
        }

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span), step: Spawn);
        SceneSimulation simulation = new(scene);

        // It attaches after the settle the step ran, so the arrival settle is what enters it.
        simulation.Step(SceneFixtures.Step());
        Assert.Equal(["entered"], log);
        Assert.Empty(seen);

        simulation.Step(SceneFixtures.Step(1));
        Assert.Equal([true], seen);
        Assert.Equal(["entered"], log);
    }

    [Fact]
    public void ANotifierLandingOffScreen_ReadsFalseAndIsOwedNothing()
    {
        List<string> log = [];
        List<bool> seen = [];
        ArrivingMarker marker = new(new Vector2(200f, 0f), log, seen);

        void Spawn(Scene host, in StepContext context)
        {
            if (context.Tick == 0)
            {
                host.Add(marker);
            }
        }

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span), step: Spawn);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal([false], seen);
        Assert.Empty(log);
    }

    // The arrivals answer for the frame the step drew, so a camera an arriving entity's OnStart
    // installs — whose own region is empty until its first late step — must not answer for them.
    [Fact]
    public void ANotifierLandingBesideACameraInstalledOnStart_StillAnswersForTheFrameThatStepDrew()
    {
        List<string> log = [];
        List<bool> seen = [];

        static void Reframe(Scene host)
        {
            Camera replacement = new() { Center = host.Camera.Center, ViewportSize = host.Camera.ViewportSize };
            ((SceneFixtures.HookScene)host).Install(replacement);
        }

        ArrivingMarker marker = new(Vector2.Zero, log, seen, Reframe);

        void Spawn(Scene host, in StepContext context)
        {
            if (context.Tick == 0)
            {
                host.Add(marker);
            }
        }

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span), step: Spawn);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());
        Assert.True(scene.Camera.VisibleRegion.IsEmpty);
        Assert.Equal(["entered"], log);

        simulation.Step(SceneFixtures.Step(1));
        Assert.Equal([true], seen);
    }

    [Fact]
    public void ANotifierAttachedFromAnArrivalsHandler_FirstSettlesOnTheNextStep()
    {
        List<string> log = [];
        List<string> lateLog = [];
        Marker marker = new(Vector2.Zero, log);

        VisibleOnScreenNotifier2D late = new(Vector2.One);
        late.ScreenEntered += () => lateLog.Add("entered");
        marker.Notifier.ScreenEntered += () => marker.Add(late);

        void Spawn(Scene host, in StepContext context)
        {
            if (context.Tick == 0)
            {
                host.Add(marker);
            }
        }

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span), step: Spawn);
        SceneSimulation simulation = new(scene);

        // The arrival settle owns the entries that landed with the adds, and stops at them.
        simulation.Step(SceneFixtures.Step());
        Assert.Equal(["entered"], log);
        Assert.Empty(lateLog);
        Assert.False(late.IsOnScreen);

        simulation.Step(SceneFixtures.Step(1));
        Assert.Equal(["entered"], lateLog);
        Assert.True(late.IsOnScreen);
    }

    [Fact]
    public void ANotifierDriftingOffTheEdge_ExitsTheScreen()
    {
        List<string> log = [];
        DriftingMarker marker = new(new Vector2(2f, 0f), log);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        scene.Add(marker);

        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());
        Assert.Equal(["entered"], log);

        // The rect ends flush with the region's right edge, still inside it.
        simulation.Step(SceneFixtures.Step(1));
        Assert.Equal(["entered"], log);

        // And now it starts on that edge, which the open region excludes.
        simulation.Step(SceneFixtures.Step(2));
        Assert.Equal(["entered", "exited"], log);
        Assert.False(marker.Notifier.IsOnScreen);
    }

    [Fact]
    public void ANotifierTakenOutOfItsSceneWhileOnScreen_IsOwedItsExit()
    {
        List<string> log = [];
        Marker marker = new(Vector2.Zero, log);

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
        Marker marker = new(new Vector2(20f, 0f), log) { Notifier = { Offset = new Vector2(-16f, 0f) } };

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        scene.Add(marker);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        // The entity stands well outside the region; the offset rect at 4..5 does not.
        Assert.Equal(["entered"], log);
    }

    // The settle walks a list its handlers may shrink by more than one entry: a guard that only
    // held the cursor still against a single removal left the traversal past the end of the list,
    // and the notifier behind the detached pair kept a stale IsOnScreen for the rest of the run.
    [Fact]
    public void ANotifierHandlerThatDetachesItsOwnAndAnEarlierNotifier_LeavesTheRestSettlingThatStep()
    {
        List<string> firstLog = [];
        List<string> secondLog = [];
        List<string> thirdLog = [];

        Marker first = new(Vector2.Zero, firstLog);
        Marker second = new(Vector2.Zero, secondLog);
        Marker third = new(Vector2.Zero, thirdLog);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        scene.Add(first);
        scene.Add(second);
        scene.Add(third);

        second.Notifier.ScreenEntered += () =>
        {
            first.Remove(first.Notifier);
            second.Remove(second.Notifier);
        };

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        // Both detached notifiers were on screen, so both are owed their exit.
        Assert.Equal(["entered", "exited"], firstLog);
        Assert.Equal(["entered", "exited"], secondLog);
        Assert.Equal(["entered"], thirdLog);
        Assert.True(third.Notifier.IsOnScreen);
    }

    // The mirror case: the handler takes out a notifier the settle has not reached yet, which must
    // neither settle nor cost the one behind it its turn.
    [Fact]
    public void ANotifierHandlerThatDetachesALaterNotifier_SettlesTheRestExactlyOnce()
    {
        List<string> firstLog = [];
        List<string> secondLog = [];
        List<string> thirdLog = [];

        Marker first = new(Vector2.Zero, firstLog);
        Marker second = new(Vector2.Zero, secondLog);
        Marker third = new(Vector2.Zero, thirdLog);

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

    [Fact]
    public void ANotifierSizedToNothing_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VisibleOnScreenNotifier2D(new Vector2(1f, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VisibleOnScreenNotifier2D(new Vector2(float.NaN, 1f)));
    }

    private static SceneSimulation Run(Action<Scene> open)
    {
        SceneSimulation simulation = new(new SceneFixtures.HookScene(start: open));
        simulation.Step(SceneFixtures.Step());
        return simulation;
    }

    private class Marker : Entity
    {
        internal Marker(Vector2 position, List<string> log)
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
    private sealed class ArrivingMarker(Vector2 position, List<string> log, List<bool> seen, Action<Scene>? onStart = null)
        : Marker(position, log)
    {
        protected internal override void OnStart() => onStart?.Invoke(Scene!);

        protected internal override void OnStep(in StepContext context) => seen.Add(Notifier.IsOnScreen);
    }

    private sealed class DriftingMarker(Vector2 position, List<string> log) : Marker(position, log)
    {
        protected internal override void OnStep(in StepContext context) => Position += Vector2.UnitX;
    }
}
