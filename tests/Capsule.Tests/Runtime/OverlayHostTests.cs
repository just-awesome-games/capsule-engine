using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Runtime;

// A failed menu action logs, and the sink is one process-wide slot.
[Collection(LogSinkCollection.Name)]
public sealed class OverlayHostTests
{
    private const double StepSeconds = 0.1;
    private const string NamedDocument = "levels/named";
    private static readonly InputAction SharedAction = new("shared");
    private static readonly InputAction SpaceAction = new("space");
    private static readonly InputAction RightAction = new("right");

    [Fact]
    public void LeadingEdgeTogglesAndQuarantinesTheBoundButtonUntilRelease()
    {
        FixedStepScheduler scheduler = CreateScheduler();
        OverlayHost overlay = new(Key.Grave, scheduler, new RecordingSimulation());

        DeviceSnapshot snapshot = DeviceSnapshot.Of(Key.Grave, Key.Space);
        snapshot = overlay.Observe(snapshot);

        Assert.True(overlay.IsOpen);
        Assert.True(scheduler.Held);
        Assert.False(snapshot.IsDown(Key.Grave));
        Assert.True(snapshot.IsDown(Key.Space));

        snapshot = DeviceSnapshot.Of(Key.Grave, Key.Space);
        snapshot = overlay.Observe(snapshot);
        Assert.True(overlay.IsOpen);
        Assert.False(snapshot.IsDown(Key.Grave));

        snapshot = DeviceSnapshot.Of(Key.Space);
        snapshot = overlay.Observe(snapshot);
        Assert.True(overlay.IsOpen);
        Assert.True(snapshot.IsDown(Key.Space));

        snapshot = DeviceSnapshot.Of(Key.Grave);
        snapshot = overlay.Observe(snapshot);

        Assert.False(overlay.IsOpen);
        Assert.False(scheduler.Held);
        Assert.False(snapshot.IsDown(Key.Grave));
    }

    [Fact]
    public void QuarantineWinsOverAGameBindingOfTheSameButton()
    {
        RecordingSimulation simulation = new(SharedAction, SpaceAction, RightAction);
        FixedStepScheduler scheduler = new(
            StepSeconds,
            5,
            new ActionBindings().Bind(SharedAction, Key.Grave));
        OverlayHost overlay = new(Key.Grave, scheduler, simulation);

        DeviceSnapshot opening = DeviceSnapshot.Of(Key.Grave);
        opening = overlay.Observe(opening);
        scheduler.Advance(StepSeconds, opening, simulation);

        DeviceSnapshot release = DeviceSnapshot.Empty;
        release = overlay.Observe(release);
        scheduler.Advance(StepSeconds, release, simulation);

        DeviceSnapshot closing = DeviceSnapshot.Of(Key.Grave);
        closing = overlay.Observe(closing);
        scheduler.Advance(StepSeconds, closing, simulation);

        RecordedStep step = Assert.Single(simulation.Recorded);
        Assert.False(step.First.Pressed);
        Assert.False((step.First.Held || step.Second.Held));
    }

    [Fact]
    public void ReboundPadButtonOpensAndIsQuarantined()
    {
        FixedStepScheduler scheduler = CreateScheduler();
        OverlayHost overlay = new((InputButton)PadButton.South, scheduler, new RecordingSimulation());
        DeviceSnapshot snapshot = DeviceSnapshot.Empty.With(PadButton.South).With(Key.Space);

        snapshot = overlay.Observe(snapshot);

        Assert.True(overlay.IsOpen);
        Assert.True(scheduler.Held);
        Assert.False(snapshot.IsDown(PadButton.South));
        Assert.True(snapshot.IsDown(Key.Space));
    }

    [Fact]
    public void NoneNeverOpensOrChangesTheSnapshot()
    {
        FixedStepScheduler scheduler = CreateScheduler();
        OverlayHost overlay = new(InputButton.None, scheduler, new RecordingSimulation());
        DeviceSnapshot snapshot = DeviceSnapshot.Of(Key.Grave);

        snapshot = overlay.Observe(snapshot);

        Assert.False(overlay.IsOpen);
        Assert.False(scheduler.Held);
        Assert.Equal(DeviceSnapshot.Of(Key.Grave), snapshot);
    }

