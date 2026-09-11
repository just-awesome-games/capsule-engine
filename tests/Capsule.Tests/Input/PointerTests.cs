using System.Numerics;
using Capsule.Input;

namespace Capsule.Tests.Input;

// The pointer is a third bindable device: mouse buttons read as any other button, and the position
// rides the snapshot in canvas pixels.
public sealed class PointerTests
{
    private static readonly InputAction Confirm = new("Confirm");

    [Fact]
    public void AnEmptySnapshot_HoldsNoMouseButtonAndRestsOnTheOrigin()
    {
        Assert.Equal(Vector2.Zero, DeviceSnapshot.Empty.Pointer);
        Assert.False(DeviceSnapshot.Empty.IsDown(MouseButton.Left));
        Assert.True(DeviceSnapshot.Empty.IsEmpty);
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
}
