using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Diagnostics;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

public sealed class DebugOverlayTests
{
    private const double StepSeconds = 0.1;
    private static readonly InputAction SharedAction = new("shared");

    [Fact]
    public void LeadingEdgeTogglesAndQuarantinesTheBoundButtonUntilRelease()
    {
        FixedStepScheduler scheduler = CreateScheduler();
        DebugOverlay overlay = new(Key.Grave, scheduler);

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
        RecordingSimulation simulation = new();
        FixedStepScheduler scheduler = new(
            StepSeconds,
            5,
            new ActionBindings().Bind(SharedAction, Key.Grave));
        DebugOverlay overlay = new(Key.Grave, scheduler);

        DeviceSnapshot opening = DeviceSnapshot.Of(Key.Grave);
        opening = overlay.Observe(opening);
        scheduler.Advance(StepSeconds, opening, simulation);

        DeviceSnapshot release = DeviceSnapshot.Empty;
        release = overlay.Observe(release);
        scheduler.Advance(StepSeconds, release, simulation);

        DeviceSnapshot closing = DeviceSnapshot.Of(Key.Grave);
        closing = overlay.Observe(closing);
        scheduler.Advance(StepSeconds, closing, simulation);

        Assert.Single(simulation.Steps);
        Assert.False(simulation.Steps[0].Pressed);
        Assert.False(simulation.Steps[0].Held);
    }

    [Fact]
    public void ReboundPadButtonOpensAndIsQuarantined()
    {
        FixedStepScheduler scheduler = CreateScheduler();
        DebugOverlay overlay = new((InputButton)PadButton.South, scheduler);
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
        DebugOverlay overlay = new(InputButton.None, scheduler);
        DeviceSnapshot snapshot = DeviceSnapshot.Of(Key.Grave);

        snapshot = overlay.Observe(snapshot);

        Assert.False(overlay.IsOpen);
        Assert.False(scheduler.Held);
        Assert.Equal(DeviceSnapshot.Of(Key.Grave), snapshot);
    }

    [Fact]
    public void ClosedOverlay_DoesNotStepItsHost()
    {
        using DebugOverlay overlay = new(Key.Grave, CreateScheduler());

        overlay.Step(DeviceSnapshot.Empty, null!);

        Assert.False(overlay.IsOpen);
        Assert.Equal(0, overlay.DebugHost.Tick);
    }

    [Fact]
    public void Refit_ChangesOnlyTheOverlayRunAndReanchorsItsLabelAtOneAndTwoTimes()
    {
        Run gameRun = new() { Canvas = new System.Numerics.Vector2(100f, 50f) };
        using SceneHost game = CreateHost(gameRun);
        using DebugOverlay overlay = new(Key.Grave, CreateScheduler(), game);

        overlay.Refit((640, 720));

        Assert.Equal(new System.Numerics.Vector2(640f, 720f), overlay.DebugHost.Run.Canvas);
        Assert.Equal(new System.Numerics.Vector2(4f, 4f), overlay.DebugScene.Label.Bounds.Position);
        Assert.Same(gameRun, game.Run);
        Assert.NotSame(game.Run, overlay.DebugHost.Run);

        overlay.Refit((1280, 1080));

        Assert.Equal(new System.Numerics.Vector2(640f, 540f), overlay.DebugHost.Run.Canvas);
        Assert.Equal(new System.Numerics.Vector2(4f, 4f), overlay.DebugScene.Label.Bounds.Position);
        Assert.Equal(new System.Numerics.Vector2(100f, 50f), game.Run.Canvas);
    }

    [Fact]
    public void ReadoutUsesTheCurrentSceneTickAndStepCountAndCachesUnchangedText()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        DebugOverlay overlay = new(Key.Grave, scheduler, host);

        DeviceSnapshot opening = DeviceSnapshot.Of(Key.Grave);
        opening = overlay.Observe(opening);
        scheduler.Advance(StepSeconds, opening, host);

        Assert.Equal("ReadoutScene  tick 0  steps 0", overlay.Readout);
        string cached = overlay.Readout;
        Assert.Same(cached, overlay.Readout);

        DeviceSnapshot release = DeviceSnapshot.Empty;
        release = overlay.Observe(release);
        DeviceSnapshot closing = DeviceSnapshot.Of(Key.Grave);
        closing = overlay.Observe(closing);
        scheduler.Advance(StepSeconds, closing, host);

        Assert.Equal("ReadoutScene  tick 1  steps 1", overlay.Readout);
    }

    private static FixedStepScheduler CreateScheduler() =>
        new(StepSeconds, 5, new ActionBindings());

    private static SceneHost CreateHost(Run? run = null) =>
        new(
            SceneTransition.ToScene(typeof(ReadoutScene), null),
            static (in SceneTransition _) => new ReadoutScene(),
            run ?? new Run());

    private sealed class ReadoutScene : Scene;

    private sealed class RecordingSimulation : ISimulation
    {
        public List<RecordedStep> Steps { get; } = [];

        public bool ExitRequested => false;

        public FrameView View { get; } = new();

        public void Step(in StepContext context) =>
            Steps.Add(new RecordedStep(
                context.Input.WasPressed(SharedAction),
                context.Input.IsHeld(SharedAction)));
    }

    private readonly record struct RecordedStep(bool Pressed, bool Held);
}
