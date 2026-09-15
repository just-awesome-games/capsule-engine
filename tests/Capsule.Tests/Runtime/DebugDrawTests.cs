using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Runtime;

// The debug draw buffer is one process-wide slot, attached by whichever overlay was built last.
[Collection(LogSinkCollection.Name)]
public sealed class DebugDrawTests
{
    private const double StepSeconds = 0.1;
    private const string Hitboxes = "hitboxes";
    private const string Labels = "labels";

    [Fact]
    public void AnEmittingStep_ListsItsChannelsOffAndATogglePutsTheDrawsOnTheWorldListWithoutAStep()
    {
        using SceneHost host = CreateHost(new DrawingScene(emitOnTick: 0, steps: 1));
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        FrameView view = overlay.Host.Simulation.View;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("No debug draw channel has emitted yet", overlay.Scene.Status);
        Assert.Equal(1, overlay.Scene.Depth);

        Press(overlay, scheduler, host, Key.Right);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Debug Draw", overlay.Scene.Title);
        Assert.Equal(["[ ] hitboxes", "[ ] labels"], Labels(overlay.Scene));
        Assert.Equal(1, scheduler.Tick);
        Assert.True(view.Lines.IsEmpty);
        Assert.True(view.Sprites.IsEmpty);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal(["[x] hitboxes", "[ ] labels"], Labels(overlay.Scene));
        Assert.Equal(2, overlay.Scene.Depth);
        Assert.Equal(0, overlay.Scene.FocusedIndex);
        Assert.Equal(1, scheduler.Tick);
        Assert.Equal(1, view.Lines.Length);
        Assert.True(view.Sprites.IsEmpty);
        Assert.Equal(new Vector2(3f, 4f), view.Lines[0].B);

        // The line named no colour: it takes its channel's, white until the channel is given one,
        // and follows a change on the overlay's next frame with no game step between.
        Assert.Equal(ColorRgba.White, view.Lines[0].Color);
        DebugDraw.SetColor(Hitboxes, ColorRgba.Orange);
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Assert.Equal(ColorRgba.Orange, view.Lines[0].Color);
        Assert.Equal(1, scheduler.Tick);
        DebugDraw.SetColor(Hitboxes, ColorRgba.White);

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal(["[x] hitboxes", "[x] labels"], Labels(overlay.Scene));
        Assert.Equal(1, overlay.Scene.FocusedIndex);
        Assert.Equal(1, view.Lines.Length);
        Assert.Equal("hi".Length, view.Sprites.Length);

        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal(["[ ] hitboxes", "[x] labels"], Labels(overlay.Scene));
        Assert.True(view.Lines.IsEmpty);

        // Leaving and re-entering finds the same menu, and a channel that first emits while the
        // submenu is open — the scene emits on "extra" from its second step — gains a row in place.
        Menu menu = overlay.Scene.Current;
        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Same(menu, overlay.Scene.Current);

        Press(overlay, scheduler, host, Key.Right);

        Assert.Same(menu, overlay.Scene.Current);
        Assert.Equal(["[ ] extra", "[ ] hitboxes", "[x] labels"], Labels(overlay.Scene));
        Assert.Equal(2, overlay.Scene.Depth);
    }

    // A left click over a row of the Debug Draw menu, opened by its hotkey, toggles that row's
    // channel; the click is the menu's and never reaches the game.
    [Fact]
    public void ALeftClickOverARow_TogglesItsChannelAndIsWithheldFromTheGame()
    {
        using SceneHost host = CreateHost(new DrawingScene(emitOnTick: 0, steps: 1));
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.Right);
        Press(overlay, scheduler, host, Key.D);

        Assert.Equal("Debug Draw", overlay.Scene.Title);
        Assert.False(overlay.IsChannelEnabled(Labels));

        // Readout, title, blank, then the rows: the second row is the fifth line.
        float lineHeight = BitmapFont.Default.LineHeight;
        Vector2 secondRow = new(6f, 4f + (4f * lineHeight) + (lineHeight / 2f));
        DeviceSnapshot game = overlay.Observe(DeviceSnapshot.Empty.WithPointer(secondRow).With(MouseButton.Left));
        scheduler.Advance(StepSeconds, game, host);
        overlay.Step();

