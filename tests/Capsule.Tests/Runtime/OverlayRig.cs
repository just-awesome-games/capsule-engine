using System.Diagnostics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Runtime;

// The overlay specs' host: one frame is observe, advance the game, step the overlay, which is the
// order CapsuleGame runs them in, and a press is the key's frame followed by its release. An instance
// owns the clock too, so a spec that reads the frame pane can say how long each frame took and how
// much of it the update bracket spent; a spec that does not reads the same frames through the static
// helpers, which drive an overlay the spec built itself.
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

    // The game frame's cost the pane is told about, which a rig with no renderer has to supply.
    internal double DrawMs { get; set; }

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

    // One frame that starts intervalMs after the last began and whose update bracket costs updateMs,
    // of which elapsedSeconds is offered to the scheduler.
    internal void Frame(
        double intervalMs,
        double updateMs,
        double elapsedSeconds = 0,
        DeviceSnapshot sampled = default)
    {
        _frameStart += Ticks(intervalMs);
        _ticks = _frameStart;
        DeviceSnapshot stripped = Overlay.Observe(sampled);
        _ticks += Ticks(updateMs);
        Scheduler.Advance(elapsedSeconds, stripped, Simulation);
        Overlay.Step(lastFrame: new RenderStats(DrawMs));
    }

    public void Dispose() => Overlay.Dispose();

    internal static FixedStepScheduler CreateScheduler(ActionBindings? bindings = null) =>
        new(StepSeconds, 5, bindings ?? new ActionBindings());

    // The labels of the page the overlay last built, in order.
    internal static string[] Rows(OverlayHost overlay)
    {
        IReadOnlyList<OverlayRow> rows = overlay.Rows;
        string[] labels = new string[rows.Count];
        for (int index = 0; index < labels.Length; index++)
        {
            labels[index] = rows[index].Label;
        }

        return labels;
    }

    // The label of the focused row.
    internal static string Focused(OverlayHost overlay) => overlay.Rows[overlay.Focus].Label;

    // Opens the overlay on the toggle's edge and releases it, so the next frame's keys are the
    // overlay's.
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

// The scenes and registry the overlay specs run over: one scene that counts its steps, two more to
// transition to, and the two that refuse a transition.
internal static class OverlayFixtures
{
    internal const string NamedDocument = "levels/named";

    internal static SceneHost CreateHost(Run? run = null, List<SceneTransition>? resolved = null) =>
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

    internal static SceneRegistry CreateRegistry() =>
        new(
            new EntityRegistry([]),
            [
                SceneRegistration.Plain(typeof(PlainScene), static _ => new PlainScene()),
                SceneRegistration.Plain(typeof(PayloadScene), static _ => new PayloadScene()),
                SceneRegistration.FromDocument(typeof(NamedScene), NamedDocument, static _ => new NamedScene()),
            ]);

    internal sealed class ReadoutScene : Scene
    {
        internal int Steps { get; private set; }

        internal bool Stopped { get; private set; }

        protected override void OnStep(in StepContext context) => Steps++;

        protected override void OnStop() => Stopped = true;
    }

    internal sealed class PlainScene : Scene;

    internal sealed class NamedScene : Scene;

    internal sealed class ExitOnStartScene : Scene
    {
        protected override void OnStart() => Run.RequestExit();
    }

    internal sealed class RequestOnStartScene : Scene
    {
        protected override void OnStart() => Run.RequestScene<PlainScene>();
    }

    internal sealed class EmptyDriver : IInputDriver
    {
        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            snapshot = DeviceSnapshot.Empty;

            return false;
        }
    }

    // Refuses to start without a payload, which is how a load that fails mid-tick is provoked.
    internal sealed class PayloadScene : Scene
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
