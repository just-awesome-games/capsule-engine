using System.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

public sealed class FramePaneTests
{
    private const double StepSeconds = 0.1;
    private static readonly ColorRgba Highlight = new(255, 255, 255, 64);

    [Fact]
    public void AFreshOverlay_SteppedClosed_DrawsNothing()
    {
        using Rig rig = new();

        Assert.Empty(rig.Overlay.Host.Simulation.View.ScreenSprites.ToArray());

        rig.Frame(intervalMs: 16, updateMs: 1);

        Assert.False(rig.Overlay.IsOpen);
        Assert.Empty(rig.Overlay.Host.Simulation.View.ScreenSprites.ToArray());
    }

    [Fact]
    public void TheToggle_LastsThePlaySessionAcrossCloseHideAndRestore()
    {
        using Rig rig = new();
        OverlayHost overlay = rig.Overlay;

        rig.Open();
        rig.Press(Key.F);
        Assert.True(overlay.IsFramePaneOn);

        rig.Press(Key.Grave);
        Assert.False(overlay.IsOpen);
        Assert.True(overlay.IsFramePaneOn);

        rig.Open();
        Assert.True(overlay.IsFramePaneOn);

        // Hidden from inside the menu's own step: the rows are gone from the frame that hid them.
        rig.Frame(intervalMs: 16, updateMs: 1, sampled: DeviceSnapshot.Of(Key.H));
        Assert.True(overlay.IsHidden);
        Assert.True(overlay.IsFramePaneOn);
        Assert.DoesNotContain(overlay.Host.Simulation.View.ScreenSprites.ToArray(), static sprite => sprite.Color == Highlight);
        rig.Frame(intervalMs: 16, updateMs: 1);

        rig.Press(Key.Grave);
        Assert.True(overlay.IsOpen);
        Assert.True(overlay.IsFramePaneOn);
        Assert.Contains(overlay.Host.Simulation.View.ScreenSprites.ToArray(), static sprite => sprite.Color == Highlight);

        rig.Press(Key.Down);
        rig.Press(Key.Down);
        Assert.Equal("Frame Pane", overlay.Scene.Current.Items[overlay.Scene.FocusedIndex].Label);

        for (int frame = 0; frame < 70; frame++)
        {
            rig.Frame(intervalMs: 16, updateMs: 1);
        }

        Assert.StartsWith("fps   62.5", overlay.Scene.Pane.Text, StringComparison.Ordinal);

        rig.Press(Key.Enter);
        Assert.False(overlay.IsFramePaneOn);

        // Switched back on, the pane starts a fresh second from zero.
        rig.Press(Key.Enter);
        Assert.True(overlay.IsFramePaneOn);
        Assert.StartsWith("fps    0.0", overlay.Scene.Pane.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AWithdrawnMenu_LeavesOnlyThePaneInTheViewAndComesBackAtItsDepthAndFocus()
    {
        using Rig rig = new();
        OverlayHost overlay = rig.Overlay;
        OverlayScene scene = overlay.Scene;

        rig.Open();
        rig.Press(Key.Grave);
        Assert.Empty(overlay.Host.Simulation.View.ScreenSprites.ToArray());

        rig.Open();
        rig.Press(Key.Up);
        rig.Press(Key.Up);
        rig.Press(Key.F);
        Assert.Equal(1, scene.Depth);
        Assert.Equal(2, scene.FocusedIndex);

        rig.Press(Key.Grave);
        Assert.False(overlay.IsOpen);

        // One backdrop and the pane's glyphs, hanging from the canvas's top-right corner; no menu
        // backdrop at the origin, no row highlight, no row entity.
        SpriteIntent[] sprites = overlay.Host.Simulation.View.ScreenSprites.ToArray();
        int glyphs = 0;
        foreach (GlyphPlacement _ in new GlyphRun(BitmapFont.Default, scene.Pane.Text, 0, TextWrap.None, HorizontalAlignment.Left))
        {
            glyphs++;
        }

        Assert.StartsWith("fps ", scene.Pane.Text, StringComparison.Ordinal);
        Assert.Equal(1 + glyphs, sprites.Length);
        Assert.Equal(overlay.Host.Run.Canvas.X - sprites[0].Size.X, sprites[0].Position.X);
        Assert.Equal(0f, sprites[0].Position.Y);
        Assert.DoesNotContain(sprites, static sprite => sprite.Color == Highlight);
        foreach (Entity entity in scene.Entities)
        {
            Assert.IsNotType<MenuRow>(entity);
        }

        rig.Open();

        Assert.Equal(1, scene.Depth);
        Assert.Equal(2, scene.FocusedIndex);
        Assert.Equal("Frame Pane  F", scene.RowText(2));
        Assert.Contains(overlay.Host.Simulation.View.ScreenSprites.ToArray(), static sprite => sprite.Color == Highlight);
    }

    [Fact]
    public void TheFigures_ArePublishedOnceASecondFromTheWholeSecond()
    {
        using Rig rig = new();
        rig.Overlay.ToggleFramePane();
        rig.Overlay.LastFrame = new RenderStats(1.5);

        // Zeros until a second completes. The first frame has no interval to measure; the second
        // is the first sampled.
        string[] lines = rig.PaneLines();
        Assert.Equal("fps    0.0   frame   0.00 ms  max   0.00", lines[0]);
        Assert.Equal("update   0.00 ms   draw   0.00 ms", lines[1]);
        Assert.StartsWith("steps    0.0/s   gc ", lines[2], StringComparison.Ordinal);
        Assert.EndsWith(" MB", lines[2], StringComparison.Ordinal);

        rig.Frame(intervalMs: 0, updateMs: 2);
        for (int frame = 1; frame <= 10; frame++)
        {
            rig.Frame(intervalMs: frame % 2 == 1 ? 16 : 20, updateMs: 2, elapsedSeconds: frame % 2 == 1 ? 0.016 : 0.020);
        }

        Assert.Equal(lines, rig.PaneLines());

        // 27 pairs of 16 and 20 reach 988 ms; the 56th frame crosses the second at 1008 ms over 56
        // frames, in which the scheduler ran ten fixed steps.
        for (int frame = 11; frame <= 56; frame++)
        {
            rig.Frame(intervalMs: frame % 2 == 1 ? 16 : 20, updateMs: 2, elapsedSeconds: frame % 2 == 1 ? 0.016 : 0.020);
        }

        lines = rig.PaneLines();
        Assert.Equal("fps   55.6   frame  18.00 ms  max  20.00", lines[0]);
        Assert.Equal("update   2.00 ms   draw   1.50 ms", lines[1]);
        Assert.StartsWith("steps    9.9/s   gc ", lines[2], StringComparison.Ordinal);
        Assert.Equal(10, rig.Simulation.Steps);

        // Mid-second nothing moves, however the frames vary.
        rig.Frame(intervalMs: 250, updateMs: 40, elapsedSeconds: 0.25);

        Assert.Equal(lines, rig.PaneLines());

        // Ticks stepped by hand from the held menu count too: the 250 ms frame ran two, three
        // presses of Step run three more, and the second completes at 1002 ms.
        rig.Open();
        rig.Press(Key.Right);
        rig.Press(Key.Right);
        rig.Press(Key.Right);
        rig.Press(Key.Grave);
        Assert.Equal(15, rig.Simulation.Steps);
        for (int frame = 0; frame < 37; frame++)
        {
            rig.Frame(intervalMs: 16, updateMs: 2);
        }

        Assert.StartsWith("steps    5.0/s   gc ", rig.PaneLines()[2], StringComparison.Ordinal);
    }

    [Fact]
    public void OnceOnAndWarm_AFrameAllocatesNothing()
    {
        using Rig rig = new();
        rig.Overlay.ToggleFramePane();
        rig.Overlay.LastFrame = new RenderStats(1.5);

        for (int frame = 0; frame < 200; frame++)
        {
            rig.Frame(intervalMs: 16, updateMs: 1);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 200; frame++)
        {
            rig.Frame(intervalMs: 16, updateMs: 1);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.StartsWith("fps   62.5", rig.Overlay.Scene.Pane.Text, StringComparison.Ordinal);
    }

    // An overlay over an idle simulation on a clock the test advances: every frame starts a set
    // interval after the last, observes, spends the update bracket, advances the game and steps
    // the overlay.
    private sealed class Rig : IDisposable
    {
        private readonly FixedStepScheduler _scheduler = new(StepSeconds, 5, new ActionBindings());
        private long _ticks;
        private long _frameStart;

        internal Rig() => Overlay = new OverlayHost(Key.Grave, _scheduler, Simulation, timestamp: () => _ticks);

        internal OverlayHost Overlay { get; }

        internal IdleSimulation Simulation { get; } = new();

        internal string[] PaneLines() => Overlay.Scene.Pane.Text.Split('\n');

        internal void Open()
        {
            Press(Key.Grave);

            Assert.True(Overlay.IsOpen);
        }

        internal void Press(Key key)
        {
            Frame(intervalMs: 16, updateMs: 1, elapsedSeconds: 0, DeviceSnapshot.Of(key));
            Frame(intervalMs: 16, updateMs: 1, elapsedSeconds: 0, DeviceSnapshot.Empty);
        }

        internal void Frame(double intervalMs, double updateMs, double elapsedSeconds = 0, DeviceSnapshot sampled = default)
        {
            _frameStart += Ticks(intervalMs);
            _ticks = _frameStart;
            DeviceSnapshot stripped = Overlay.Observe(sampled);
            _ticks += Ticks(updateMs);
            _scheduler.Advance(elapsedSeconds, stripped, Simulation);
            Overlay.Step();
        }

        public void Dispose() => Overlay.Dispose();

        private static long Ticks(double ms) => (long)Math.Round(ms * Stopwatch.Frequency / 1000.0);
    }

    private sealed class IdleSimulation : ISimulation
    {
        public int Steps { get; private set; }

        public bool ExitRequested => false;

        public FrameView View { get; } = new();

        public void Step(in StepContext context) => Steps++;
    }
}
