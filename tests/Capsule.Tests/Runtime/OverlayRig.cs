using System.Diagnostics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Runtime;

// The overlay specs' host: one frame is intercept, advance the game, update the overlay, the order
// CapsuleGame runs them in, and a press is the key's frame followed by its release. The instance owns
// the clock, so a frame pane spec can say how long each frame took.
internal sealed class OverlayRig : IDisposable
{
    internal const double StepSeconds = 0.1;

    // An unclocked frame: sixty of them are a second.
    private const double DefaultIntervalMs = 16;
    private const double DefaultUpdateMs = 1;

    private long _ticks;
    private long _frameStart;

    // The run defaults to a RecordingScene that records nothing. The rig disposes it.
    internal OverlayRig(
        SceneHost? host = null,
        SceneRegistry? registry = null,
        InputButton? toggle = null,
        FixedStepScheduler? scheduler = null)
    {
        Host = host ?? RecordingHost();
        Scheduler = scheduler ?? CreateScheduler();
        Overlay = new DebugOverlay(toggle ?? Key.Grave, Scheduler, Host, registry, () => _ticks);
    }

    internal DebugOverlay Overlay { get; }

    internal FixedStepScheduler Scheduler { get; }

    internal SceneHost Host { get; }

    // The game frame's cost the pane is told about, which a rig with no renderer has to supply.
    internal double DrawMs { get; set; }

    internal RecordingSimulation Recording => ((RecordingScene)Host.Scene).Recorder;

    // The labels of the page the overlay last built, in order.
    internal string[] Rows()
    {
        IReadOnlyList<OverlayRow> rows = Overlay.Rows;
        string[] labels = new string[rows.Count];
        for (int index = 0; index < labels.Length; index++)
        {
            labels[index] = rows[index].Label;
        }

        return labels;
    }

    internal string Focused() => Overlay.Rows[Overlay.Focus].Label;

    internal string[] PaneLines() => Overlay.Scene.Pane.Text.Split('\n');

    internal void Open()
    {
        Press(Key.Grave);

        Assert.True(Overlay.IsOpen);
    }

    internal void Press(Key key)
    {
        Frame(DeviceSnapshot.Of(key));
        Frame();
    }

    // One unclocked frame. Returns what the game saw.
    internal DeviceSnapshot Frame(DeviceSnapshot sampled = default, double elapsedSeconds = StepSeconds) =>
        Frame(DefaultIntervalMs, DefaultUpdateMs, elapsedSeconds, sampled);

    // One frame that starts intervalMs after the last began and whose update bracket costs updateMs.
    internal DeviceSnapshot Frame(
        double intervalMs,
        double updateMs,
        double elapsedSeconds = 0,
        DeviceSnapshot sampled = default)
    {
        _frameStart += Ticks(intervalMs);
        _ticks = _frameStart;
        DeviceSnapshot stripped = Overlay.Intercept(sampled);
        _ticks += Ticks(updateMs);
        Scheduler.Advance(elapsedSeconds, stripped, Host);
        Overlay.Update(lastFrameMs: DrawMs);

        return stripped;
    }

    public void Dispose()
    {
        Overlay.Dispose();
        Host.Dispose();
    }

    internal static FixedStepScheduler CreateScheduler(ActionBindings? bindings = null) =>
        new(StepSeconds, 5, bindings ?? new ActionBindings());

    // A run of one RecordingScene reading actions.
    internal static SceneHost RecordingHost(params InputAction[] actions)
    {
        RecordingScene scene = new(actions);

        return new SceneHost(SceneTransition.ToScene(typeof(RecordingScene), null), (in SceneTransition _) => scene, new Run());
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
                    SceneTransitionKind.Named => new NamedScene(),
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
