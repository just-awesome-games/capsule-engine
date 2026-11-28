using System.Numerics;
using Capsule.Input;

namespace Capsule.Tests.Input;

// The pointer is a third bindable device: mouse buttons read as any other button, and the position
// and the wheel ride the snapshot in canvas pixels and in notches.
public sealed class PointerTests
{
    private static readonly AxisAction Zoom = new("Zoom");

    [Fact]
    public void AScrollAmount_IsKeptExactlyAndLeavesEverythingElseAlone()
    {
        DeviceSnapshot flicked = DeviceSnapshot.Of(Key.LeftControl)
            .WithPointer(new Vector2(7f, 8f))
            .WithScroll(new Vector2(-1f, 2.5f));

        Assert.Equal(new Vector2(-1f, 2.5f), flicked.Scroll);
        Assert.Equal(new Vector2(7f, 8f), flicked.Pointer);
        Assert.True(flicked.IsDown(Key.LeftControl));
        Assert.False(flicked.IsEmpty);
        Assert.NotEqual(flicked, flicked.WithScroll(Vector2.Zero));
    }

    [Fact]
    public void APointerPosition_IsKeptExactlyAndTellsTwoSnapshotsApart()
    {
        DeviceSnapshot outside = DeviceSnapshot.Empty.WithPointer(new Vector2(-40f, 5000.5f));

        Assert.Equal(new Vector2(-40f, 5000.5f), outside.Pointer);
        Assert.False(outside.IsEmpty);
        Assert.NotEqual(DeviceSnapshot.Empty, outside);
        Assert.NotEqual(outside.With(MouseButton.Middle), outside);
    }

    [Fact]
    public void ALatch_UnionsMouseButtonsAndTakesTheNewestPointer()
    {
        DeviceSnapshot clicked = DeviceSnapshot.Empty.With(MouseButton.Left).WithPointer(new Vector2(1f, 1f));
        DeviceSnapshot moved = DeviceSnapshot.Empty.With(MouseButton.Right).WithPointer(new Vector2(9f, 9f));

        DeviceSnapshot latched = clicked.LatchedWith(moved);

        Assert.True(latched.IsDown(MouseButton.Left));
        Assert.True(latched.IsDown(MouseButton.Right));
        Assert.Equal(new Vector2(9f, 9f), latched.Pointer);
    }

    // A notch is a displacement. The latch must add it where it replaces a position.
    [Fact]
    public void ALatch_SumsTheWheelRatherThanTakingTheNewest()
    {
        DeviceSnapshot first = DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 1f));
        DeviceSnapshot second = DeviceSnapshot.Empty.WithScroll(new Vector2(-2f, 3f));

        Assert.Equal(new Vector2(-2f, 4f), first.LatchedWith(second).Scroll);
        Assert.Equal(new Vector2(0f, 1f), first.LatchedWith(DeviceSnapshot.Empty).Scroll);
    }

    [Fact]
    public void ThePointer_MovesOnTheEdgeIntoAStepAndThenRests()
    {
        InputState input = new(new ActionBindings());
        input.Advance(DeviceSnapshot.Empty.WithPointer(new Vector2(10f, 10f)));
        input.Advance(DeviceSnapshot.Empty.WithPointer(new Vector2(4f, 25f)));

        Assert.Equal(new Vector2(4f, 25f), input.Pointer);
        Assert.True(input.PointerMoved);
        Assert.Equal(new Vector2(-6f, 15f), input.PointerDelta);

        input.Advance(DeviceSnapshot.Empty.WithPointer(new Vector2(4f, 25f)));

        Assert.False(input.PointerMoved);
        Assert.Equal(Vector2.Zero, input.PointerDelta);
    }

    // The canvas moving under the pointer, as a window resize or a new screen placement does, jumps the
    // pointer with no hand on the mouse. The step reads the new position and no motion. It drops any
    // real move made in the same step, and motion reads again from the next step.
    [Fact]
    public void ACanvasMovedUnderThePointer_IsNoMotionAndNoUseOfTheMouse()
    {
        InputState input = new(new ActionBindings());
        input.Seed(InputDevice.Gamepad);
        input.Advance(DeviceSnapshot.Empty);

        input.Advance(DeviceSnapshot.Empty.WithPointer(new Vector2(40f, 60f)).WithPointerRemapped(true));

        Assert.Equal(new Vector2(40f, 60f), input.Pointer);
        Assert.False(input.PointerMoved);
        Assert.Equal(Vector2.Zero, input.PointerDelta);
        Assert.Equal(InputDevice.Gamepad, input.ActiveDevice);
        Assert.False(input.ActiveDeviceChanged);

        input.Advance(DeviceSnapshot.Empty.WithPointer(new Vector2(50f, 60f)));

        Assert.True(input.PointerMoved);
        Assert.Equal(new Vector2(10f, 0f), input.PointerDelta);
        Assert.Equal(InputDevice.KeyboardMouse, input.ActiveDevice);
    }

    [Fact]
    public void TheWheel_ReadsTheStepItTurnedOnAndThenStills()
    {
        InputState input = new(new ActionBindings());
        input.Advance(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 2f)));

        Assert.Equal(new Vector2(0f, 2f), input.Scroll);

        input.Advance(DeviceSnapshot.Empty);

        Assert.Equal(Vector2.Zero, input.Scroll);
    }

    [Fact]
    public void TheWheel_BindsAsAnAxisBesideAStickAndIsNotClampedAway()
    {
        ActionBindings bindings = new ActionBindings()
            .Bind(Zoom, MouseAxis.ScrollY)
            .Bind(Zoom, PadAxis.RightStickY);

        InputState input = new(bindings);
        input.Advance(DeviceSnapshot.Empty.WithAxis(PadAxis.RightStickY, 1f));

        Assert.Equal(1f, input.Axis(Zoom), InputFixtures.Tolerance);

        // The stick stays bounded while three notches in one step read as three.
        input.Advance(DeviceSnapshot.Empty.WithAxis(PadAxis.RightStickY, 1f).WithScroll(new Vector2(5f, 3f)));

        Assert.Equal(4f, input.Axis(Zoom), InputFixtures.Tolerance);

        input.Advance(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -1f)));

        Assert.Equal(-1f, input.Axis(Zoom), InputFixtures.Tolerance);
    }
}
