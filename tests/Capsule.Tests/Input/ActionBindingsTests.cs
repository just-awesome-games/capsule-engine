using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class ActionBindingsTests
{
    private static readonly InputAction Jump = new("Jump");
    private static readonly AxisAction Move = new("Move");

    [Fact]
    public void AnUnboundAction_HasNoButtonsAndReadsNothing()
    {
        ActionBindings bindings = new();

        Assert.True(bindings.ButtonsFor(Jump).IsEmpty);
        Assert.False(bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.Space)));
        Assert.Equal(0f, bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, 1f)));
    }

    [Fact]
    public void Bind_MixesKeysAndPadButtonsInOneCall()
    {
        ActionBindings bindings = new ActionBindings().Bind(Jump, Key.Space, PadButton.South);

        Assert.True(bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.Space)));
        Assert.True(bindings.IsAnyDown(Jump, DeviceSnapshot.Empty.With(PadButton.South)));
        Assert.False(bindings.IsAnyDown(Jump, DeviceSnapshot.Empty.With(PadButton.North)));
    }

    [Fact]
    public void BindingAnActionTwice_UnionsTheButtons()
    {
        ActionBindings bindings = new ActionBindings()
            .Bind(Jump, Key.Space)
            .Bind(Jump, Key.W, Key.Space)
            .Bind(Jump, PadButton.South);

        InputButton[] expected = [Key.Space, Key.W, PadButton.South];

        Assert.Equal(expected, bindings.ButtonsFor(Jump).ToArray());
    }

    [Theory]
    [MemberData(nameof(BadBindings))]
    public void Bind_RejectsBadConfigurations(Action<ActionBindings> badBind)
    {
        Assert.Throws<ArgumentException>(() => badBind(new ActionBindings()));
    }

    public static IEnumerable<object[]> BadBindings()
    {
        yield return [new Action<ActionBindings>(b => b.Bind(default, Key.Space))];
        yield return [new Action<ActionBindings>(b => b.Bind(new InputAction("  "), Key.Space))];
        yield return [new Action<ActionBindings>(b => b.Bind(Jump))];
        yield return [new Action<ActionBindings>(b => b.Bind(Jump, Key.Space, Key.None))];
        yield return [new Action<ActionBindings>(b => b.Bind(Jump, PadButton.None))];
        yield return [new Action<ActionBindings>(b => b.Bind(Jump, InputButton.None))];
        yield return [new Action<ActionBindings>(b => b.BindAxis(Move, PadAxis.None))];
    }

    [Fact]
    public void BindAxis_ToAnAnalogSource_ReadsThatAxis()
    {
        ActionBindings bindings = new ActionBindings().BindAxis(Move, PadAxis.LeftStickX);

        Assert.Equal(0.5f, bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, 0.5f)), InputFixtures.Tolerance);
        Assert.Equal(-1f, bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, -1f)), InputFixtures.Tolerance);
        Assert.Equal(0f, bindings.AxisValue(Move, Stick(PadAxis.LeftStickY, 1f)), InputFixtures.Tolerance);
    }

    [Fact]
    public void BindAxis_ToADigitalPair_ReadsMinusOneZeroOrOne()
    {
        ActionBindings bindings = new ActionBindings().BindAxis(Move, Key.A, Key.D);

        Assert.Equal(-1f, bindings.AxisValue(Move, DeviceSnapshot.Of(Key.A)), InputFixtures.Tolerance);
        Assert.Equal(1f, bindings.AxisValue(Move, DeviceSnapshot.Of(Key.D)), InputFixtures.Tolerance);
        Assert.Equal(0f, bindings.AxisValue(Move, DeviceSnapshot.Empty), InputFixtures.Tolerance);
    }

    [Fact]
    public void ADigitalPairHeldBothWays_Cancels()
    {
        ActionBindings bindings = new ActionBindings().BindAxis(Move, Key.A, Key.D);

        Assert.Equal(0f, bindings.AxisValue(Move, DeviceSnapshot.Of(Key.A, Key.D)), InputFixtures.Tolerance);
    }

    [Fact]
    public void EveryContributionSums()
    {
        ActionBindings bindings = new ActionBindings()
            .BindAxis(Move, PadAxis.LeftStickX)
            .BindAxis(Move, Key.A, Key.D);

        DeviceSnapshot halfLeftStick = Stick(PadAxis.LeftStickX, -0.5f);

        Assert.Equal(-0.5f, bindings.AxisValue(Move, halfLeftStick), InputFixtures.Tolerance);
        Assert.Equal(0.5f, bindings.AxisValue(Move, halfLeftStick.With(Key.D)), InputFixtures.Tolerance);
        Assert.Equal(-1f, bindings.AxisValue(Move, halfLeftStick.With(Key.A)), InputFixtures.Tolerance);
    }

    [Fact]
    public void TheSum_ClampsToTheUnitRange()
    {
        ActionBindings bindings = new ActionBindings()
            .BindAxis(Move, PadAxis.LeftStickX)
            .BindAxis(Move, PadAxis.RightStickX)
            .BindAxis(Move, Key.A, Key.D);

        DeviceSnapshot bothSticksRight = Stick(PadAxis.LeftStickX, 1f).WithAxis(PadAxis.RightStickX, 1f);

        Assert.Equal(1f, bindings.AxisValue(Move, bothSticksRight), InputFixtures.Tolerance);
        Assert.Equal(1f, bindings.AxisValue(Move, bothSticksRight.With(Key.D)), InputFixtures.Tolerance);
        Assert.Equal(-1f, bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, -1f).WithAxis(PadAxis.RightStickX, -1f)), InputFixtures.Tolerance);
    }

    [Fact]
    public void RegisteringTheSameContributionTwice_DoesNotDoubleCountIt()
    {
        ActionBindings bindings = new ActionBindings()
            .BindAxis(Move, PadAxis.LeftStickX)
            .BindAxis(Move, PadAxis.LeftStickX)
            .BindAxis(Move, Key.A, Key.D)
            .BindAxis(Move, Key.A, Key.D);

        Assert.Equal(0.5f, bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, 0.5f)), InputFixtures.Tolerance);
        Assert.Equal(1f, bindings.AxisValue(Move, DeviceSnapshot.Of(Key.D)), InputFixtures.Tolerance);
    }

    [Fact]
    public void ABooleanAndAnAxisActionOfTheSameName_DoNotCollide()
    {
        ActionBindings bindings = new ActionBindings()
            .Bind(Jump, Key.Space)
            .BindAxis(new AxisAction("Jump"), PadAxis.LeftTrigger);

        Assert.True(bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.Space)));
        Assert.Equal(0f, bindings.AxisValue(new AxisAction("Jump"), DeviceSnapshot.Of(Key.Space)));
    }

    private static DeviceSnapshot Stick(PadAxis axis, float value) => DeviceSnapshot.Empty.WithAxis(axis, value);
}