    [Fact]
    public void ClosedOverlay_DoesNotStepItsHost()
    {
        using OverlayHost overlay = new(Key.Grave, CreateScheduler(), new RecordingSimulation());

        overlay.Step();

        Assert.False(overlay.IsOpen);
        Assert.Equal(0, overlay.Host.Tick);
    }

    [Fact]
    public void Refit_ChangesOnlyTheOverlayRunAtOneAndTwoTimes()
    {
        Run gameRun = new() { Canvas = new System.Numerics.Vector2(100f, 50f) };
        using SceneHost game = CreateHost(gameRun);
        using OverlayHost overlay = new(Key.Grave, CreateScheduler(), game, game);

        overlay.Refit((640, 720));

        Assert.Equal(new System.Numerics.Vector2(640f, 720f), overlay.Host.Run.Canvas);
        Assert.Same(gameRun, game.Run);
        Assert.NotSame(game.Run, overlay.Host.Run);

        overlay.Refit((1280, 1080));

        Assert.Equal(new System.Numerics.Vector2(640f, 540f), overlay.Host.Run.Canvas);
        Assert.Equal(new System.Numerics.Vector2(100f, 50f), game.Run.Canvas);
    }

    [Fact]
    public void ReadoutUsesTheCurrentSceneAndTickAndCachesUnchangedText()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        DeviceSnapshot opening = DeviceSnapshot.Of(Key.Grave);
        opening = overlay.Observe(opening);
        scheduler.Advance(StepSeconds, opening, host);

        Assert.Equal("ReadoutScene  tick 0", overlay.Readout);
        string cached = overlay.Readout;
        Assert.Same(cached, overlay.Readout);

        DeviceSnapshot release = DeviceSnapshot.Empty;
        release = overlay.Observe(release);
        DeviceSnapshot closing = DeviceSnapshot.Of(Key.Grave);
        closing = overlay.Observe(closing);
        scheduler.Advance(StepSeconds, closing, host);

