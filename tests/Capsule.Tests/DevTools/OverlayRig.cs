using System.Diagnostics;
using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Tests.DevTools;

// The overlay specs' host: one frame is intercept, advance the game, update the overlay, the order
// WindowHost runs them in, and a press is the key's frame followed by its release. The instance owns
// the clock. A frame pane spec can say how long each frame took.
internal sealed class OverlayRig : IDisposable
{
    internal const double StepSeconds = 0.1;

    // An unclocked frame: sixty of them are a second.
    private const double DefaultIntervalMs = 16;
    private const double DefaultUpdateMs = 1;

    private long _ticks;
    private long _frameStart;

    internal DebugOverlay Overlay { get; }

    internal FixedStepScheduler Scheduler { get; }

    internal SceneHost Host { get; }

    // The game frame's cost the pane is told about, which a rig with no renderer has to supply.
    internal double DrawMs { get; set; }

    // Where the last game frame's world landed, which a rig with no renderer has to supply. A pointer
    // is in window pixels while the game layer is unplaced.
    internal WorldPlacement? World { get; set; }

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

    // Opens the panel of the entity whose scene-page row reads name, opening the overlay first when
    // it is closed.
    internal void OpenEntityPanel(string name)
    {
        if (!Overlay.IsOpen)
        {
            Open();
        }

        Press(Key.S);
        SelectRow(name);

        Assert.Equal(name, Overlay.Title);
    }

    // Moves the focus down to the row reading label, ignoring indentation, and activates it.
    internal void SelectRow(string label)
    {
        int guard = Overlay.Rows.Count;
        while (Focused().Trim() != label)
        {
            Assert.True(guard-- > 0, $"No row reads '{label}'.");
            Press(Key.Down);
        }

        Press(Key.Enter);
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
        DeviceSnapshot stripped = Overlay.Intercept(sampled, world: World);
        _ticks += Ticks(updateMs);
        Scheduler.Advance(elapsedSeconds, stripped, Host);
        Overlay.Update(lastFrameMs: DrawMs);

        return stripped;
    }

    // Places the world as the renderer would draw the game's frame now, on the scheduler's output.
    internal void PlaceWorld()
    {
        FrameView view = Host.View;
        CameraView camera = view.Camera.At(1f);
        ScreenLayout layout = FrameLayout.Layout(null, camera, view.Canvas, (int)Scheduler.Output.X, (int)Scheduler.Output.Y);
        Rect world = camera.Place(1f, layout.Span);
        Vector2 topLeft = new(world.Left, world.Top);
        World = new WorldPlacement(topLeft, layout.World, ScreenPlacement.Identity, Snap: false, ScrollLayout.Parallax(topLeft, layout.Span, camera.ScrollCenter));
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
