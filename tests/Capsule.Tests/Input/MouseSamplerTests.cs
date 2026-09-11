using System.Numerics;
using Capsule.Input;
using Capsule.Runtime.Input;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Input;

public sealed class MouseSamplerTests
{
    // The OS read itself no spec can stand up; the focus rule around it is what these hold.
    [Fact]
    public void AnInactiveWindow_HoldsThePointerAndAddsNoButton()
    {
        MouseSampler sampler = new();
        ScreenPlacement placement = new(new Vector2(8f, 4f), 2f);
        DeviceSnapshot keyboard = DeviceSnapshot.Of(Key.Space);

        sampler.Resume(keyboard, new Vector2(20f, 30f), MouseButton.Left);
        DeviceSnapshot sampled = sampler.SampleOnto(keyboard, placement, windowActive: false);

        Assert.Equal(new Vector2(20f, 30f), sampled.Pointer);
        Assert.True(sampled.IsDown(Key.Space));
        Assert.False(sampled.IsDown(MouseButton.Left));
    }

    [Fact]
    public void AButtonHeldAcrossAFocusReturn_IsNotAPressUntilItIsReleasedAndPressedAgain()
    {
        MouseSampler sampler = new();
        DeviceSnapshot none = DeviceSnapshot.Empty;

        // Launched with nothing held, then pressed in focus.
        sampler.Resume(none, Vector2.Zero);
        Assert.True(sampler.Resume(none, Vector2.Zero, MouseButton.Left).IsDown(MouseButton.Left));
        Assert.False(sampler.Suspend(none).IsDown(MouseButton.Left));

        // Back in focus with the button still down and the pointer somewhere new: a move, no press.
        DeviceSnapshot returned = sampler.Resume(none, new Vector2(50f, 60f), MouseButton.Left);
        Assert.Equal(new Vector2(50f, 60f), returned.Pointer);
        Assert.False(returned.IsDown(MouseButton.Left));

        Assert.False(sampler.Resume(none, new Vector2(50f, 60f)).IsDown(MouseButton.Left));
        Assert.True(sampler.Resume(none, new Vector2(50f, 60f), MouseButton.Left).IsDown(MouseButton.Left));
    }

    [Fact]
    public void AButtonHeldThroughTheLaunch_IsNotAPress()
    {
        MouseSampler sampler = new();

        Assert.False(sampler.Resume(DeviceSnapshot.Empty, Vector2.Zero, MouseButton.Left).IsDown(MouseButton.Left));
        Assert.True(sampler.Resume(DeviceSnapshot.Empty, Vector2.Zero, MouseButton.Right).IsDown(MouseButton.Right));
    }
}
