using Capsule.Input;
using Capsule.Runtime.Input;

namespace Capsule.Tests.Runtime;

public sealed class GamepadRumbleTests
{
    private const double Frame = 1.0 / 60.0;

    private static readonly RumbleLevel Buzz = new(0.8f, 0.4f, 0f, 0f);

    [Fact]
    public void AnUnchangedLevel_IsWrittenOnce()
    {
        (GamepadRumble applier, List<(int Player, RumbleLevel Level)> writes) = Applier();

        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 0, Frame);
        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 0, Frame);
        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 0, Frame);

        Assert.Equal([(0, Buzz)], writes);
    }

    [Fact]
    public void LosingFocus_RestsTheMotorsAndRegainingItRewritesTheLevel()
    {
        (GamepadRumble applier, List<(int Player, RumbleLevel Level)> writes) = Applier();

        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 0, Frame);
        applier.Apply(Buzz, focused: false, connected: true, padActive: true, player: 0, Frame);
        applier.Apply(Buzz, focused: false, connected: true, padActive: true, player: 0, Frame);
        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 0, Frame);

        Assert.Equal([(0, Buzz), (0, RumbleLevel.Zero), (0, Buzz)], writes);
    }

    [Fact]
    public void AChangedPlayerIndex_RestsTheOldPadBeforeDrivingTheNew()
    {
        (GamepadRumble applier, List<(int Player, RumbleLevel Level)> writes) = Applier();

        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 0, Frame);
        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 2, Frame);

        Assert.Equal([(0, Buzz), (0, RumbleLevel.Zero), (2, Buzz)], writes);
    }

    [Fact]
    public void ANonZeroLevel_IsRefreshedAfterASecondAndAZeroLevelNever()
    {
        (GamepadRumble applier, List<(int Player, RumbleLevel Level)> writes) = Applier();

        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 0, Frame);
        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 0, 0.5);
        Assert.Single(writes);

        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 0, 0.5);
        Assert.Equal([(0, Buzz), (0, Buzz)], writes);

        applier.Apply(RumbleLevel.Zero, focused: true, connected: true, padActive: true, player: 0, Frame);
        applier.Apply(RumbleLevel.Zero, focused: true, connected: true, padActive: true, player: 0, 2.0);
        Assert.Equal([(0, Buzz), (0, Buzz), (0, RumbleLevel.Zero)], writes);
    }

    [Fact]
    public void AKeyboardStep_RestsTheMotorsAndThePadReturningRewritesTheLevel()
    {
        (GamepadRumble applier, List<(int Player, RumbleLevel Level)> writes) = Applier();

        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 0, Frame);
        applier.Apply(Buzz, focused: true, connected: true, padActive: false, player: 0, Frame);
        applier.Apply(Buzz, focused: true, connected: true, padActive: false, player: 0, Frame);
        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 0, Frame);

        Assert.Equal([(0, Buzz), (0, RumbleLevel.Zero), (0, Buzz)], writes);
    }

    [Fact]
    public void Silence_RestsTheMotorsOnceAndAgainDoesNothing()
    {
        (GamepadRumble applier, List<(int Player, RumbleLevel Level)> writes) = Applier();

        applier.Silence();
        applier.Apply(Buzz, focused: true, connected: true, padActive: true, player: 1, Frame);
        applier.Silence();
        applier.Silence();

        Assert.Equal([(1, Buzz), (1, RumbleLevel.Zero)], writes);
    }

    private static (GamepadRumble, List<(int, RumbleLevel)>) Applier()
    {
        List<(int, RumbleLevel)> writes = [];

        return (new GamepadRumble((player, level) => writes.Add((player, level))), writes);
    }
}
