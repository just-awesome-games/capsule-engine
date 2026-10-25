using Capsule.Input;
using Capsule.Runtime.Input;

namespace Capsule.Tests.Runtime;

public sealed class GamepadRumbleTests
{
    private const double Frame = 1.0 / 60.0;

    // SDL joystick ids, which count up from 1 as pads connect.
    private const uint Pad = 3;
    private const uint OtherPad = 5;

    private static readonly RumbleLevel Buzz = new(0.8f, 0.4f, 0f, 0f);

    [Fact]
    public void AnUnchangedLevel_IsWrittenOnce()
    {
        (GamepadRumble applier, List<(uint Pad, RumbleLevel Level)> writes) = Applier();

        applier.Apply(Buzz, focused: true, padActive: true, runHeld: false, pad: Pad, Frame);
        applier.Apply(Buzz, focused: true, padActive: true, runHeld: false, pad: Pad, Frame);
        applier.Apply(Buzz, focused: true, padActive: true, runHeld: false, pad: Pad, Frame);

        Assert.Equal([(Pad, Buzz)], writes);
    }

    // Lost focus, an unplugged pad, a keyboard step and a held run each rest the motors.
    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void AClosedGate_RestsTheMotorsAndReopeningItRewritesTheLevel(bool focused, bool connected, bool padActive, bool runHeld)
    {
        (GamepadRumble applier, List<(uint Pad, RumbleLevel Level)> writes) = Applier();

        applier.Apply(Buzz, focused: true, padActive: true, runHeld: false, pad: Pad, Frame);
        applier.Apply(Buzz, focused, padActive, runHeld, pad: connected ? Pad : 0, Frame);
        applier.Apply(Buzz, focused, padActive, runHeld, pad: connected ? Pad : 0, 2.0);
        applier.Apply(Buzz, focused: true, padActive: true, runHeld: false, pad: Pad, Frame);

        Assert.Equal([(Pad, Buzz), (Pad, RumbleLevel.Zero), (Pad, Buzz)], writes);
    }

    [Fact]
    public void AChangedActivePad_RestsTheOldPadBeforeDrivingTheNew()
    {
        (GamepadRumble applier, List<(uint Pad, RumbleLevel Level)> writes) = Applier();

        applier.Apply(Buzz, focused: true, padActive: true, runHeld: false, pad: Pad, Frame);
        applier.Apply(Buzz, focused: true, padActive: true, runHeld: false, pad: OtherPad, Frame);

        Assert.Equal([(Pad, Buzz), (Pad, RumbleLevel.Zero), (OtherPad, Buzz)], writes);
    }

    [Fact]
    public void ANonZeroLevel_IsRefreshedAfterASecondAndAZeroLevelNever()
    {
        (GamepadRumble applier, List<(uint Pad, RumbleLevel Level)> writes) = Applier();

        applier.Apply(Buzz, focused: true, padActive: true, runHeld: false, pad: Pad, Frame);
        applier.Apply(Buzz, focused: true, padActive: true, runHeld: false, pad: Pad, 0.5);
        Assert.Single(writes);

        applier.Apply(Buzz, focused: true, padActive: true, runHeld: false, pad: Pad, 0.5);
        Assert.Equal([(Pad, Buzz), (Pad, Buzz)], writes);

        applier.Apply(RumbleLevel.Zero, focused: true, padActive: true, runHeld: false, pad: Pad, Frame);
        applier.Apply(RumbleLevel.Zero, focused: true, padActive: true, runHeld: false, pad: Pad, 2.0);
        Assert.Equal([(Pad, Buzz), (Pad, Buzz), (Pad, RumbleLevel.Zero)], writes);
    }

    [Fact]
    public void Silence_RestsTheMotorsOnceAndAgainDoesNothing()
    {
        (GamepadRumble applier, List<(uint Pad, RumbleLevel Level)> writes) = Applier();

        applier.Silence();
        applier.Apply(Buzz, focused: true, padActive: true, runHeld: false, pad: Pad, Frame);
        applier.Silence();
        applier.Silence();

        Assert.Equal([(Pad, Buzz), (Pad, RumbleLevel.Zero)], writes);
    }

    private static (GamepadRumble, List<(uint, RumbleLevel)>) Applier()
    {
        List<(uint, RumbleLevel)> writes = [];

        return (new GamepadRumble((pad, level) => writes.Add((pad, level))), writes);
    }
}
