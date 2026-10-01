using Capsule.Input;
using Capsule.Runtime.DevTools;
using static Capsule.Tests.Runtime.OverlayFixtures;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Runtime;

// The overlay's toggle, its hold over the simulation, and the buttons it keeps from the game.
public sealed class OverlayToggleTests
{
    private static readonly InputAction SharedAction = new("shared");

    [Fact]
    public void LeadingEdgeTogglesAndQuarantinesTheBoundButtonUntilRelease()
    {
        using OverlayRig rig = new();
        OverlayHost overlay = rig.Overlay;

        DeviceSnapshot snapshot = overlay.Observe(DeviceSnapshot.Of(Key.Grave, Key.Space));

        Assert.True(overlay.IsOpen);
        Assert.True(rig.Scheduler.Held);
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
        Assert.False(rig.Scheduler.Held);
        Assert.False(snapshot.IsDown(Key.Grave));
    }

    [Fact]
    public void QuarantineWinsOverAGameBindingOfTheSameButton()
    {
        using OverlayRig rig = Recorded(Key.Grave);

        rig.Frame(DeviceSnapshot.Of(Key.Grave));
        rig.Frame();
        rig.Frame(DeviceSnapshot.Of(Key.Grave));

        RecordedStep step = Assert.Single(rig.Recording.Recorded);
        Assert.False(step.First.Pressed);
        Assert.False(step.First.Held);
    }

    [Fact]
    public void ReboundPadButtonOpensAndIsQuarantined()
    {
        using OverlayRig rig = new(toggle: PadButton.South);

        DeviceSnapshot snapshot = rig.Overlay.Observe(DeviceSnapshot.Empty.With(PadButton.South).With(Key.Space));

        Assert.True(rig.Overlay.IsOpen);
        Assert.False(snapshot.IsDown(PadButton.South));
        Assert.True(snapshot.IsDown(Key.Space));
    }

    [Fact]
    public void NoneNeverOpensOrChangesTheSnapshot()
    {
        using OverlayRig rig = new(toggle: InputButton.None);

        DeviceSnapshot snapshot = rig.Overlay.Observe(DeviceSnapshot.Of(Key.Grave));

        Assert.False(rig.Overlay.IsOpen);
        Assert.False(rig.Scheduler.Held);
        Assert.Equal(DeviceSnapshot.Of(Key.Grave), snapshot);
    }

    // Closed, the overlay builds no page. There is nothing on screen to build one for.
    [Fact]
    public void AClosedOverlay_BuildsNoRows()
    {
        using OverlayRig rig = new();

        rig.Frame();

        Assert.Empty(rig.Overlay.Rows);
    }

    // Hiding and showing again keep the hold without an edge.
    [Fact]
    public void TheHold_ReportsItsEdgesOnOpenAndCloseAndNotOnHideOrTheReturnFromIt()
    {
        using OverlayRig rig = new(CreateHost());
        List<bool> edges = [];
        rig.Overlay.HoldChanged = edges.Add;

        rig.Open();
        Assert.Equal([true], edges);

        rig.Press(Key.H);
        Assert.True(rig.Overlay.IsHidden);
        Assert.Equal([true], edges);

        rig.Press(Key.Grave);
        Assert.True(rig.Overlay.IsOpen);
        Assert.True(rig.Scheduler.Held);
        Assert.Equal([true], edges);

        rig.Press(Key.H);
        Assert.True(rig.Overlay.IsHidden);
        rig.Press(Key.H);
        Assert.True(rig.Overlay.IsOpen);
        Assert.Equal([true], edges);

        rig.Press(Key.Grave);

        Assert.False(rig.Overlay.IsOpen);
        Assert.False(rig.Scheduler.Held);
        Assert.Equal([true, false], edges);
    }

