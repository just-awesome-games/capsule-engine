using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.OverlayFixtures;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Runtime;

// What the overlay's rows do to the run: stepping it by hand, restarting it, loading a scene into it,
// and exiting.
// A failed row logs, and the sink is one process-wide slot.
[Collection(LogSinkCollection.Name)]
public sealed class OverlayRunTests
{
    private const double StepSeconds = 0.1;
    private static readonly InputAction SharedAction = new("shared");
    private static readonly InputAction SpaceAction = new("space");
    private static readonly InputAction RightAction = new("right");

    [Fact]
    public void Step_RunsOneTickThroughTheInputPathWithoutTheOverlaysKeysAndRepeatsOnAHeldKey()
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
        Assert.True(step.First.Held || step.Second.Held);
        Assert.False(step.Third.Held);
        Assert.Equal(1, scheduler.Tick);
        Assert.Equal(1, scheduler.StepsThisFrame);
        Assert.Equal(1f, scheduler.InterpolationAlpha);
        Assert.True(scheduler.Held);

        for (int frame = 0; frame < OverlayHost.RepeatDelayFrames - 1; frame++)
        {
            Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Right));
        }

        Assert.Single(simulation.Recorded);

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Right));
        Assert.Equal(2, simulation.Recorded.Count);

        for (int frame = 0; frame < OverlayHost.RepeatIntervalFrames - 1; frame++)
        {
            Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Right));
        }

        Assert.Equal(2, simulation.Recorded.Count);

        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Right));
        Assert.Equal(3, simulation.Recorded.Count);
        Assert.Equal(3, scheduler.Tick);
    }

    // A held direction repeats on the same pacing as a held hotkey.
    [Fact]
    public void AHeldDirection_RepeatsPastTheDelay()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);

        // The press itself moves one row; the repeat lands on the frame the delay is met.
        for (int frame = 0; frame <= OverlayHost.RepeatDelayFrames; frame++)
        {
            Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Down));
        }

        Assert.Equal(2, overlay.Focus);

        for (int frame = 0; frame < OverlayHost.RepeatIntervalFrames; frame++)
        {
            Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Down));
        }

        Assert.Equal(3, overlay.Focus);
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

        Assert.StartsWith("Restart failed", overlay.Status, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), overlay.Status, StringComparison.Ordinal);
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

        Assert.NotEmpty(overlay.Status);
        Assert.Same(before, host.Scene);
        Assert.True(scheduler.Held);
        Assert.Equal(0, scheduler.Tick);
    }

    // A load whose incoming scene throws on its way up: the failure is shown on one line, the run
    // stays on the scene it was on, and the next Step still advances it.
    [Fact]
    public void ALoadWhoseStartFails_ShowsTheFailureKeepsTheSceneAndStillSteps()
    {
        Log.UseSink(null);
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());
        ReadoutScene before = Assert.IsType<ReadoutScene>(host.Scene);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.L);
        Press(overlay, scheduler, host, Key.Down);
        Assert.Equal("PayloadScene", Focused(overlay));

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Enter));

        Assert.StartsWith("Load failed", overlay.Status, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), overlay.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("Second line", overlay.Status, StringComparison.Ordinal);
        Assert.Same(before, host.Scene);
        Assert.False(before.Stopped);
        Assert.True(scheduler.Held);
        Assert.Equal(0, scheduler.Tick);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Right));

        Assert.Equal(1, scheduler.Tick);
        Assert.Equal(2, before.Steps);
        Assert.Equal(string.Empty, overlay.Status);
    }

    [Fact]
    public void Exit_TearsDownTheRunAndTheNextHeldAdvanceReportsIt()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Up));
        Assert.Equal("Exit", Focused(overlay));

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Enter));

        Assert.True(host.ExitRequested);
        Assert.True(scheduler.Held);

        DeviceSnapshot stripped = overlay.Observe(DeviceSnapshot.Empty);
        Assert.True(scheduler.Advance(StepSeconds, stripped, host));

        // Nothing of the overlay is read once the run is gone.
        overlay.Step();
        Assert.True(overlay.IsOpen);
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
}
