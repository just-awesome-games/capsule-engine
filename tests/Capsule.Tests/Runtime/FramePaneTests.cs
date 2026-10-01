using System.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Runtime;

public sealed class FramePaneTests
{
    private static readonly ColorRgba Highlight = new(255, 255, 255, 64);

    [Fact]
    public void TheToggle_LastsThePlaySessionAcrossCloseHideAndRestore()
    {
        using OverlayRig rig = new();
        OverlayHost overlay = rig.Overlay;

        rig.Open();
        rig.Press(Key.F);
        Assert.True(overlay.IsFramePaneOn);

        rig.Press(Key.Grave);
        Assert.False(overlay.IsOpen);
        Assert.True(overlay.IsFramePaneOn);

        rig.Open();
        Assert.True(overlay.IsFramePaneOn);

        // The rows are gone from the frame that hid them.
        rig.Frame(intervalMs: 16, updateMs: 1, sampled: DeviceSnapshot.Of(Key.H));
        Assert.True(overlay.IsHidden);
        Assert.True(overlay.IsFramePaneOn);
        Assert.DoesNotContain(overlay.View.ScreenSprites.ToArray(), static sprite => sprite.Color == Highlight);
        rig.Frame(intervalMs: 16, updateMs: 1);

        rig.Press(Key.Grave);
        Assert.True(overlay.IsOpen);
        Assert.True(overlay.IsFramePaneOn);
        Assert.Contains(overlay.View.ScreenSprites.ToArray(), static sprite => sprite.Color == Highlight);

        rig.Press(Key.Down);
        rig.Press(Key.Down);
        rig.Press(Key.Down);
        Assert.Equal("Frame Pane", rig.Focused());

        for (int frame = 0; frame < 70; frame++)
        {
            rig.Frame(intervalMs: 16, updateMs: 1);
        }

        Assert.Equal(62.5, overlay.Scene.Pane.Figures.Fps, 1);

        rig.Press(Key.Enter);
        Assert.False(overlay.IsFramePaneOn);

        // Switched back on, the pane starts a fresh second from zero.
        rig.Press(Key.Enter);
        Assert.True(overlay.IsFramePaneOn);
        Assert.Equal(default, overlay.Scene.Pane.Figures);
    }

    [Fact]
    public void AWithdrawnMenu_LeavesOnlyThePaneInTheViewAndComesBackAtItsDepthAndFocus()
    {
        using OverlayRig rig = new();
        OverlayHost overlay = rig.Overlay;
        OverlayScene scene = overlay.Scene;

        rig.Frame();
        Assert.Empty(overlay.View.ScreenSprites.ToArray());

        rig.Open();
        rig.Press(Key.Grave);
        Assert.Empty(overlay.View.ScreenSprites.ToArray());

        rig.Open();
        rig.Press(Key.Up);
        rig.Press(Key.Up);
        rig.Press(Key.F);
        Assert.Equal(1, overlay.Depth);
        Assert.Equal("Frame Pane", rig.Focused());

        rig.Press(Key.Grave);
        Assert.False(overlay.IsOpen);

        // Only the pane's backdrop and glyphs, hanging from the canvas's top-right corner.
        SpriteIntent[] sprites = overlay.View.ScreenSprites.ToArray();
        int glyphs = 0;
        foreach (GlyphPlacement _ in new GlyphRun(BitmapFont.Default, scene.Pane.Text, 0, TextWrap.None, HorizontalAlignment.Left))
        {
            glyphs++;
        }

        Assert.StartsWith("fps ", scene.Pane.Text, StringComparison.Ordinal);
        Assert.Equal(1 + glyphs, sprites.Length);
        Assert.Equal(overlay.View.Canvas.X - sprites[0].Size.X, sprites[0].Position.X);
        Assert.Equal(0f, sprites[0].Position.Y);
        Assert.DoesNotContain(sprites, static sprite => sprite.Color == Highlight);

        rig.Open();

        Assert.Equal(1, overlay.Depth);
        Assert.Equal("Frame Pane", rig.Focused());
        Assert.Contains(overlay.View.ScreenSprites.ToArray(), static sprite => sprite.Color == Highlight);
    }

    [Fact]
    public void TheFigures_ArePublishedOnceASecondFromTheWholeSecond()
    {
        using OverlayRig rig = new();
        rig.Overlay.ToggleFramePane();
        rig.DrawMs = 1.5;

        // Zeros until a second completes. The first frame has no interval to measure.
        string[] lines = rig.PaneLines();
        Assert.Equal(default, rig.Overlay.Scene.Pane.Figures);

        // One line read whole, so the pane is known to be formatted rather than only measured.
        Assert.Equal("fps    0.0   frame   0.00 ms  max   0.00", lines[0]);
        Assert.EndsWith(" MB", lines[2], StringComparison.Ordinal);

        rig.Frame(intervalMs: 0, updateMs: 2);
        for (int frame = 1; frame <= 10; frame++)
        {
            rig.Frame(intervalMs: frame % 2 == 1 ? 16 : 20, updateMs: 2, elapsedSeconds: frame % 2 == 1 ? 0.016 : 0.020);
        }

        Assert.Equal(lines, rig.PaneLines());

        // 27 pairs of 16 and 20 reach 988 ms. The 56th frame crosses the second at 1008 ms, in which
        // the scheduler ran ten fixed steps.
        for (int frame = 11; frame <= 56; frame++)
        {
            rig.Frame(intervalMs: frame % 2 == 1 ? 16 : 20, updateMs: 2, elapsedSeconds: frame % 2 == 1 ? 0.016 : 0.020);
        }

        lines = rig.PaneLines();
        FrameFigures figures = rig.Overlay.Scene.Pane.Figures;
        Assert.Equal(55.6, figures.Fps, 1);
        Assert.Equal(18.0, figures.FrameMs, 2);
        Assert.Equal(20.0, figures.WorstMs, 2);
        Assert.Equal(2.0, figures.UpdateMs, 2);
        Assert.Equal(1.5, figures.DrawMs, 2);
        Assert.Equal(9.9, figures.StepsPerSecond, 1);
        Assert.Equal(10, rig.Recording.Steps);

        // Mid-second nothing moves, however the frames vary.
        rig.Frame(intervalMs: 250, updateMs: 40, elapsedSeconds: 0.25);

        Assert.Equal(lines, rig.PaneLines());

        // Ticks stepped by hand count too. The 250 ms frame ran two, three presses of Step run three
        // more, and the second completes at 1002 ms.
        rig.Open();
        rig.Press(Key.Right);
        rig.Press(Key.Right);
        rig.Press(Key.Right);
        rig.Frame(intervalMs: 16, updateMs: 2, sampled: DeviceSnapshot.Of(Key.Grave));
        rig.Frame(intervalMs: 16, updateMs: 2);
        Assert.Equal(15, rig.Recording.Steps);
        for (int frame = 0; frame < 37; frame++)
        {
            rig.Frame(intervalMs: 16, updateMs: 2);
        }

        Assert.Equal(5.0, rig.Overlay.Scene.Pane.Figures.StepsPerSecond, 1);
    }
}
