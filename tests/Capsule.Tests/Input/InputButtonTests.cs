using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class InputButtonTests
{
    [Fact]
    public void TheNoneOfEitherDevice_IsNone()
    {
        Assert.True(((InputButton)Key.None).IsNone);
        Assert.True(((InputButton)PadButton.None).IsNone);
        Assert.Equal(InputButton.None, (InputButton)Key.None);
        Assert.Equal(InputButton.None, (InputButton)PadButton.None);
    }

    [Fact]
    public void IsDown_ReadsTheDeviceTheButtonBelongsTo()
    {
        DeviceSnapshot snapshot = DeviceSnapshot.Of(Key.Space).With(PadButton.South);

        Assert.True(((InputButton)Key.Space).IsDown(snapshot));
        Assert.True(((InputButton)PadButton.South).IsDown(snapshot));
        Assert.False(((InputButton)Key.W).IsDown(snapshot));
        Assert.False(((InputButton)PadButton.North).IsDown(snapshot));
    }

    [Fact]
    public void None_IsNeverDown()
    {
        DeviceSnapshot snapshot = DeviceSnapshot.Of(Key.Space).With(PadButton.South);

        Assert.False(InputButton.None.IsDown(snapshot));
        Assert.False(InputButton.None.IsDown(DeviceSnapshot.Empty));
    }

    [Fact]
    public void AStickDirection_IsDownOnlyPastThePressPointItsWay()
    {
        DeviceSnapshot pushed = DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickY, 0.6f);

        Assert.True(((InputButton)StickDirection.LeftStickUp).IsDown(pushed));
        Assert.False(((InputButton)StickDirection.LeftStickDown).IsDown(pushed));
        Assert.False(((InputButton)StickDirection.RightStickUp).IsDown(pushed));

        DeviceSnapshot insideDeadband = DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickY, 0.4f);

        Assert.False(((InputButton)StickDirection.LeftStickUp).IsDown(insideDeadband));
        Assert.False(((InputButton)StickDirection.LeftStickDown).IsDown(insideDeadband));
    }

    [Fact]
    public void AHeldStickDirection_IsPressedOnceAndNeverRepeats()
    {
        InputAction menuUp = new("menu-up");
        ActionBindings bindings = new ActionBindings().Bind(menuUp, StickDirection.LeftStickUp);
        InputState input = new(bindings);
        DeviceSnapshot pushed = DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickY, 0.6f);

        int presses = 0;
        for (int step = 0; step < 3; step++)
        {
            input.Advance(pushed);
            presses += input.WasPressed(menuUp) ? 1 : 0;
        }

        Assert.Equal(1, presses);
        Assert.True(input.IsHeld(menuUp));
    }
}
