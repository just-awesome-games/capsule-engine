using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.OverlayFixtures;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Runtime;

// What the overlay's rows do to the run. A failed row logs to the process-wide sink.
[Collection(LogSinkCollection.Name)]
public sealed class OverlayRunTests
{
    private static readonly InputAction SharedAction = new("shared");
    private static readonly InputAction SpaceAction = new("space");
    private static readonly InputAction RightAction = new("right");

    [Fact]
    public void Step_RunsOneTickThroughTheInputPathWithoutTheOverlaysKeysAndRepeatsOnAHeldKey()
    {
        using OverlayRig rig = new(
            new RecordingSimulation(SharedAction, SpaceAction, RightAction),
            scheduler: CreateScheduler(new ActionBindings().Bind(SpaceAction, Key.Space).Bind(RightAction, Key.Right)));
        List<RecordedStep> recorded = rig.Recording.Recorded;

        rig.Open();
        rig.Frame(DeviceSnapshot.Of(Key.Right, Key.Space));

        RecordedStep step = Assert.Single(recorded);
        Assert.True(step.First.Held || step.Second.Held);
        Assert.False(step.Third.Held);
        Assert.Equal(1, rig.Scheduler.StepsThisFrame);
        Assert.Equal(1f, rig.Scheduler.InterpolationAlpha);
        Assert.True(rig.Scheduler.Held);

        for (int frame = 0; frame < OverlayHost.RepeatDelayFrames - 1; frame++)
        {
            rig.Frame(DeviceSnapshot.Of(Key.Right));
        }

        Assert.Single(recorded);

        rig.Frame(DeviceSnapshot.Of(Key.Right));
        Assert.Equal(2, recorded.Count);

        for (int frame = 0; frame < OverlayHost.RepeatIntervalFrames - 1; frame++)
        {
            rig.Frame(DeviceSnapshot.Of(Key.Right));
        }

        Assert.Equal(2, recorded.Count);

        rig.Frame(DeviceSnapshot.Of(Key.Right));
        Assert.Equal(3, rig.Scheduler.Tick);
    }

    [Fact]
    public void AHeldDirection_RepeatsPastTheDelay()
    {
        using OverlayRig rig = new(CreateHost());

        rig.Open();

        // The press moves one row and the repeat lands on the frame the delay is met.
        for (int frame = 0; frame <= OverlayHost.RepeatDelayFrames; frame++)
        {
            rig.Frame(DeviceSnapshot.Of(Key.Down));
        }

        Assert.Equal(2, rig.Overlay.Focus);

        for (int frame = 0; frame < OverlayHost.RepeatIntervalFrames; frame++)
        {
            rig.Frame(DeviceSnapshot.Of(Key.Down));
        }

        Assert.Equal(3, rig.Overlay.Focus);
    }

    [Fact]
    public void Restart_ReplacesTheSceneInExactlyOneTickAndStaysHeld()
    {
        using OverlayRig rig = new(CreateHost());
        Scene before = rig.Host.Scene;

        rig.Open();
        rig.Frame(DeviceSnapshot.Of(Key.R));

        Assert.NotSame(before, rig.Host.Scene);
        Assert.IsType<ReadoutScene>(rig.Host.Scene);
        Assert.Equal(1, rig.Scheduler.Tick);
        Assert.True(rig.Scheduler.Held);
        Assert.Equal("ReadoutScene  tick 1", rig.Overlay.Readout);
    }

