using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.UI;

using static Capsule.Tests.UI.FocusFixtures;

using Menu = Capsule.Tests.UI.FocusFixtures.Menu;

namespace Capsule.Tests.UI;

public sealed class FocusGeometryTests
{
    [Fact]
    public void TheStartingItem_TakesTheFocusAtTheNavigatorsStartAndNothingIsRaisedBeforeIt()
    {
        using Menu menu = Column();

        // Named from the moment it was added, so a game may read it before the scene starts; the item
        // itself knows nothing yet.
        Assert.Equal(0, menu.FocusedIndex);
        Assert.False(menu.At(0).IsFocused);
        Assert.Empty(menu.Log);

        menu.Open();

        Assert.True(menu.At(0).IsFocused);
        Assert.Equal(["focused 0", "changed 0"], menu.Log);
    }

    // The contract a game writes its handlers against: the item losing the focus hears first, the item
    // taking it hears next with the navigator already naming it, and the navigator announces last.
    [Fact]
    public void AMove_UnfocusesTheOldItemThenFocusesTheNewOneThenAnnouncesIt()
    {
        using Menu menu = Column().Open();

        menu.Tap(Key.Down);

        Assert.Equal(["unfocused 0", "focused 1", "changed 1"], menu.Log);
        Assert.False(menu.At(0).IsFocused);
        Assert.True(menu.At(1).IsFocused);
        Assert.Equal(1, menu.FocusedIndex);
    }

    [Fact]
    public void AStepThatMovesAndPresses_PressesTheItemItLandedOnAfterTheMove()
    {
        using Menu menu = Column().Open();

        menu.Pointer(InSecond).Tap(MouseButton.Left);

        Assert.Equal(["unfocused 0", "focused 1", "changed 1", "pressed 1"], menu.Log);
    }

    // Up from the top edge has no item above it, so it wraps to the item farthest below: the one in the
    // same column, because a tie on distance along the direction is broken by list order.
    [Fact]
    public void AGrid_MovesToTheItemEachDirectionReachesAndWrapsPastAnEdge()
    {
        using Menu menu = Grid().Open();

        Assert.Equal(1, menu.Tap(Key.Right).FocusedIndex);
        Assert.Equal(3, menu.Tap(Key.Down).FocusedIndex);
        Assert.Equal(2, menu.Tap(Key.Left).FocusedIndex);
        Assert.Equal(0, menu.Tap(Key.Up).FocusedIndex);

        // Released, then pressed again: an edge needs a step without it.
        Assert.Equal(2, menu.Rest().Tap(Key.Up).FocusedIndex);
    }

    // Alignment and closeness trade against each other: dot(direction, delta) / |delta|² ranks a
    // squarely placed item over a diagonal that is barely nearer, and a close diagonal over an aligned
    // item far away.
    [Fact]
    public void ADirection_RanksByAlignmentTradedAgainstCloseness()
    {
        using Menu aligned = new(Item(Vector2.Zero), Item(new Vector2(40f, 0f)), Item(new Vector2(25f, 25f)));

        Assert.Equal(1, aligned.Open().Tap(Key.Right).FocusedIndex);

        using Menu diagonal = new(Item(Vector2.Zero), Item(new Vector2(40f, 0f)), Item(new Vector2(10f, 10f)));

        Assert.Equal(2, diagonal.Open().Tap(Key.Right).FocusedIndex);
    }

    [Fact]
    public void AStepHoldingTwoDirections_ReadsTheFirstOfUpDownLeftRight()
    {
        using Menu menu = Grid().Open();

        // Down and right are both reachable from the top-left item; down is read.
        Assert.Equal(2, menu.Tap(Key.Down, Key.Right).FocusedIndex);
    }

