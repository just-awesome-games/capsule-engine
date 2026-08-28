using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class InputStateTests
{
    private const float Tolerance = 1e-6f;

    private static readonly InputAction Jump = new("Jump");
    private static readonly AxisAction Move = new("Move");

    private static InputState Bound(params ReadOnlySpan<InputButton> buttons) =>
        new(new ActionBindings().Bind(Jump, buttons));

    [Fact]
    public void TheFirstAdvanceWithTheKeyDown_IsAPress()
    {
        InputState input = Bound(Key.Space);

        input.Advance(DeviceSnapshot.Of(Key.Space));

        Assert.True(input.IsHeld(Jump));
        Assert.True(input.WasPressed(Jump));
        Assert.False(input.WasReleased(Jump));
    }

    [Fact]
    public void HoldingAcrossSteps_IsAPressThenHeldOnly()
    {
        InputState input = Bound(Key.Space);
        DeviceSnapshot down = DeviceSnapshot.Of(Key.Space);

        input.Advance(down);
        input.Advance(down);

        Assert.True(input.IsHeld(Jump));
        Assert.False(input.WasPressed(Jump));
        Assert.False(input.WasReleased(Jump));
    }

    [Fact]
    public void ReleasingTheKey_IsAReleaseOnTheNextStepOnly()
    {
        InputState input = Bound(Key.Space);

        input.Advance(DeviceSnapshot.Of(Key.Space));
        input.Advance(DeviceSnapshot.Empty);

        Assert.False(input.IsHeld(Jump));
        Assert.False(input.WasPressed(Jump));
        Assert.True(input.WasReleased(Jump));

        input.Advance(DeviceSnapshot.Empty);

        Assert.False(input.WasReleased(Jump));
    }

    [Fact]
    public void AdvancingTwiceOnOneSnapshot_FiresTheEdgeOnTheFirstStepOnly()
    {
        InputState input = Bound(Key.Space);
        DeviceSnapshot frame = DeviceSnapshot.Of(Key.Space);
        int presses = 0;

        for (int step = 0; step < 4; step++)
        {
            input.Advance(frame);
            presses += input.WasPressed(Jump) ? 1 : 0;
        }

        Assert.Equal(1, presses);
        Assert.True(input.IsHeld(Jump));
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

    [Fact]
    public void Axis_ReadsTheCurrentStepOnly()
    {
        InputState input = new(new ActionBindings().BindAxis(Move, PadAxis.LeftStickX));

        Assert.Equal(0f, input.Axis(Move));

        input.Advance(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, -0.75f));

        Assert.Equal(-0.75f, input.Axis(Move), Tolerance);

        input.Advance(DeviceSnapshot.Empty);

        Assert.Equal(0f, input.Axis(Move), Tolerance);
    }

    [Fact]
    public void Construction_RequiresBindings()
    {
        Assert.Throws<ArgumentNullException>(() => new InputState(null!));
    }
}