    [Fact]
    public void RestartWhileTheRunHasAlreadyRequestedExit_ShowsTheRefusalAndKeepsTheHold()
    {
        Log.UseSink(null);
        using OverlayRig rig = new(new SceneHost(
            SceneTransition.ToScene(typeof(ExitOnStartScene), null),
            static (in SceneTransition _) => new ExitOnStartScene(),
            new Run()));

        rig.Open();
        rig.Frame(DeviceSnapshot.Of(Key.R));

        Assert.StartsWith("Restart failed", rig.Overlay.Status, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), rig.Overlay.Status, StringComparison.Ordinal);
        Assert.True(rig.Scheduler.Held);
        Assert.True(rig.Overlay.IsOpen);
        Assert.Equal(0, rig.Scheduler.Tick);
    }

    [Fact]
    public void RestartWhileATransitionIsAlreadyPending_ShowsTheRefusalAndDoesNotStep()
    {
        using OverlayRig rig = new(new SceneHost(
            SceneTransition.ToScene(typeof(RequestOnStartScene), null),
            static (in SceneTransition target) => target.SceneType == typeof(RequestOnStartScene)
                ? new RequestOnStartScene()
                : new PlainScene(),
            new Run()));
        Scene before = rig.Host.Scene;

        rig.Open();
        rig.Frame(DeviceSnapshot.Of(Key.R));

        Assert.NotEmpty(rig.Overlay.Status);
        Assert.Same(before, rig.Host.Scene);
        Assert.True(rig.Scheduler.Held);
        Assert.Equal(0, rig.Scheduler.Tick);
    }

    // The failure is shown on one line and the run stays on its scene.
    [Fact]
    public void ALoadWhoseStartFails_ShowsTheFailureKeepsTheSceneAndStillSteps()
    {
        Log.UseSink(null);
        using OverlayRig rig = new(CreateHost(), CreateRegistry());
        ReadoutScene before = Assert.IsType<ReadoutScene>(rig.Host.Scene);

        rig.Open();
        rig.Press(Key.L);
        rig.Press(Key.Down);
        Assert.Equal("PayloadScene", rig.Focused());

        rig.Frame(DeviceSnapshot.Of(Key.Enter));

        Assert.StartsWith("Load failed", rig.Overlay.Status, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), rig.Overlay.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("Second line", rig.Overlay.Status, StringComparison.Ordinal);
        Assert.Same(before, rig.Host.Scene);
        Assert.False(before.Stopped);
        Assert.True(rig.Scheduler.Held);
        Assert.Equal(0, rig.Scheduler.Tick);

        rig.Frame();
        rig.Frame(DeviceSnapshot.Of(Key.Right));

        Assert.Equal(1, rig.Scheduler.Tick);
        Assert.Equal(2, before.Steps);
        Assert.Equal(string.Empty, rig.Overlay.Status);
    }

    [Fact]
    public void Exit_TearsDownTheRunAndTheNextHeldAdvanceReportsIt()
    {
        using OverlayRig rig = new(CreateHost());

        rig.Open();
        rig.Frame(DeviceSnapshot.Of(Key.Up));
        Assert.Equal("Exit", rig.Focused());

        rig.Frame(DeviceSnapshot.Of(Key.Enter));

        Assert.True(rig.Host.ExitRequested);
        Assert.True(rig.Scheduler.Held);

        Assert.True(rig.Scheduler.Advance(StepSeconds, rig.Overlay.Observe(DeviceSnapshot.Empty), rig.Host));

        // Nothing of the overlay is read once the run is gone.
        rig.Overlay.Step();
        Assert.True(rig.Overlay.IsOpen);
    }

    [Fact]
    public void AStepThatExhaustsTheDriver_EndsTheRunOnTheNextHeldAdvance()
    {
        SceneHost host = CreateHost();
        using OverlayRig rig = new(host, scheduler: new FixedStepScheduler(StepSeconds, 5, new ActionBindings(), new EmptyDriver(), host));

        rig.Open();
        rig.Frame(DeviceSnapshot.Of(Key.Right));

        Assert.Equal(0, rig.Scheduler.Tick);
        Assert.True(rig.Scheduler.Held);

        Assert.True(rig.Scheduler.Advance(StepSeconds, rig.Overlay.Observe(DeviceSnapshot.Empty), host));
    }
}
