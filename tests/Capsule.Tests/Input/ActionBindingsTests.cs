using Capsule.Input;

namespace Capsule.Tests.Input;

// The binding members of InputConfiguration, read through the table behind them.
public sealed class ActionBindingsTests
{
    private static readonly InputAction Jump = new("Jump");
    private static readonly AxisAction Move = new("Move");

    [Fact]
    public void AnUnboundAction_HasNoButtonsAndReadsNothing()
    {
        InputConfiguration input = new();

        Assert.True(input.ButtonsFor(Jump).IsEmpty);
        Assert.False(input.Bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.Space)));
        Assert.Equal(0f, input.Bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, 1f)));
    }

    [Fact]
    public void BindingAnActionTwice_UnionsTheButtons()
    {
        InputConfiguration input = new InputConfiguration()
            .Bind(Jump, Key.Space)
            .Bind(Jump, Key.W, Key.Space)
            .Bind(Jump, PadButton.South);

        InputButton[] expected = [Key.Space, Key.W, PadButton.South];

        Assert.Equal(expected, input.ButtonsFor(Jump).ToArray());
    }

    [Theory]
    [MemberData(nameof(BadBindings))]
    public void Bind_RejectsBadConfigurations(Action<InputConfiguration> badBind)
    {
        Assert.Throws<ArgumentException>(() => badBind(new InputConfiguration()));
    }

    public static IEnumerable<object[]> BadBindings()
    {
        yield return [new Action<InputConfiguration>(b => b.Bind(default, Key.Space))];
        yield return [new Action<InputConfiguration>(b => b.Bind(new InputAction("  "), Key.Space))];
        yield return [new Action<InputConfiguration>(b => b.Bind(Jump))];
        yield return [new Action<InputConfiguration>(b => b.Bind(Jump, Key.Space, Key.None))];
        yield return [new Action<InputConfiguration>(b => b.Bind(Jump, (Key)DeviceSnapshot.Capacity))];
        yield return [new Action<InputConfiguration>(b => b.Bind(Move, PadAxis.None))];
    }

    [Fact]
    public void Bind_ToAnAnalogSource_ReadsThatAxis()
    {
        InputConfiguration input = new InputConfiguration().Bind(Move, PadAxis.LeftStickX);

        Assert.Equal(0.5f, input.Bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, 0.5f)), InputFixtures.Tolerance);
        Assert.Equal(-1f, input.Bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, -1f)), InputFixtures.Tolerance);
        Assert.Equal(0f, input.Bindings.AxisValue(Move, Stick(PadAxis.LeftStickY, 1f)), InputFixtures.Tolerance);
    }

    [Fact]
    public void Bind_ToADigitalPair_ReadsMinusOneZeroOrOne()
    {
        InputConfiguration input = new InputConfiguration().Bind(Move, Key.A, Key.D);

        Assert.Equal(-1f, input.Bindings.AxisValue(Move, DeviceSnapshot.Of(Key.A)), InputFixtures.Tolerance);
        Assert.Equal(1f, input.Bindings.AxisValue(Move, DeviceSnapshot.Of(Key.D)), InputFixtures.Tolerance);
        Assert.Equal(0f, input.Bindings.AxisValue(Move, DeviceSnapshot.Of(Key.A, Key.D)), InputFixtures.Tolerance);
    }

    [Fact]
    public void EveryContribution_SumsAndTheSumClampsToTheUnitRange()
    {
        InputConfiguration input = new InputConfiguration()
            .Bind(Move, PadAxis.LeftStickX)
            .Bind(Move, PadAxis.RightStickX)
            .Bind(Move, Key.A, Key.D);

        DeviceSnapshot halfLeftStick = Stick(PadAxis.LeftStickX, -0.5f);
        DeviceSnapshot bothSticksRight = Stick(PadAxis.LeftStickX, 1f).WithAxis(PadAxis.RightStickX, 1f);

        Assert.Equal(0.5f, input.Bindings.AxisValue(Move, halfLeftStick.With(Key.D)), InputFixtures.Tolerance);
        Assert.Equal(1f, input.Bindings.AxisValue(Move, bothSticksRight.With(Key.D)), InputFixtures.Tolerance);
        Assert.Equal(-1f, input.Bindings.AxisValue(Move, halfLeftStick.With(Key.A).WithAxis(PadAxis.RightStickX, -1f)), InputFixtures.Tolerance);
    }

    [Fact]
    public void RegisteringTheSameContributionTwice_DoesNotDoubleCountIt()
    {
        InputConfiguration input = new InputConfiguration()
            .Bind(Move, PadAxis.LeftStickX)
            .Bind(Move, PadAxis.LeftStickX)
            .Bind(Move, Key.A, Key.D)
            .Bind(Move, Key.A, Key.D);

        Assert.Equal(0.5f, input.Bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, 0.5f)), InputFixtures.Tolerance);
        Assert.Equal(1f, input.Bindings.AxisValue(Move, DeviceSnapshot.Of(Key.D)), InputFixtures.Tolerance);
    }

    [Fact]
    public void Rebind_DropsThePreviousButtons()
    {
        InputConfiguration input = new InputConfiguration().Bind(Jump, Key.Space, PadButton.South);

        input.Rebind(Jump, Key.F);

        InputButton[] expected = [Key.F];
        Assert.Equal(expected, input.ButtonsFor(Jump).ToArray());
        Assert.False(input.Bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.Space)));
        Assert.True(input.Bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.F)));
    }

    [Fact]
    public void Unbind_LeavesTheActionUnbound()
    {
        InputConfiguration input = new InputConfiguration().Bind(Jump, Key.Space).Bind(Move, PadAxis.LeftStickX);

        input.Unbind(Jump).Unbind(Move);

        Assert.True(input.ButtonsFor(Jump).IsEmpty);
        Assert.False(input.Bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.Space)));
        Assert.Equal(0f, input.Bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, 1f)));
    }

    [Fact]
    public void ABooleanAndAnAxisActionOfTheSameName_DoNotCollide()
    {
        InputConfiguration input = new InputConfiguration()
            .Bind(Jump, Key.Space)
            .Bind(new AxisAction("Jump"), PadAxis.LeftTrigger);

        Assert.True(input.Bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.Space)));
        Assert.Equal(0f, input.Bindings.AxisValue(new AxisAction("Jump"), DeviceSnapshot.Of(Key.Space)));
    }

    private static DeviceSnapshot Stick(PadAxis axis, float value) => DeviceSnapshot.Empty.WithAxis(axis, value);
}
