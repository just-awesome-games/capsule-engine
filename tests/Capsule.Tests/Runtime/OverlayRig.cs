using System.Diagnostics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

// The overlay specs' host: one frame is observe, advance the game, step the overlay, which is the
// order CapsuleGame runs them in, and a press is the key's frame followed by its release. An
// instance owns the clock too, so a spec that reads the frame pane can say how long each frame took
// and how much of it the update bracket spent; a spec that does not reads the same frames through
// the static helpers, which drive an overlay the spec built itself.
internal sealed class OverlayRig : IDisposable
{
    internal const double StepSeconds = 0.1;

    // What an unclocked frame costs: enough to be a frame, short enough that sixty of them are a
    // second.
    private const double DefaultIntervalMs = 16;
    private const double DefaultUpdateMs = 1;

    private long _ticks;
    private long _frameStart;

    internal OverlayRig()
    {
        Scheduler = CreateScheduler();
        Overlay = new OverlayHost(Key.Grave, Scheduler, Simulation, timestamp: () => _ticks);
    }

    internal OverlayHost Overlay { get; }

    internal FixedStepScheduler Scheduler { get; }

    internal RecordingSimulation Simulation { get; } = new();

    internal string[] PaneLines() => Overlay.Scene.Pane.Text.Split('\n');

    internal void Open()
    {
        Press(Key.Grave);

        Assert.True(Overlay.IsOpen);
    }

    internal void Press(Key key)
    {
        Frame(DefaultIntervalMs, DefaultUpdateMs, sampled: DeviceSnapshot.Of(key));
        Frame(DefaultIntervalMs, DefaultUpdateMs, sampled: DeviceSnapshot.Empty);
    }

    // One frame that starts intervalMs after the last began and whose update bracket costs
    // updateMs, of which elapsedSeconds is offered to the scheduler.
    internal void Frame(double intervalMs, double updateMs, double elapsedSeconds = 0, DeviceSnapshot sampled = default)
    {
        _frameStart += Ticks(intervalMs);
        _ticks = _frameStart;
        DeviceSnapshot stripped = Overlay.Observe(sampled);
        _ticks += Ticks(updateMs);
        Scheduler.Advance(elapsedSeconds, stripped, Simulation);
        Overlay.Step();
    }

    public void Dispose() => Overlay.Dispose();

    internal static FixedStepScheduler CreateScheduler(ActionBindings? bindings = null) =>
        new(StepSeconds, 5, bindings ?? new ActionBindings());

    internal static string[] Labels(OverlayScene scene)
    {
        IReadOnlyList<MenuItem> items = scene.Current.Items;
        string[] labels = new string[items.Count];
        for (int index = 0; index < items.Count; index++)
        {
            labels[index] = items[index].Label;
        }

        return labels;
    }

    // Opens the overlay on the toggle's edge and releases it, so the next frame's keys are the
    // menu's.
    internal static void Open(OverlayHost overlay, FixedStepScheduler scheduler, ISimulation simulation)
    {
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Grave));
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Empty);

        Assert.True(overlay.IsOpen);
    }

    // One press: the key's frame and the release after it.
    internal static void Press(OverlayHost overlay, FixedStepScheduler scheduler, ISimulation simulation, Key key)
    {
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(key));
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Empty);
    }

    // One host frame; returns what the game saw.
    internal static DeviceSnapshot Frame(
        OverlayHost overlay,
        FixedStepScheduler scheduler,
        ISimulation simulation,
        DeviceSnapshot sampled,
        double elapsed = StepSeconds)
    {
        DeviceSnapshot stripped = overlay.Observe(sampled);
        scheduler.Advance(elapsed, stripped, simulation);
        overlay.Step();

        return stripped;
    }

    private static long Ticks(double ms) => (long)Math.Round(ms * Stopwatch.Frequency / 1000.0);
}