        Assert.False(game.IsDown(MouseButton.Left));
        Assert.True(overlay.IsChannelEnabled(Labels));
        Assert.False(overlay.IsChannelEnabled(Hitboxes));
        Assert.Equal(["[ ] hitboxes", "[x] labels"], Labels(overlay.Scene));
        Assert.Equal(1, scheduler.Tick);
    }

    [Fact]
    public void ADraw_StaysForItsStepsCountedInTicksAndThenLeaves()
    {
        using SceneHost host = CreateHost(new DrawingScene(emitOnTick: 0, steps: 2));
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        FrameView view = overlay.Host.Simulation.View;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.Right);
        overlay.ToggleChannel(Hitboxes);
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);

        Assert.Equal(1, scheduler.Tick);
        Assert.Equal(1, view.Lines.Length);

        for (int frame = 0; frame < 3; frame++)
        {
            Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        }

        Assert.Equal(1, view.Lines.Length);

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(2, scheduler.Tick);
        Assert.Equal(1, view.Lines.Length);

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(3, scheduler.Tick);
        Assert.True(view.Lines.IsEmpty);
    }

    [Fact]
    public void AClosedOverlay_KeepsDrawingAnEnabledChannelWithoutSteppingItsMenu()
    {
        using SceneHost host = CreateHost(new DrawingScene(emitOnTick: null, steps: 1));
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        FrameView view = overlay.Host.Simulation.View;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.Right);
        overlay.ToggleChannel(Hitboxes);
        Press(overlay, scheduler, host, Key.Grave);
        long menuTick = overlay.Host.Tick;

        Assert.False(overlay.IsOpen);
        Assert.False(scheduler.Held);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);

        Assert.Equal(4, scheduler.Tick);
        Assert.Equal(menuTick, overlay.Host.Tick);
        Assert.Equal(1, view.Lines.Length);
        Assert.Equal(new Vector2(3f, 2f), view.Lines[0].A);
    }

    [Fact]
    public void EachShape_DecomposesIntoItsSegmentsOnTheWorldList()
    {
        using SceneHost host = CreateHost(new ShapesScene());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        FrameView view = overlay.Host.Simulation.View;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.Right);
        overlay.ToggleChannel(Hitboxes);
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);

        ReadOnlySpan<LineIntent> lines = view.Lines;
        Assert.Equal(4 + 24 + 26 + 3, lines.Length);

        // The rect's four edges, clockwise from its top-left corner.
        Assert.Equal((new Vector2(0f, 0f), new Vector2(4f, 0f)), (lines[0].A, lines[0].B));
        Assert.Equal((new Vector2(4f, 0f), new Vector2(4f, 2f)), (lines[1].A, lines[1].B));
        Assert.Equal((new Vector2(4f, 2f), new Vector2(0f, 2f)), (lines[2].A, lines[2].B));
        Assert.Equal((new Vector2(0f, 2f), new Vector2(0f, 0f)), (lines[3].A, lines[3].B));

        // The circle's segments chain, every end at the radius from the centre.
        Vector2 center = new(10f, 10f);
        for (int index = 4; index < 28; index++)
        {
            Assert.Equal(2f, Vector2.Distance(center, lines[index].A), 3);
            Assert.Equal(2f, Vector2.Distance(center, lines[index].B), 3);
            Assert.Equal(lines[index].B, lines[index + 1 < 28 ? index + 1 : 4].A);
        }

        // The capsule: a half arc around each end at its radius, then the two sides one radius
        // either side of the axis from start to end.
        for (int index = 28; index < 40; index++)
        {
            Assert.Equal(1f, Vector2.Distance(new Vector2(6f, 0f), lines[index].A), 3);
        }

        for (int index = 40; index < 52; index++)
        {
            Assert.Equal(1f, Vector2.Distance(new Vector2(0f, 0f), lines[index].A), 3);
        }

        Assert.Equal((new Vector2(0f, 1f), new Vector2(6f, 1f)), (lines[52].A, lines[52].B));
        Assert.Equal((new Vector2(0f, -1f), new Vector2(6f, -1f)), (lines[53].A, lines[53].B));

        // The polygon closes back on its first corner.
        Assert.Equal((new Vector2(20f, 20f), new Vector2(21f, 20f)), (lines[54].A, lines[54].B));
        Assert.Equal((new Vector2(21f, 20f), new Vector2(21f, 21f)), (lines[55].A, lines[55].B));
        Assert.Equal((new Vector2(21f, 21f), new Vector2(20f, 20f)), (lines[56].A, lines[56].B));
    }

    [Fact]
    public void WithNoBufferAttached_ACallAllocatesAndRecordsNothing()
    {
        DebugDraw.UseBuffer(null);
        Span<Vector2> corners = [Vector2.Zero, Vector2.UnitX, Vector2.One];

        long before = GC.GetAllocatedBytesForCurrentThread();
        DebugDraw.Line(Hitboxes, Vector2.Zero, Vector2.One, ColorRgba.White);
        DebugDraw.Polygon(Hitboxes, corners, ColorRgba.White);
        DebugDraw.Text(Labels, Vector2.Zero, "hi", ColorRgba.White);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);

        using OverlayHost overlay = new(Key.Grave, CreateScheduler(), new RecordingSimulation());

        Assert.Empty(overlay.Channels);
    }

    private static SceneHost CreateHost(Scene scene) =>
        new(SceneTransition.ToScene(scene.GetType(), null), (in SceneTransition _) => scene, new Run());

    // Emits a hitbox line and a label on the named tick, or on every tick when none is named. The
    // line's start is the tick, so a test can tell which step's draw it is looking at. A third
    // channel first emits on the step after the named one.
    private sealed class DrawingScene(long? emitOnTick, int steps) : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            if (emitOnTick is { } tick && context.Tick != tick)
            {
                if (context.Tick == tick + 1)
                {
                    DebugDraw.Text("extra", Vector2.Zero, "x", ColorRgba.White, steps);
                }

                return;
            }

            DebugDraw.Line(Hitboxes, new Vector2(context.Tick, 2f), new Vector2(3f, 4f), steps: steps);
            DebugDraw.Text(Labels, Vector2.Zero, "hi", ColorRgba.White, steps);
        }
    }

    // One of every shape on the hitbox channel, on the first tick only.
    private sealed class ShapesScene : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            if (context.Tick != 0)
            {
                return;
            }

            DebugDraw.Rect(Hitboxes, new Rect(0f, 0f, 4f, 2f), ColorRgba.White);
            DebugDraw.Circle(Hitboxes, new Vector2(10f, 10f), 2f, ColorRgba.White);
            DebugDraw.Capsule(Hitboxes, new Vector2(0f, 0f), new Vector2(6f, 0f), 1f, ColorRgba.White);
            DebugDraw.Polygon(Hitboxes, [new Vector2(20f, 20f), new Vector2(21f, 20f), new Vector2(21f, 21f)], ColorRgba.White);
        }
    }
}
