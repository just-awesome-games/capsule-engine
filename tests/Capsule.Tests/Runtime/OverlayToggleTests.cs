using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using static Capsule.Tests.Runtime.OverlayFixtures;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Runtime;

// The overlay's toggle, its hold over the simulation, and the buttons it keeps from the game.
public sealed class OverlayToggleTests
{
    private const double StepSeconds = 0.1;
    private static readonly InputAction SharedAction = new("shared");
    private static readonly InputAction SpaceAction = new("space");
    private static readonly InputAction RightAction = new("right");

    [Fact]
    public void LeadingEdgeTogglesAndQuarantinesTheBoundButtonUntilRelease()
    {
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, new RecordingSimulation());

        DeviceSnapshot snapshot = overlay.Observe(DeviceSnapshot.Of(Key.Grave, Key.Space));

        Assert.True(overlay.IsOpen);
        Assert.True(scheduler.Held);
        Assert.False(snapshot.IsDown(Key.Grave));
        Assert.True(snapshot.IsDown(Key.Space));

        snapshot = overlay.Observe(DeviceSnapshot.Of(Key.Grave, Key.Space));
        Assert.True(overlay.IsOpen);
        Assert.False(snapshot.IsDown(Key.Grave));

        snapshot = overlay.Observe(DeviceSnapshot.Of(Key.Space));
        Assert.True(overlay.IsOpen);
        Assert.True(snapshot.IsDown(Key.Space));

        snapshot = overlay.Observe(DeviceSnapshot.Of(Key.Grave));

        Assert.False(overlay.IsOpen);
        Assert.False(scheduler.Held);
        Assert.False(snapshot.IsDown(Key.Grave));
    }

    [Fact]
    public void QuarantineWinsOverAGameBindingOfTheSameButton()
    {
        RecordingSimulation simulation = new(SharedAction, SpaceAction, RightAction);
        FixedStepScheduler scheduler = new(StepSeconds, 5, new ActionBindings().Bind(SharedAction, Key.Grave));
        using OverlayHost overlay = new(Key.Grave, scheduler, simulation);

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Grave));
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Grave));

        RecordedStep step = Assert.Single(simulation.Recorded);
        Assert.False(step.First.Pressed);
        Assert.False(step.First.Held || step.Second.Held);
    }

    [Fact]
    public void ReboundPadButtonOpensAndIsQuarantined()
    {
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new((InputButton)PadButton.South, scheduler, new RecordingSimulation());

        DeviceSnapshot snapshot = overlay.Observe(DeviceSnapshot.Empty.With(PadButton.South).With(Key.Space));

        Assert.True(overlay.IsOpen);
        Assert.True(scheduler.Held);
        Assert.False(snapshot.IsDown(PadButton.South));
        Assert.True(snapshot.IsDown(Key.Space));
    }

    [Fact]
    public void NoneNeverOpensOrChangesTheSnapshot()
    {
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(InputButton.None, scheduler, new RecordingSimulation());

        DeviceSnapshot snapshot = overlay.Observe(DeviceSnapshot.Of(Key.Grave));

        Assert.False(overlay.IsOpen);
        Assert.False(scheduler.Held);
        Assert.Equal(DeviceSnapshot.Of(Key.Grave), snapshot);
    }

    // Closed, the overlay builds no page at all: there is nothing on screen to build one for.
    [Fact]
    public void AClosedOverlay_BuildsNoRows()
    {
        using OverlayHost overlay = new(Key.Grave, CreateScheduler(), new RecordingSimulation());

        overlay.Step();

        Assert.False(overlay.IsOpen);
        Assert.Empty(overlay.Rows);
    }

    // The hold's edges are the host's to hear: taken on open, kept through hide and either way back
    // from it, let go on close.
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
    public void Hide_WithdrawsTheOverlayWhileHeldAndTheToggleRestoresIt()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.H));

        Assert.True(overlay.IsHidden);
        Assert.False(overlay.IsOpen);
        Assert.True(scheduler.Held);

        // Hidden, the rows read no input: the focus stands and the game sees its own keys again.
        int focus = overlay.Focus;
        DeviceSnapshot passed = Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Space, Key.Down));

        Assert.Equal(focus, overlay.Focus);
        Assert.True(passed.IsDown(Key.Space));
        Assert.Equal(0, scheduler.Tick);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));

        Assert.True(overlay.IsOpen);
        Assert.True(scheduler.Held);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));

        Assert.False(overlay.IsOpen);
        Assert.False(overlay.IsHidden);
        Assert.False(scheduler.Held);
    }

    // The press that shows a hidden overlay again is withheld from the rows, or the Hide row would
    // read it as a fresh press and hide it on the same frame.
    [Fact]
    public void TheHPressThatShowsAnOverlayHiddenByItsRow_IsConsumed()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Up);
        Assert.Equal("Hide", Focused(overlay));

        Press(overlay, scheduler, host, Key.Enter);
        Assert.True(overlay.IsHidden);

        DeviceSnapshot game = Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.H));

        Assert.True(overlay.IsOpen);
        Assert.False(game.IsDown(Key.H));

        game = Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.H));

        Assert.True(overlay.IsOpen);
        Assert.False(game.IsDown(Key.H));
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
    public void AToggleThatIsAlsoAnOverlayKey_DoesNotFireThatKeysActionOnTheFrameItOpens()
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

    // An Enter that chose a row is withheld from the tick that row forces and from the step the
    // close resumes, and is read again once released.
    [Fact]
    public void AnEnterThatActivatesARow_IsWithheldFromTheStepItCausesAndFromTheResumedStep()
    {
        RecordingSimulation simulation = new(SharedAction, SpaceAction, RightAction);
        FixedStepScheduler scheduler = new(StepSeconds, 5, new ActionBindings().Bind(SharedAction, Key.Enter));
        using OverlayHost overlay = new(Key.Grave, scheduler, simulation);

        Open(overlay, scheduler, simulation);
        Assert.Equal("Step", Focused(overlay));

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Enter));

        RecordedStep stepped = Assert.Single(simulation.Recorded);
        Assert.False(stepped.First.Pressed);
        Assert.False(stepped.First.Held || stepped.Second.Held);
        Assert.True(overlay.IsOpen);

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Enter, Key.Grave));

        Assert.False(overlay.IsOpen);
        Assert.False(scheduler.Held);
        Assert.Equal(2, simulation.Recorded.Count);
        Assert.False(simulation.Recorded[^1].First.Pressed);
        Assert.False(simulation.Recorded[^1].First.Held || simulation.Recorded[^1].Second.Held);

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Enter));

        Assert.True(simulation.Recorded[^1].First.Pressed);
    }

    [Fact]
    public void AnOverlayHotkey_IsWithheldFromTheGameWhileTheOverlayIsOpen()
    {
        RecordingSimulation simulation = new(SharedAction, SpaceAction, RightAction);
        FixedStepScheduler scheduler = new(StepSeconds, 5, new ActionBindings().Bind(SharedAction, Key.T));
        using OverlayHost overlay = new(Key.Grave, scheduler, simulation);

        Open(overlay, scheduler, simulation);
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.T));

        Assert.Equal("Time Scale", overlay.Title);
        Assert.Empty(simulation.Recorded);

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.T, Key.Grave));

        Assert.False(overlay.IsOpen);
        RecordedStep resumed = Assert.Single(simulation.Recorded);
        Assert.False(resumed.First.Pressed);
        Assert.False(resumed.First.Held || resumed.Second.Held);

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.T));

        Assert.True(simulation.Recorded[^1].First.Pressed);
    }
}
