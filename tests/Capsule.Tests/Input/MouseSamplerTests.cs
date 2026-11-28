using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Input;

namespace Capsule.Tests.Input;

public sealed class MouseSamplerTests
{
    // The OS read itself no spec can stand up. The focus rule around it and the presses and notches
    // the event feed adds are what these hold. InputFeedTests holds the feed's reading of SDL's events.
    [Fact]
    public void AnInactiveWindow_HoldsThePointerAndAddsNoButton()
    {
        MouseSampler sampler = new();
        ScreenPlacement placement = new(new Vector2(8f, 4f), 2f);
        DeviceSnapshot keyboard = DeviceSnapshot.Of(Key.Space);

        sampler.Resume(keyboard, new Vector2(20f, 30f), ScreenPlacement.Identity, Vector2.Zero, [MouseButton.Left], []);
        DeviceSnapshot sampled = sampler.SampleOnto(keyboard, placement, 1f, windowActive: false, new InputEvents(new TriggerEdges()));

        Assert.Equal(new Vector2(20f, 30f), sampled.Pointer);
        Assert.True(sampled.IsDown(Key.Space));
        Assert.False(sampled.IsDown(MouseButton.Left));
    }

    // A click shorter than a frame is down on the sample after it, released before it, and up on the next.
    [Fact]
    public void AButtonPressedAndReleasedBetweenSamples_IsDownForOneSample()
    {
        MouseSampler sampler = new();
        DeviceSnapshot none = DeviceSnapshot.Empty;
        sampler.Resume(none, Vector2.Zero, ScreenPlacement.Identity, Vector2.Zero, [], []);

        Assert.True(sampler.Resume(none, Vector2.Zero, ScreenPlacement.Identity, Vector2.Zero, [], [MouseButton.Right]).IsDown(MouseButton.Right));
        Assert.False(sampler.Resume(none, Vector2.Zero, ScreenPlacement.Identity, Vector2.Zero, [], []).IsDown(MouseButton.Right));
    }

    [Fact]
    public void AButtonHeldAcrossAFocusReturn_IsNotAPressUntilItIsReleasedOrPressedAgain()
    {
        MouseSampler sampler = new();
        DeviceSnapshot none = DeviceSnapshot.Empty;

        // Launched with nothing held, then pressed in focus.
        sampler.Resume(none, Vector2.Zero, ScreenPlacement.Identity, Vector2.Zero, [], []);
        Assert.True(sampler.Resume(none, Vector2.Zero, ScreenPlacement.Identity, Vector2.Zero, [MouseButton.Left], [MouseButton.Left]).IsDown(MouseButton.Left));
        Assert.False(sampler.Suspend(none).IsDown(MouseButton.Left));

        // Back in focus with the button still down and the pointer somewhere new: a move, no press.
        DeviceSnapshot returned = sampler.Resume(none, new Vector2(50f, 60f), ScreenPlacement.Identity, Vector2.Zero, [MouseButton.Left], []);
        Assert.Equal(new Vector2(50f, 60f), returned.Pointer);
        Assert.False(returned.IsDown(MouseButton.Left));
        Assert.False(sampler.Resume(none, new Vector2(50f, 60f), ScreenPlacement.Identity, Vector2.Zero, [MouseButton.Left], []).IsDown(MouseButton.Left));

        // Released and pressed again between two samples: held both times, and the press unmasks it.
        Assert.True(sampler.Resume(none, new Vector2(50f, 60f), ScreenPlacement.Identity, Vector2.Zero, [MouseButton.Left], [MouseButton.Left]).IsDown(MouseButton.Left));

        // And a release seen by a sample unmasks it the same way.
        sampler.Suspend(none);
        sampler.Resume(none, Vector2.Zero, ScreenPlacement.Identity, Vector2.Zero, [MouseButton.Left], []);
        Assert.False(sampler.Resume(none, Vector2.Zero, ScreenPlacement.Identity, Vector2.Zero, [], []).IsDown(MouseButton.Left));
        Assert.True(sampler.Resume(none, Vector2.Zero, ScreenPlacement.Identity, Vector2.Zero, [MouseButton.Left], [MouseButton.Left]).IsDown(MouseButton.Left));
    }

