using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class InputStateTests
{
    private static readonly InputAction Jump = new("Jump");
    private static readonly AxisAction Move = new("Move");

    private static InputState Bound(params ReadOnlySpan<InputButton> buttons) =>
        new(new ActionBindings().Bind(Jump, buttons));

    // The whole edge walk: the press on the first step down, held only while it stays down, the
    // release on the first step up, and nothing on the step after that.
    [Fact]
    public void AKeyHeldAndThenReleased_EdgesOnceEachWay()
    {
        InputState input = Bound(Key.Space);
        DeviceSnapshot down = DeviceSnapshot.Of(Key.Space);

        input.Advance(down);

        Assert.True(input.IsHeld(Jump));
        Assert.True(input.WasPressed(Jump));
        Assert.False(input.WasReleased(Jump));

        input.Advance(down);

        Assert.True(input.IsHeld(Jump));
        Assert.False(input.WasPressed(Jump));
        Assert.False(input.WasReleased(Jump));

        input.Advance(DeviceSnapshot.Empty);

        Assert.False(input.IsHeld(Jump));
        Assert.False(input.WasPressed(Jump));
        Assert.True(input.WasReleased(Jump));

        input.Advance(DeviceSnapshot.Empty);

        Assert.False(input.WasReleased(Jump));
    }

    [Fact]
    public void SwappingBetweenBoundKeys_IsNotAnEdge()
    {
        InputState input = Bound(Key.Space, Key.W);

        input.Advance(DeviceSnapshot.Of(Key.Space));
        input.Advance(DeviceSnapshot.Of(Key.Space, Key.W));
        input.Advance(DeviceSnapshot.Of(Key.W));

        Assert.True(input.IsHeld(Jump));
        Assert.False(input.WasPressed(Jump));
        Assert.False(input.WasReleased(Jump));
    }

    // A default snapshot has window focus, and a repeated snapshot raises no second edge.
    [Fact]
    public void AWindowFocusLossAndRegain_EdgeOnceEachWay()
    {
        InputState input = new(new ActionBindings());
        DeviceSnapshot unfocused = DeviceSnapshot.Empty.WithWindowFocus(false);

        input.Advance(DeviceSnapshot.Empty);

        Assert.True(input.HasWindowFocus);
        Assert.False(input.WindowFocusLost);

        input.Advance(unfocused);

        Assert.False(input.HasWindowFocus);
        Assert.True(input.WindowFocusLost);
        Assert.False(input.WindowFocusGained);

        input.Advance(unfocused);

        Assert.False(input.WindowFocusLost);

        input.Advance(DeviceSnapshot.Empty);

        Assert.True(input.WindowFocusGained);
        Assert.False(input.WindowFocusLost);

        input.Advance(DeviceSnapshot.Empty);

        Assert.False(input.WindowFocusGained);
    }

    [Fact]
    public void Axis_ReadsTheCurrentStepOnly()
    {
        InputState input = new(new ActionBindings().BindAxis(Move, PadAxis.LeftStickX));

        Assert.Equal(0f, input.Axis(Move));

        input.Advance(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, -0.75f));

        Assert.Equal(-0.75f, input.Axis(Move), InputFixtures.Tolerance);

        input.Advance(DeviceSnapshot.Empty);

        Assert.Equal(0f, input.Axis(Move), InputFixtures.Tolerance);
    }

    [Fact]
    public void WasAnyPressed_ReadsTheEdgeAndTheDocumentedOrder()
    {
        InputState input = new(new ActionBindings());

        input.Advance(DeviceSnapshot.Empty);

        Assert.False(input.WasAnyPressed(out InputButton none));
        Assert.Equal(InputButton.None, none);

        input.Advance(DeviceSnapshot.Of(Key.Space));

        Assert.True(input.WasAnyPressed(out InputButton pressed));
        Assert.Equal((InputButton)Key.Space, pressed);

        input.Advance(DeviceSnapshot.Of(Key.Space));

        Assert.False(input.WasAnyPressed(out _));

        input.Advance(DeviceSnapshot.Empty);
        input.Advance(DeviceSnapshot.Of(Key.Space).With(PadButton.South));

        Assert.True(input.WasAnyPressed(out InputButton both));
        Assert.Equal((InputButton)Key.Space, both);
    }
}