    [Fact]
    public void ADirectionReachingNothing_LeavesTheFocusWhereItIs()
    {
        using Menu menu = new(Item(Vector2.Zero));

        menu.Open().Tap(Key.Down);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);
    }

    [Fact]
    public void AConfirmArrivingWithANamedMove_PressesTheItemThatMoveLandedOn()
    {
        using Menu menu = Grid();

        menu.At(0).Right = menu.At(3);

        menu.Open().Tap(Key.Right, Key.Enter);

        Assert.Equal(["unfocused 0", "focused 3", "changed 3", "pressed 3"], menu.Log);
    }

    [Fact]
    public void AConfirmPress_PressesTheFocusedItemWhereverThePointerIs()
    {
        using Menu menu = Column().Open();

        menu.Tap(Key.Down).Pointer(InNeither).Tap(Key.Enter);

        Assert.Equal(["pressed 1"], menu.Log);
    }

    [Fact]
    public void Focus_NamesTheStartingItemBeforeTheStartAndMovesTheFocusAfterIt()
    {
        using Menu menu = Column();

        menu.FocusOn(1);

        Assert.Equal(1, menu.FocusedIndex);
        Assert.Empty(menu.Log);

        menu.Open();

        Assert.Equal(["focused 1", "changed 1"], menu.Log);

        menu.FocusOn(0);

        Assert.Equal(["unfocused 1", "focused 0", "changed 0"], menu.Log);

        // Already focused: nothing moved, so nothing is raised.
        menu.FocusOn(0);

        Assert.Empty(menu.Log);
    }

    // A menu is commonly built and pointed at its opening item before any of it is in a scene, so
    // before the start there is nothing for liveness to mean: the request is kept as asked for.
    [Fact]
    public void FocusBeforeAnyItemIsInAScene_StillOpensOnTheItemItNamed()
    {
        using Menu menu = Column().Unseated();

        menu.FocusOn(1).Seat().Open();

        Assert.Equal(["focused 1", "changed 1"], menu.Log);
        Assert.Equal(1, menu.FocusedIndex);
        Assert.Equal(1, menu.OnlyFocusedIndex);
    }

    [Fact]
    public void FocusOnAnItemTheNavigatorDoesNotHold_AndAddingOneItHolds_AreRefused()
    {
        using Menu menu = Column().Open();

        Assert.Throws<ArgumentException>(() => menu.Navigator.Focus(Item(Vector2.Zero)));
        Assert.Throws<ArgumentNullException>(() => menu.Navigator.Focus(null!));
        Assert.Throws<ArgumentException>(() => menu.Navigator.Add(menu.Navigator.Items[0]));
        Assert.Equal(0, menu.FocusedIndex);
        Assert.Equal(2, menu.Navigator.Items.Length);
    }

    [Fact]
    public void AnEmptyNavigator_FocusesNothingAndRaisesNothing()
    {
        using Menu menu = new();

        menu.Open().Pointer(InFirst).Tap(MouseButton.Left, Key.Enter);

        Assert.Null(menu.Navigator.Focused);
        Assert.Equal(0, menu.Navigator.Items.Length);
        Assert.Empty(menu.Log);
    }

    // A handler cannot move the focus on: the focus it was told about is already the navigator's, so
    // a second move from inside it would leave two items claiming the focus.
    [Fact]
    public void AMoveAskedForFromInsideAFocusHandler_IsRefused()
    {
        using Menu unfocusing = Triple().RedirectOnUnfocused(0, 2).Open();

        Assert.Throws<InvalidOperationException>(() => unfocusing.Tap(Key.Down));

        using Menu focusing = Triple().RedirectOnFocused(1, 2).Open();

        Assert.Throws<InvalidOperationException>(() => focusing.Tap(Key.Down));
    }

    // The same rule reaches the start, which is the earliest a handler can run.
    [Fact]
    public void AnAddAskedForFromTheStartingItemsHandler_IsRefused()
    {
        using Menu menu = Column();
        Focusable latecomer = menu.Latecomer(new Vector2(0f, 80f));

        menu.At(0).Focused += () => menu.Navigator.Add(latecomer);

        Assert.Throws<InvalidOperationException>(menu.Open);
    }
}