    [Fact]
    public void Hide_WithdrawsTheOverlayWhileHeldAndTheToggleRestoresIt()
    {
        using OverlayRig rig = new(CreateHost());

        rig.Open();
        rig.Frame(DeviceSnapshot.Of(Key.H));

        Assert.True(rig.Overlay.IsHidden);
        Assert.False(rig.Overlay.IsOpen);
        Assert.True(rig.Scheduler.Held);

        // Hidden rows read no input, and the game sees its own keys again.
        int focus = rig.Overlay.Focus;
        DeviceSnapshot passed = rig.Frame(DeviceSnapshot.Of(Key.Space, Key.Down));

        Assert.Equal(focus, rig.Overlay.Focus);
        Assert.True(passed.IsDown(Key.Space));
        Assert.Equal(0, rig.Scheduler.Tick);

        rig.Frame(DeviceSnapshot.Of(Key.Grave));

        Assert.True(rig.Overlay.IsOpen);
        Assert.True(rig.Scheduler.Held);

        rig.Frame();
        rig.Frame(DeviceSnapshot.Of(Key.Grave));

        Assert.False(rig.Overlay.IsOpen);
        Assert.False(rig.Overlay.IsHidden);
        Assert.False(rig.Scheduler.Held);
    }

    // The press that shows a hidden overlay again is withheld from the rows, or the Hide row would
    // read it as a fresh press and hide it on the same frame.
    [Fact]
    public void TheHPressThatShowsAnOverlayHiddenByItsRow_IsConsumed()
    {
        using OverlayRig rig = new(CreateHost());

        rig.Open();
        rig.Press(Key.Up);
        rig.Press(Key.Up);
        Assert.Equal("Hide", rig.Focused());

        rig.Press(Key.Enter);
        Assert.True(rig.Overlay.IsHidden);

        DeviceSnapshot game = rig.Frame(DeviceSnapshot.Of(Key.H));

        Assert.True(rig.Overlay.IsOpen);
        Assert.False(game.IsDown(Key.H));

        game = rig.Frame(DeviceSnapshot.Of(Key.H));

        Assert.True(rig.Overlay.IsOpen);
        Assert.False(game.IsDown(Key.H));
        Assert.True(rig.Scheduler.Held);

        rig.Frame();
        rig.Frame(DeviceSnapshot.Of(Key.H));

        Assert.True(rig.Overlay.IsHidden);
    }

    [Fact]
    public void AToggleThatIsAlsoAnOverlayKey_DoesNotFireThatKeysActionOnTheFrameItOpens()
    {
        using (OverlayRig enter = new(toggle: Key.Enter))
        {
            enter.Frame(DeviceSnapshot.Of(Key.Enter));

            Assert.True(enter.Overlay.IsOpen);
            Assert.Equal(0, enter.Scheduler.Tick);
        }

        using OverlayRig right = new(CreateHost(), toggle: Key.Right);

        right.Frame(DeviceSnapshot.Of(Key.Right));
        right.Frame(DeviceSnapshot.Of(Key.Right));

        Assert.True(right.Overlay.IsOpen);
        Assert.Equal(0, right.Scheduler.Tick);
    }

    // An Enter that chose a row is withheld from the tick that row forces and from the step the close
    // resumes, and is read again once released.
    [Fact]
    public void AnEnterThatActivatesARow_IsWithheldFromTheStepItCausesAndFromTheResumedStep()
    {
        using OverlayRig rig = Recorded(Key.Enter);
        List<RecordedStep> recorded = rig.Recording.Recorded;

        rig.Open();
        Assert.Equal("Step", rig.Focused());

        rig.Frame(DeviceSnapshot.Of(Key.Enter));

        Assert.False(Assert.Single(recorded).First.Held);
        Assert.True(rig.Overlay.IsOpen);

        rig.Frame(DeviceSnapshot.Of(Key.Enter, Key.Grave));

        Assert.False(rig.Scheduler.Held);
        Assert.Equal(2, recorded.Count);
        Assert.False(recorded[^1].First.Held);

        rig.Frame();
        rig.Frame(DeviceSnapshot.Of(Key.Enter));

        Assert.True(recorded[^1].First.Pressed);
    }

    // A game binding SharedAction to key, recording what each step read of it.
    private static OverlayRig Recorded(Key key) =>
        new(new RecordingSimulation(SharedAction), scheduler: CreateScheduler(new ActionBindings().Bind(SharedAction, key)));
}
