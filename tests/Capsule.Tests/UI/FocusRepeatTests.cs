using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.UI;

using static Capsule.Tests.UI.FocusFixtures;

using Menu = Capsule.Tests.UI.FocusFixtures.Menu;

namespace Capsule.Tests.UI;

public sealed class FocusRepeatTests
{
    // Edge-triggered, which is what the second step of the same held key proves.
    [Fact]
    public void AHeldDirection_MovesTheFocusOnceAndNeverRepeats()
    {
        using Menu menu = Column().Open();

        menu.Hold(Key.Down);

        Assert.Equal(1, menu.FocusedIndex);

        menu.Rest();

        Assert.Equal(1, menu.FocusedIndex);
        Assert.Empty(menu.Log);
    }

    // A held direction moves on its press, again once held past the delay, then every interval,
    // wrapping through the column as a press would; releasing it ends the run.
    [Fact]
    public void AHeldDirection_RepeatsAfterTheDelayThenEveryIntervalUntilReleased()
    {
        using Menu menu = Triple().Open();
        menu.Navigator.RepeatDelay = 4;
        menu.Navigator.RepeatInterval = 2;

        menu.Hold(Key.Down);
        Assert.Equal(1, menu.FocusedIndex);

        menu.Rest().Rest().Rest();
        Assert.Equal(1, menu.FocusedIndex);

        menu.Rest();
        Assert.Equal(2, menu.FocusedIndex);

        menu.Rest();
        Assert.Equal(2, menu.FocusedIndex);

        menu.Rest();
        Assert.Equal(0, menu.FocusedIndex);

        menu.Release(Key.Down).Rest().Rest().Rest().Rest();
        Assert.Equal(0, menu.FocusedIndex);

        menu.Hold(Key.Down);
        Assert.Equal(1, menu.FocusedIndex);

        menu.Rest().Rest().Rest().Rest();
        Assert.Equal(2, menu.FocusedIndex);
    }

    [Fact]
    public void APressOnAnotherDirection_RestartsTheRepeatCounter()
    {
        using Menu menu = Triple().Open();
        menu.Navigator.RepeatDelay = 4;
        menu.Navigator.RepeatInterval = 2;

        menu.Hold(Key.Down).Rest().Rest();
        Assert.Equal(1, menu.FocusedIndex);

        // Up is read first of the two, so its press moves up; the counter starts over from it.
        menu.Hold(Key.Up);
        Assert.Equal(0, menu.FocusedIndex);

        menu.Rest().Rest().Rest();
        Assert.Equal(0, menu.FocusedIndex);

        menu.Rest();
        Assert.Equal(2, menu.FocusedIndex);
    }

    [Fact]
    public void AnIntervalOfZero_NeverRepeatsAndAHeldConfirmNeverRePresses()
    {
        using Menu menu = Triple().Open();
        menu.Navigator.RepeatDelay = 0;
        menu.Navigator.RepeatInterval = 0;

        menu.Hold(Key.Down);
        for (int step = 0; step < 40; step++)
        {
            menu.Rest();
        }

        Assert.Equal(1, menu.FocusedIndex);

        menu.Release(Key.Down);
        menu.Navigator.RepeatInterval = 1;
        menu.Hold(Key.Enter);
        Assert.Equal(["pressed 1"], menu.Log);

        for (int step = 0; step < 40; step++)
        {
            menu.Rest();
            Assert.Empty(menu.Log);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => menu.Navigator.RepeatDelay = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => menu.Navigator.RepeatInterval = -1);
    }
}
