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
    public void DeviceSnapshot_WithoutStickDirectionKeepsTheOppositeDirectionWhenPossible()
    {
        DeviceSnapshot snapshot = DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, -1f);

        DeviceSnapshot released = snapshot.Without((InputButton)StickDirection.LeftStickRight);

        Assert.False(((InputButton)StickDirection.LeftStickRight).IsDown(released));
        Assert.True(((InputButton)StickDirection.LeftStickLeft).IsDown(released));
    }

    [Theory]
    [MemberData(nameof(RoundTripButtons))]
    public void ToStringAndParse_RoundTrip(InputButton button) =>
        Assert.Equal(button, InputButton.Parse(button.ToString()));

    public static IEnumerable<object[]> RoundTripButtons()
    {
        yield return [InputButton.None];
        yield return [(InputButton)Key.Space];
        yield return [(InputButton)PadButton.South];
        yield return [(InputButton)MouseButton.Left];
        yield return [(InputButton)StickDirection.LeftStickUp];
    }

    [Fact]
    public void Parse_RejectsANumericEnumString()
    {
        Assert.False(InputButton.TryParse("Key.999", out _));
        Assert.Throws<FormatException>(() => InputButton.Parse("Key.999"));
    }
}