        Assert.Equal("ReadoutScene  tick 1", overlay.Readout);
    }

    [Fact]
    public void TheMainMenu_ListsTheEngineEntriesForARunOfScenesWithEachHotkeyInASharedColumn()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);

        Assert.Equal(
            ["Scene", "Step", "Debug Draw", "Time Scale", "Restart", "Load Scene", "Frame Pane", "Hide", "Exit"],
            Labels(scene));
        Assert.Equal(0, scene.FocusedIndex);
    }

    // An opener's hotkey is the root menu's alone: inside a submenu it changes nothing, not even
    // the status line Debug Draw would otherwise write about a scene that has emitted nothing.
    // Exit acts on the run from any depth.
    [Fact]
    public void TheHotkeys_OpenMenusOnlyFromTheRootAndExitThroughTheRunAtAnyDepth()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.L);

        Assert.Equal("Load Scene", overlay.Scene.Title);
        Assert.Equal(2, overlay.Scene.Depth);

        Press(overlay, scheduler, host, Key.L);
        Press(overlay, scheduler, host, Key.D);
        Press(overlay, scheduler, host, Key.T);

        Assert.Equal("Load Scene", overlay.Scene.Title);
        Assert.Equal(2, overlay.Scene.Depth);
        Assert.Equal(string.Empty, overlay.Scene.Status);

        Press(overlay, scheduler, host, Key.E);

        Assert.True(host.ExitRequested);
        Assert.Equal(1, scheduler.Tick);
    }

    [Fact]
    public void NavigatingDownAndEnter_ActivatesTheFocusedEntry()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Assert.Equal(0, overlay.Scene.FocusedIndex);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Down));

        Assert.Equal(1, overlay.Scene.FocusedIndex);
        Assert.Equal(0, scheduler.Tick);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Enter));

        Assert.Equal(1, scheduler.Tick);
        Assert.True(scheduler.Held);
        Assert.True(overlay.IsOpen);
    }

    // The game's pointer is in its canvas, letterboxed and scaled into the window; the overlay's
    // canvas is the window at its own integer scale. A pointer over the second row in window terms
    // must focus that row, and the game must still see the pointer it was handed.
    [Fact]
    public void APointerOverARow_FocusesItThroughTheGamesPlacementAndLeavesTheGamesPointerAlone()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        ScreenPlacement gameLayer = new(new System.Numerics.Vector2(100f, 20f), 3f);
        const int overlayScale = 2;

        overlay.Observe(DeviceSnapshot.Of(Key.Grave), gameLayer, overlayScale);
        scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, host);
        overlay.Step();
        Assert.True(overlay.IsOpen);

        // The second row's box in overlay canvas pixels, then that point in the window, then the
        // game canvas position the host would have sampled for it.
        float lineHeight = BitmapFont.Default.LineHeight;
        System.Numerics.Vector2 overlayPoint = new(6f, 4f + (3f * lineHeight) + (lineHeight / 2f));
        System.Numerics.Vector2 window = overlayPoint * overlayScale;
        System.Numerics.Vector2 gamePoint = (window - gameLayer.Origin) / gameLayer.Scale;

        DeviceSnapshot game = overlay.Observe(DeviceSnapshot.Empty.WithPointer(gamePoint), gameLayer, overlayScale);
        scheduler.Advance(StepSeconds, game, host);
        overlay.Step();

        Assert.Equal(gamePoint, game.Pointer);
        Assert.Equal(1, overlay.Scene.FocusedIndex);
        Assert.Equal("Step", Labels(overlay.Scene)[overlay.Scene.FocusedIndex]);
    }

    [Fact]
    public void Restart_ReplacesTheSceneInExactlyOneTickAndStaysHeld()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        Scene before = host.Scene;

        Open(overlay, scheduler, host);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.R));

        Assert.NotSame(before, host.Scene);
        Assert.IsType<ReadoutScene>(host.Scene);
        Assert.Equal(1, scheduler.Tick);
        Assert.True(scheduler.Held);
        Assert.Equal("ReadoutScene  tick 1", overlay.Readout);
    }

    [Fact]
    public void LoadSceneSubmenu_ListsTheRegisteredClassesSortedRequestsEachByItsRegisteredFormAndStaysOpen()
    {
        List<SceneTransition> resolved = [];
        using SceneHost host = CreateHost(resolved: resolved);
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal(["NamedScene", "PayloadScene", "PlainScene"], Labels(overlay.Scene));
        Assert.Equal("Load Scene", overlay.Scene.Title);
        Assert.Equal(2, overlay.Scene.Depth);
        Assert.Equal(0, overlay.Scene.FocusedIndex);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Enter));

        Assert.IsType<NamedScene>(host.Scene);
        Assert.Equal(2, resolved.Count);
        SceneTransition named = resolved[^1];
        Assert.Equal(SceneTransitionKind.Named, named.Kind);
        Assert.Equal(NamedDocument, named.DocumentName);
        Assert.Null(named.Payload);
        Assert.Equal(1, scheduler.Tick);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);

        Assert.Equal(2, overlay.Scene.Depth);
        Assert.Equal("NamedScene  tick 1", overlay.Readout);

        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.IsType<PlainScene>(host.Scene);
        Assert.Equal(SceneTransitionKind.Scene, resolved[^1].Kind);
        Assert.Equal(typeof(PlainScene), resolved[^1].SceneType);
        Assert.Equal(2, overlay.Scene.Depth);
    }

    [Fact]
    public void Back_PopsTheSubmenuAndReturnsTheFocusToTheItemThatOpenedIt()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Press(overlay, scheduler, host, Key.Down);

        Assert.Equal("Load Scene", scene.Title);
        Assert.Equal(1, scene.FocusedIndex);

        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal(1, scene.Depth);
        Assert.Equal(string.Empty, scene.Title);
        Assert.Equal("Load Scene", Labels(scene)[scene.FocusedIndex]);

        Press(overlay, scheduler, host, Key.Left);

        Assert.Equal(1, scene.Depth);
        Assert.Equal(0, scheduler.Tick);
    }

    // The hold's edges are the host's to hear: taken on open, kept through hide and either way
    // back from it, let go on close.
    [Fact]
    public void TheHold_ReportsItsEdgesOnOpenAndCloseAndNotOnHideOrTheReturnFromIt()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        List<bool> edges = [];
        overlay.HoldChanged = edges.Add;

        Open(overlay, scheduler, host);
        Assert.Equal([true], edges);

        Press(overlay, scheduler, host, Key.H);
        Assert.True(overlay.IsHidden);
        Assert.Equal([true], edges);

        Press(overlay, scheduler, host, Key.Grave);
        Assert.True(overlay.IsOpen);
        Assert.True(scheduler.Held);
        Assert.Equal([true], edges);

        Press(overlay, scheduler, host, Key.H);
        Assert.True(overlay.IsHidden);
        Press(overlay, scheduler, host, Key.H);
        Assert.True(overlay.IsOpen);
        Assert.Equal([true], edges);

        Press(overlay, scheduler, host, Key.Grave);

        Assert.False(overlay.IsOpen);
        Assert.False(scheduler.Held);
        Assert.Equal([true, false], edges);
    }

    [Fact]
    public void ALoadWhoseStartFails_ShowsTheFailureKeepsTheSceneAndStillSteps()
    {
        Log.UseSink(null);
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());
        ReadoutScene before = Assert.IsType<ReadoutScene>(host.Scene);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);
        Press(overlay, scheduler, host, Key.Down);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Enter));

        Assert.StartsWith("Load failed", overlay.Scene.Status, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), overlay.Scene.Status, StringComparison.Ordinal);
        Assert.Same(before, host.Scene);
        Assert.False(before.Stopped);
        Assert.True(scheduler.Held);
        Assert.Equal(0, scheduler.Tick);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Right));

        Assert.Equal(1, scheduler.Tick);
        Assert.Equal(2, before.Steps);
        Assert.Equal(string.Empty, overlay.Scene.Status);
    }

    [Fact]
    public void Step_RunsOneTickThroughTheInputPathWithoutTheMenusKeysAndRepeatsOnAHeldKey()
    {
        RecordingSimulation simulation = new(SharedAction, SpaceAction, RightAction);
        FixedStepScheduler scheduler = new(
            StepSeconds,
            5,
            new ActionBindings().Bind(SpaceAction, Key.Space).Bind(RightAction, Key.Right));
        using OverlayHost overlay = new(Key.Grave, scheduler, simulation);

        Open(overlay, scheduler, simulation);
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Right, Key.Space));

        RecordedStep step = Assert.Single(simulation.Recorded);
        Assert.True((step.First.Held || step.Second.Held));
        Assert.False(step.Third.Held);
        Assert.Equal(1, scheduler.Tick);
        Assert.Equal(1, scheduler.StepsThisFrame);
        Assert.Equal(1f, scheduler.InterpolationAlpha);
        Assert.Equal(0, scheduler.AccumulatorSeconds);
        Assert.True(scheduler.Held);

        for (int frame = 0; frame < OverlayScene.RepeatDelayFrames - 1; frame++)
        {
            Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Right));
        }

        Assert.Single(simulation.Recorded);

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Right));
        Assert.Equal(2, simulation.Recorded.Count);

        for (int frame = 0; frame < OverlayScene.RepeatIntervalFrames - 1; frame++)
        {
            Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Right));
        }

        Assert.Equal(2, simulation.Recorded.Count);

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Right));
        Assert.Equal(3, simulation.Recorded.Count);
        Assert.Equal(3, scheduler.Tick);
    }

    [Fact]
    public void Hide_WithdrawsTheOverlayWhileHeldAndTheToggleRestoresIt()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.H));
        long overlayTick = overlay.Host.Tick;

        Assert.True(overlay.IsHidden);
        Assert.False(overlay.IsOpen);
        Assert.True(scheduler.Held);

        DeviceSnapshot passed = Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Space));

        Assert.Equal(overlayTick, overlay.Host.Tick);
        Assert.True(passed.IsDown(Key.Space));
        Assert.Equal(0, scheduler.Tick);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));

        Assert.True(overlay.IsOpen);
        Assert.True(scheduler.Held);
        Assert.Equal(overlayTick + 1, overlay.Host.Tick);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));

        Assert.False(overlay.IsOpen);
        Assert.False(overlay.IsHidden);
        Assert.False(scheduler.Held);
    }

    [Fact]
    public void TheHPressThatShowsAnOverlayHiddenByTheMenuItem_IsConsumed()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Up);
        Assert.Equal("Hide", Labels(overlay.Scene)[overlay.Scene.FocusedIndex]);

        Press(overlay, scheduler, host, Key.Enter);
        Assert.True(overlay.IsHidden);

        DeviceSnapshot game = Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.H));

        Assert.True(overlay.IsOpen);
        Assert.False(game.IsDown(Key.H));
        Assert.False(overlay.Host.Input.IsHeld(OverlayActions.Hide));

        game = Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.H));

        Assert.True(overlay.IsOpen);
        Assert.False(game.IsDown(Key.H));
        Assert.False(overlay.Host.Input.IsHeld(OverlayActions.Hide));
        Assert.True(scheduler.Held);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.H));

        Assert.True(overlay.IsHidden);
    }

    [Fact]
    public void H_HidesAndPressedAgainShowsTheOverlayStillHeld()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.H));
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.H));

        Assert.True(overlay.IsHidden);
        Assert.True(scheduler.Held);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.H));

        Assert.True(overlay.IsOpen);
        Assert.False(overlay.IsHidden);
        Assert.True(scheduler.Held);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.H));

        Assert.True(overlay.IsHidden);
        Assert.True(scheduler.Held);
        Assert.Equal(0, scheduler.Tick);
    }

    [Fact]
    public void Exit_TearsDownTheRunAndTheNextHeldAdvanceReportsIt()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Up));
        Assert.Equal("Exit", Labels(overlay.Scene)[overlay.Scene.FocusedIndex]);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Enter));

        Assert.True(host.ExitRequested);
        Assert.True(scheduler.Held);

        DeviceSnapshot stripped = overlay.Observe(DeviceSnapshot.Empty);
        Assert.True(scheduler.Advance(StepSeconds, stripped, host));

        long overlayTick = overlay.Host.Tick;
        overlay.Step();
        Assert.Equal(overlayTick, overlay.Host.Tick);
    }

    [Fact]
    public void AStepThatExhaustsTheDriver_EndsTheRunOnTheNextHeldAdvance()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = new(StepSeconds, 5, new ActionBindings(), new EmptyDriver(), host);
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Right));

        Assert.Equal(0, scheduler.Tick);
        Assert.True(scheduler.Held);

        DeviceSnapshot stripped = overlay.Observe(DeviceSnapshot.Empty);
        Assert.True(scheduler.Advance(StepSeconds, stripped, host));
    }

    [Fact]
    public void AToggleThatIsAlsoAMenuKey_DoesNotFireThatKeysActionOnTheFrameItOpens()
    {
        RecordingSimulation simulation = new(SharedAction, SpaceAction, RightAction);
        FixedStepScheduler enterScheduler = CreateScheduler();
        using OverlayHost enterOverlay = new(Key.Enter, enterScheduler, simulation);

        Frame(enterOverlay, enterScheduler, simulation, DeviceSnapshot.Of(Key.Enter));

        Assert.True(enterOverlay.IsOpen);
        Assert.True(enterScheduler.Held);

        using SceneHost host = CreateHost();
        FixedStepScheduler rightScheduler = CreateScheduler();
        using OverlayHost rightOverlay = new(Key.Right, rightScheduler, host, host);

        Frame(rightOverlay, rightScheduler, host, DeviceSnapshot.Of(Key.Right));
        Frame(rightOverlay, rightScheduler, host, DeviceSnapshot.Of(Key.Right));

        Assert.True(rightOverlay.IsOpen);
        Assert.Equal(0, rightScheduler.Tick);
    }

    [Fact]
    public void RestartWhileTheRunHasAlreadyRequestedExit_ShowsTheRefusalAndKeepsTheHold()
    {
        Log.UseSink(null);
        using SceneHost host = new(
            SceneTransition.ToScene(typeof(ExitOnStartScene), null),
            static (in SceneTransition _) => new ExitOnStartScene(),
            new Run());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.R));

        Assert.StartsWith("Restart failed", overlay.Scene.Status, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), overlay.Scene.Status, StringComparison.Ordinal);
        Assert.True(scheduler.Held);
        Assert.True(overlay.IsOpen);
        Assert.Equal(0, scheduler.Tick);
    }

    [Fact]
    public void RestartWhileATransitionIsAlreadyPending_ShowsTheRefusalAndDoesNotStep()
    {
        using SceneHost host = new(
            SceneTransition.ToScene(typeof(RequestOnStartScene), null),
            static (in SceneTransition target) => target.SceneType == typeof(RequestOnStartScene)
                ? new RequestOnStartScene()
                : new PlainScene(),
            new Run());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        Scene before = host.Scene;

        Open(overlay, scheduler, host);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.R));

        Assert.NotEmpty(overlay.Scene.Status);
        Assert.Same(before, host.Scene);
        Assert.True(scheduler.Held);
        Assert.Equal(0, scheduler.Tick);
    }

    [Fact]
    public void AnEnterThatActivatesAnItem_IsWithheldFromTheStepItCausesAndFromTheResumedStep()
    {
        RecordingSimulation simulation = new(SharedAction, SpaceAction, RightAction);
        FixedStepScheduler scheduler = new(
            StepSeconds,
            5,
            new ActionBindings().Bind(SharedAction, Key.Enter));
        using OverlayHost overlay = new(Key.Grave, scheduler, simulation);

        Open(overlay, scheduler, simulation);
        Assert.Equal("Step", Labels(overlay.Scene)[overlay.Scene.FocusedIndex]);

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Enter));

        RecordedStep stepped = Assert.Single(simulation.Recorded);
        Assert.False(stepped.First.Pressed);
        Assert.False((stepped.First.Held || stepped.Second.Held));
        Assert.True(overlay.IsOpen);

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Enter, Key.Grave));

        Assert.False(overlay.IsOpen);
        Assert.False(scheduler.Held);
        Assert.Equal(2, simulation.Recorded.Count);
        Assert.False(simulation.Recorded[^1].First.Pressed);
        Assert.False((simulation.Recorded[^1].First.Held || simulation.Recorded[^1].Second.Held));

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Enter));

        Assert.True(simulation.Recorded[^1].First.Pressed);
    }

    [Fact]
    public void TimeScaleSubmenu_MarksThePaceInForceSetsItWithoutATickAndKeepsItForThePlaySession()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.T);

        Assert.Equal("Time Scale", overlay.Scene.Title);
        Assert.Equal(2, overlay.Scene.Depth);
        AssertLadder(overlay.Scene, host.Run.TimeScale);

        Press(overlay, scheduler, host, Key.T);
        Assert.Equal(2, overlay.Scene.Depth);

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        // The pick lands on the run, where the game reads it, and on the applied value; the marks
        // follow at once, the focus and depth stay, and no tick is stepped for it.
        Assert.Equal(0.5, host.Run.TimeScale);
        Assert.Equal(0.5, scheduler.TimeScale);
        AssertLadder(overlay.Scene, host.Run.TimeScale);
        Assert.Equal(1, overlay.Scene.FocusedIndex);
        Assert.Equal(2, overlay.Scene.Depth);
        Assert.Equal(0, scheduler.Tick);

        // Closed, the game runs at the chosen pace: a frame worth one step buys half of one.
        Press(overlay, scheduler, host, Key.Backspace);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));

        Assert.False(overlay.IsOpen);
        Assert.Equal(0, scheduler.Tick);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);

        Assert.Equal(1, scheduler.Tick);

        // Reopened, hidden, shown again and across a Restart, the overlay never resets it.
        Press(overlay, scheduler, host, Key.Grave);
        Assert.True(overlay.IsOpen);
        Press(overlay, scheduler, host, Key.H);
        Assert.True(overlay.IsHidden);
        Press(overlay, scheduler, host, Key.H);
        Assert.True(overlay.IsOpen);
        Press(overlay, scheduler, host, Key.R);
        Assert.IsType<ReadoutScene>(host.Scene);

        Press(overlay, scheduler, host, Key.T);

        Assert.Equal(0.5, host.Run.TimeScale);
        AssertLadder(overlay.Scene, host.Run.TimeScale);
    }

    // The run holds the pace, so the ladder shows what the game set — and marks nothing when the
    // game set a pace the ladder does not offer.
    [Fact]
    public void TimeScaleSubmenu_MarksThePaceTheGameSetAndNoRowForOneOffTheLadder()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        host.Run.TimeScale = 2;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.T);

        AssertLadder(overlay.Scene, host.Run.TimeScale);

        // Set off the ladder while the submenu is open: the marks follow it the next time it opens.
        host.Run.TimeScale = 1.5;
        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.T);

        AssertLadder(overlay.Scene, host.Run.TimeScale);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal(0.25, host.Run.TimeScale);
        AssertLadder(overlay.Scene, host.Run.TimeScale);
    }

    [Fact]
    public void TheTimeScaleHotkey_IsWithheldFromTheGameWhileTheOverlayIsOpen()
    {
        RecordingSimulation simulation = new(SharedAction, SpaceAction, RightAction);
        FixedStepScheduler scheduler = new(
            StepSeconds,
            5,
            new ActionBindings().Bind(SharedAction, Key.T));
        using OverlayHost overlay = new(Key.Grave, scheduler, simulation);

        Open(overlay, scheduler, simulation);
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.T));

        Assert.Equal("Time Scale", overlay.Scene.Title);
        Assert.Empty(simulation.Recorded);

        // Withheld from the step the close resumes, and read again once released.
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.T, Key.Grave));

        Assert.False(overlay.IsOpen);
        RecordedStep resumed = Assert.Single(simulation.Recorded);
        Assert.False(resumed.First.Pressed);
        Assert.False((resumed.First.Held || resumed.Second.Held));

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.T));

        Assert.True(simulation.Recorded[^1].First.Pressed);
    }

    // The Time Scale submenu: one row per pace on the host's ladder, in its order, and a mark on
    // exactly the row whose pace is the one in force — none, where the game set one off the ladder.
    private static void AssertLadder(OverlayScene scene, double pace)
    {
        string[] labels = Labels(scene);

        Assert.Equal(OverlayHost.TimeScales.Length, labels.Length);
        for (int index = 0; index < labels.Length; index++)
        {
            (double scale, string label) = OverlayHost.TimeScales[index];

            Assert.EndsWith(label, labels[index], StringComparison.Ordinal);
            Assert.Equal(scale == pace, labels[index].StartsWith("(x)", StringComparison.Ordinal));
        }
    }

    private static SceneHost CreateHost(Run? run = null, List<SceneTransition>? resolved = null) =>
        new(
            SceneTransition.ToScene(typeof(ReadoutScene), null),
            (in SceneTransition target) =>
            {
                resolved?.Add(target);

                return target.Kind switch
                {
                    SceneTransitionKind.Named when target.DocumentName == NamedDocument => new NamedScene(),
                    SceneTransitionKind.Scene when target.SceneType == typeof(PlainScene) => new PlainScene(),
                    SceneTransitionKind.Scene when target.SceneType == typeof(PayloadScene) => new PayloadScene(),
                    SceneTransitionKind.Scene when target.SceneType == typeof(ReadoutScene) => new ReadoutScene(),
                    _ => throw new InvalidOperationException($"Unexpected transition {target.Kind}."),
                };
            },
            run ?? new Run());

    private static SceneRegistry CreateRegistry() =>
        new(
            new EntityRegistry([]),
            [
                SceneRegistration.Plain(typeof(PlainScene), static () => new PlainScene()),
                SceneRegistration.Plain(typeof(PayloadScene), static () => new PayloadScene()),
                SceneRegistration.FromDocument(typeof(NamedScene), NamedDocument, static _ => new NamedScene()),
            ]);

    private sealed class ReadoutScene : Scene
    {
        internal int Steps { get; private set; }

        internal bool Stopped { get; private set; }

        protected override void OnStep(in StepContext context) => Steps++;

        protected override void OnStop() => Stopped = true;
    }

    private sealed class PlainScene : Scene;

    private sealed class ExitOnStartScene : Scene
    {
        protected override void OnStart() => Run.RequestExit();
    }

    private sealed class RequestOnStartScene : Scene
    {
        protected override void OnStart() => Run.RequestScene<PlainScene>();
    }

    private sealed class EmptyDriver : IInputDriver
    {
        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            snapshot = DeviceSnapshot.Empty;

            return false;
        }
    }

    private sealed class NamedScene : Scene;

    private sealed class PayloadScene : Scene
    {
        protected override void OnStart()
        {
            if (EntryPayload is null)
            {
                throw new InvalidOperationException("PayloadScene needs a payload.\nSecond line.");
            }
        }
    }
}