    // A still mouse under a new placement reads a new canvas position, which the sample marks remapped.
    // The mouse moves freely while the window is away. The return's sample is no remap.
    [Fact]
    public void ANewPlacement_RemapsThePointerButAFocusReturnDoesNot()
    {
        MouseSampler sampler = new();
        DeviceSnapshot none = DeviceSnapshot.Empty;
        Vector2 still = new(100f, 100f);
        ScreenPlacement fitted = new(Vector2.Zero, 2f);
        ScreenPlacement whole = new(new Vector2(40f, 10f), 3f);

        Assert.False(sampler.Resume(none, still, fitted, Vector2.Zero, [], []).PointerRemapped);

        DeviceSnapshot remapped = sampler.Resume(none, still, whole, Vector2.Zero, [], []);
        Assert.True(remapped.PointerRemapped);
        Assert.Equal(new Vector2(20f, 30f), remapped.Pointer);
        Assert.False(sampler.Resume(none, still, whole, Vector2.Zero, [], []).PointerRemapped);

        sampler.Suspend(none);
        Assert.False(sampler.Resume(none, still, fitted, Vector2.Zero, [], []).PointerRemapped);
    }

    // The OS can report a still mouse's place in a moved window a sample after the placement changed.
    [Fact]
    public void AStillMouse_ThatTheWindowReportsASampleLate_IsRemapped()
    {
        MouseSampler sampler = new();
        DeviceSnapshot none = DeviceSnapshot.Empty;
        ScreenPlacement windowed = ScreenPlacement.Identity;
        ScreenPlacement fullscreen = new(new Vector2(-1000f, 0f), 2f);
        sampler.Resume(none, new Vector2(500f, 300f), windowed, Vector2.Zero, [], []);

        Assert.True(sampler.Resume(none, new Vector2(500f, 300f), fullscreen, Vector2.Zero, [], []).PointerRemapped);
        DeviceSnapshot late = sampler.Resume(none, new Vector2(1700f, 700f), fullscreen, Vector2.Zero, [], []);
        Assert.True(late.PointerRemapped);
        Assert.Equal(fullscreen.ToCanvas(new Vector2(1700f, 700f)), late.Pointer);

        // From the sample after that, the pointer moving is the mouse moving.
        Assert.False(sampler.Resume(none, new Vector2(1710f, 700f), fullscreen, Vector2.Zero, [], []).PointerRemapped);
    }

    [Fact]
    public void TheWheelTurnedBeforeTheFirstSampleOrWhileUnfocused_IsNoNotch()
    {
        MouseSampler sampler = new();
        DeviceSnapshot none = DeviceSnapshot.Empty;

        Assert.Equal(Vector2.Zero, sampler.Resume(none, Vector2.Zero, ScreenPlacement.Identity, new Vector2(3f, 3f), [], []).Scroll);
        Assert.Equal(new Vector2(1f, 2f), sampler.Resume(none, Vector2.Zero, ScreenPlacement.Identity, new Vector2(1f, 2f), [], []).Scroll);

        Assert.Equal(Vector2.Zero, sampler.Suspend(none).Scroll);

        // The turns that reach the return's sample are where the wheel now rests.
        Assert.Equal(Vector2.Zero, sampler.Resume(none, Vector2.Zero, ScreenPlacement.Identity, new Vector2(0f, 50f), [], []).Scroll);

        // From there the wheel reads normally again, a notch down reading negative.
        Assert.Equal(new Vector2(0f, -1f), sampler.Resume(none, Vector2.Zero, ScreenPlacement.Identity, new Vector2(0f, -1f), [], []).Scroll);
    }

    [Fact]
    public void AButtonHeldOrPressedBeforeTheFirstSample_IsNotAPress()
    {
        MouseSampler sampler = new();

        DeviceSnapshot first = sampler.Resume(DeviceSnapshot.Empty, Vector2.Zero, ScreenPlacement.Identity, Vector2.Zero, [MouseButton.Left], [MouseButton.Middle]);
        Assert.False(first.IsDown(MouseButton.Left));
        Assert.False(first.IsDown(MouseButton.Middle));

        Assert.True(sampler.Resume(DeviceSnapshot.Empty, Vector2.Zero, ScreenPlacement.Identity, Vector2.Zero, [MouseButton.Right], []).IsDown(MouseButton.Right));
    }
}
