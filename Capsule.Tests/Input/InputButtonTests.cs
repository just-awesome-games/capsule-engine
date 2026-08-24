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
    public void ButtonsOfDifferentDevices_AreNeverEqual()
    {
        InputButton key = Key.A;
        InputButton pad = PadButton.South;

        Assert.NotEqual(key, pad);
        Assert.True(key != pad);
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
}
