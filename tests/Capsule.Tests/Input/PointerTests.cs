using System.Numerics;
using Capsule.Input;

namespace Capsule.Tests.Input;

// The pointer is a third bindable device: mouse buttons read as any other button, and the position
// and the wheel ride the snapshot in canvas pixels and in notches.
public sealed class PointerTests
{
    private const float Tolerance = 1e-6f;

    private static readonly InputAction Confirm = new("Confirm");
    private static readonly AxisAction Zoom = new("Zoom");

    [Fact]
    public void AnEmptySnapshot_HoldsNoMouseButtonAndRestsOnTheOrigin()
    {
        Assert.Equal(Vector2.Zero, DeviceSnapshot.Empty.Pointer);
        Assert.Equal(Vector2.Zero, DeviceSnapshot.Empty.Scroll);
        Assert.False(DeviceSnapshot.Empty.IsDown(MouseButton.Left));
        Assert.True(DeviceSnapshot.Empty.IsEmpty);
    }

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
        Assert.Equal(flicked.GetHashCode(), flicked.WithScroll(new Vector2(-1f, 2.5f)).GetHashCode());
    }

    [Fact]
    public void AScrollAmountThatIsNotFinite_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DeviceSnapshot.Empty.WithScroll(new Vector2(0f, float.PositiveInfinity)));
    }

    [Fact]
    public void AMouseButton_IsHeldUntilItIsReleased()
    {
        DeviceSnapshot held = DeviceSnapshot.Empty.With(MouseButton.Right);

        Assert.True(held.IsDown(MouseButton.Right));
        Assert.False(held.IsDown(MouseButton.Left));
        Assert.False(held.Without(MouseButton.Right).IsDown(MouseButton.Right));
    }

    [Fact]
    public void APointerPosition_IsKeptExactlyAndUnclamped()
    {
        DeviceSnapshot outside = DeviceSnapshot.Empty.WithPointer(new Vector2(-40f, 5000.5f));

        Assert.Equal(new Vector2(-40f, 5000.5f), outside.Pointer);
        Assert.False(outside.IsEmpty);
    }

    [Fact]
    public void APointerPositionThatIsNotFinite_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.WithPointer(new Vector2(float.NaN, 0f)));
    }

    [Fact]
    public void TwoSnapshots_DifferOnTheirPointerAndTheirMouseButtons()
    {
        DeviceSnapshot at = DeviceSnapshot.Empty.WithPointer(new Vector2(3f, 4f));

        Assert.NotEqual(at, DeviceSnapshot.Empty);
        Assert.Equal(at, DeviceSnapshot.Empty.WithPointer(new Vector2(3f, 4f)));
        Assert.Equal(at.GetHashCode(), DeviceSnapshot.Empty.WithPointer(new Vector2(3f, 4f)).GetHashCode());
        Assert.NotEqual(at.With(MouseButton.Middle), at);
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

    // A notch is a displacement, so the latch must add it where it replaces a position.
    [Fact]
    public void ALatch_SumsTheWheelRatherThanTakingTheNewest()
    {
        DeviceSnapshot first = DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 1f));
        DeviceSnapshot second = DeviceSnapshot.Empty.WithScroll(new Vector2(-2f, 3f));

        Assert.Equal(new Vector2(-2f, 4f), first.LatchedWith(second).Scroll);
        Assert.Equal(new Vector2(0f, 1f), first.LatchedWith(DeviceSnapshot.Empty).Scroll);
    }

    [Fact]
    public void AMouseButton_BindsBesideAKeyAndAPadButton()
    {
        ActionBindings bindings = new();
        bindings.Bind(Confirm, Key.Enter, PadButton.South, MouseButton.Left);

        InputState input = new(bindings);
        input.Advance(DeviceSnapshot.Empty);
        input.Advance(DeviceSnapshot.Empty.With(MouseButton.Left));

        Assert.True(input.IsHeld(Confirm));
        Assert.True(input.WasPressed(Confirm));

        input.Advance(DeviceSnapshot.Empty);

        Assert.True(input.WasReleased(Confirm));
    }

    [Fact]
    public void ThePointer_MovesOnTheEdgeIntoAStepAndThenRests()
    {
        InputState input = new(new ActionBindings());
        input.Advance(DeviceSnapshot.Empty.WithPointer(new Vector2(2f, 2f)));

        Assert.Equal(new Vector2(2f, 2f), input.Pointer);
        Assert.True(input.PointerMoved);

        input.Advance(DeviceSnapshot.Empty.WithPointer(new Vector2(2f, 2f)));

        Assert.False(input.PointerMoved);
    }

    [Fact]
    public void ThePointerDelta_IsHowFarItMovedIntoTheStepAndZeroWhileItRests()
    {
        InputState input = new(new ActionBindings());
        input.Advance(DeviceSnapshot.Empty.WithPointer(new Vector2(10f, 10f)));
        input.Advance(DeviceSnapshot.Empty.WithPointer(new Vector2(4f, 25f)));

        Assert.Equal(new Vector2(-6f, 15f), input.PointerDelta);

        input.Advance(DeviceSnapshot.Empty.WithPointer(new Vector2(4f, 25f)));

        Assert.Equal(Vector2.Zero, input.PointerDelta);
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
            .BindAxis(Zoom, MouseAxis.ScrollY)
            .BindAxis(Zoom, PadAxis.RightStickY);

        InputState input = new(bindings);
        input.Advance(DeviceSnapshot.Empty.WithAxis(PadAxis.RightStickY, 1f));

        Assert.Equal(1f, input.Axis(Zoom), Tolerance);

        // The stick stays bounded while three notches in one step read as three.
        input.Advance(DeviceSnapshot.Empty.WithAxis(PadAxis.RightStickY, 1f).WithScroll(new Vector2(5f, 3f)));

        Assert.Equal(4f, input.Axis(Zoom), Tolerance);

        input.Advance(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -1f)));

        Assert.Equal(-1f, input.Axis(Zoom), Tolerance);
    }

    [Fact]
    public void TheWheel_CannotBeBoundToAnUndefinedAxis()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionBindings().BindAxis(Zoom, (MouseAxis)7));
    }
}
