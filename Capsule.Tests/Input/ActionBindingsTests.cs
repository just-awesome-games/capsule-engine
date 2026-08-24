using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class ActionBindingsTests
{
    private const float Tolerance = 1e-6f;

    private static readonly InputAction Jump = new("Jump");
    private static readonly AxisAction Move = new("Move");

    [Fact]
    public void AnUnboundAction_HasNoButtonsAndIsNeverDown()
    {
        ActionBindings bindings = new();

        Assert.True(bindings.ButtonsFor(Jump).IsEmpty);
        Assert.False(bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.Space)));
    }

    [Fact]
    public void Bind_MakesEveryBoundKeyStandForTheAction()
    {
        ActionBindings bindings = new ActionBindings().Bind(Jump, Key.Space, Key.W);

        Assert.True(bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.Space)));
        Assert.True(bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.W)));
        Assert.False(bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.A)));
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

    [Fact]
    public void Bind_RejectsAnUnnamedAction()
    {
        Assert.Throws<ArgumentException>(() => new ActionBindings().Bind(default, Key.Space));
        Assert.Throws<ArgumentException>(() => new ActionBindings().Bind(new InputAction("  "), Key.Space));
    }

    [Fact]
    public void Bind_RejectsNoButtons()
    {
        Assert.Throws<ArgumentException>(() => new ActionBindings().Bind(Jump));
    }

    [Fact]
    public void Bind_RejectsAButtonThatNamesNothing()
    {
        Assert.Throws<ArgumentException>(() => new ActionBindings().Bind(Jump, Key.Space, Key.None));
        Assert.Throws<ArgumentException>(() => new ActionBindings().Bind(Jump, PadButton.None));
        Assert.Throws<ArgumentException>(() => new ActionBindings().Bind(Jump, InputButton.None));
    }

    [Fact]
    public void AnUnboundAxisAction_ReadsZero()
    {
        ActionBindings bindings = new();

        Assert.Equal(0f, bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, 1f)));
    }

    [Fact]
    public void BindAxis_ToAnAnalogSource_ReadsThatAxis()
    {
        ActionBindings bindings = new ActionBindings().BindAxis(Move, PadAxis.LeftStickX);

        Assert.Equal(0.5f, bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, 0.5f)), Tolerance);
        Assert.Equal(-1f, bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, -1f)), Tolerance);
        Assert.Equal(0f, bindings.AxisValue(Move, Stick(PadAxis.LeftStickY, 1f)), Tolerance);
    }

    [Fact]
    public void BindAxis_ToADigitalPair_ReadsMinusOneZeroOrOne()
    {
        ActionBindings bindings = new ActionBindings().BindAxis(Move, Key.A, Key.D);

        Assert.Equal(-1f, bindings.AxisValue(Move, DeviceSnapshot.Of(Key.A)), Tolerance);
        Assert.Equal(1f, bindings.AxisValue(Move, DeviceSnapshot.Of(Key.D)), Tolerance);
        Assert.Equal(0f, bindings.AxisValue(Move, DeviceSnapshot.Empty), Tolerance);
    }

    [Fact]
    public void ADigitalPairHeldBothWays_Cancels()
    {
        ActionBindings bindings = new ActionBindings().BindAxis(Move, Key.A, Key.D);

        Assert.Equal(0f, bindings.AxisValue(Move, DeviceSnapshot.Of(Key.A, Key.D)), Tolerance);
    }

    [Fact]
    public void EveryContributionSums()
    {
        ActionBindings bindings = new ActionBindings()
            .BindAxis(Move, PadAxis.LeftStickX)
            .BindAxis(Move, Key.A, Key.D);

        DeviceSnapshot halfLeftStick = Stick(PadAxis.LeftStickX, -0.5f);

        Assert.Equal(-0.5f, bindings.AxisValue(Move, halfLeftStick), Tolerance);
        Assert.Equal(0.5f, bindings.AxisValue(Move, halfLeftStick.With(Key.D)), Tolerance);
        Assert.Equal(-1f, bindings.AxisValue(Move, halfLeftStick.With(Key.A)), Tolerance);
    }

    [Fact]
    public void TheSum_ClampsToTheUnitRange()
    {
        ActionBindings bindings = new ActionBindings()
            .BindAxis(Move, PadAxis.LeftStickX)
            .BindAxis(Move, PadAxis.RightStickX)
            .BindAxis(Move, Key.A, Key.D);

        DeviceSnapshot bothSticksRight = Stick(PadAxis.LeftStickX, 1f).WithAxis(PadAxis.RightStickX, 1f);

        Assert.Equal(1f, bindings.AxisValue(Move, bothSticksRight), Tolerance);
        Assert.Equal(1f, bindings.AxisValue(Move, bothSticksRight.With(Key.D)), Tolerance);
        Assert.Equal(-1f, bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, -1f).WithAxis(PadAxis.RightStickX, -1f)), Tolerance);
    }

    [Fact]
    public void RegisteringTheSameContributionTwice_DoesNotDoubleCountIt()
    {
        ActionBindings bindings = new ActionBindings()
            .BindAxis(Move, PadAxis.LeftStickX)
            .BindAxis(Move, PadAxis.LeftStickX)
            .BindAxis(Move, Key.A, Key.D)
            .BindAxis(Move, Key.A, Key.D);

        Assert.Equal(0.5f, bindings.AxisValue(Move, Stick(PadAxis.LeftStickX, 0.5f)), Tolerance);
        Assert.Equal(1f, bindings.AxisValue(Move, DeviceSnapshot.Of(Key.D)), Tolerance);
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

    [Fact]
    public void BindAxis_RejectsTheNoneAxis()
    {
        Assert.Throws<ArgumentException>(() => new ActionBindings().BindAxis(Move, PadAxis.None));
    }

    private static DeviceSnapshot Stick(PadAxis axis, float value) => DeviceSnapshot.Empty.WithAxis(axis, value);
}
