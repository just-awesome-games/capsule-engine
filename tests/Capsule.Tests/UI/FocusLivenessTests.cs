using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.UI;

using static Capsule.Tests.UI.FocusFixtures;

using Menu = Capsule.Tests.UI.FocusFixtures.Menu;

namespace Capsule.Tests.UI;

public sealed class FocusLivenessTests
{
    [Fact]
    public void AStartingItemThatIsNotLiveAtTheStart_LandsTheFirstLiveItemOrNoneAtAll()
    {
        using Menu column = Column();

        column.Remove(0).Open();

        Assert.Equal(["focused 1", "changed 1"], column.Log);
        Assert.Equal(1, column.FocusedIndex);

        using Menu empty = Column().Unseated();

        empty.Open();

        Assert.Null(empty.Navigator.Focused);
        Assert.Empty(empty.Log);
    }

    // The press is aimed at one item: a handler that closes the item the focus just landed on drops it
    // rather than handing it to whatever the focus falls back to, by either path that presses.
    [Fact]
    public void AnItemRemovedByItsOwnFocusedHandler_IsNotPressedByTheStepThatMovedOntoIt()
    {
        using Menu clicked = Column().RemoveOnFocused(1).Open();

        clicked.Pointer(InSecond).Tap(MouseButton.Left);

        Assert.Equal(["unfocused 0", "focused 1", "changed 1"], clicked.Log);

        // The repair is the next step's, and it presses nothing on the way.
        clicked.Rest();

        Assert.Equal(["unfocused 1", "focused 0", "changed 0"], clicked.Log);

        using Menu confirmed = Column().RemoveOnFocused(1).Open();

        confirmed.Tap(Key.Down, Key.Enter);

        Assert.Equal(["unfocused 0", "focused 1", "changed 1"], confirmed.Log);
    }

    // A removed entity takes its item out of the focus entirely: the step that notices spends itself
    // landing the first live item, so the confirm that arrived with it presses nothing.
    [Fact]
    public void TheFocusedItemsEntityLeavingTheScene_ReleasesItAndLandsTheFirstLiveItemWithoutPressing()
    {
        using Menu menu = Column().Open();

        menu.Tap(Key.Down).Remove(1).Tap(Key.Enter);

        Assert.Equal(["unfocused 1", "focused 0", "changed 0"], menu.Log);
        Assert.Equal(0, menu.OnlyFocusedIndex);

        // Released, then pressed again: an edge needs a step without it. The confirm now reaches the
        // item that was landed on, and a click over the box the removed one used to occupy reaches
        // nothing at all.
        menu.Rest().Tap(Key.Enter);

        Assert.Equal(["pressed 0"], menu.Log);

        menu.Pointer(InSecond).Tap(MouseButton.Left);

        Assert.Empty(menu.Log);
        Assert.Equal(0, menu.FocusedIndex);
    }

    [Fact]
    public void AnItemWhoseEntityLeftTheScene_IsReachedByNeitherADirectionNorThePointer()
    {
        using Menu menu = Triple().Open();

        menu.Remove(1).Pointer(InSecond).Rest();

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);

        // Past the hole in the column rather than into it.
        Assert.Equal(2, menu.Tap(Key.Down).FocusedIndex);
    }

    // Nothing live to land on, so the navigator holds no focus at all — and takes one again by itself
    // once an item is live, which is what makes an item re-entering the scene simply usable again.
    [Fact]
    public void TheLastLiveItemLeavingTheScene_LeavesNoFocusUntilOneIsLiveAgain()
    {
        using Menu menu = new(Item(Vector2.Zero));

        menu.Open().Remove(0).Rest();

        Assert.Equal(["unfocused 0"], menu.Log);
        Assert.Null(menu.Navigator.Focused);

        menu.Restore(0).Rest();

        Assert.Equal(["focused 0", "changed 0"], menu.Log);
        Assert.Equal(0, menu.FocusedIndex);
    }

    // Taking the focused item out of the navigator is the release and the repair in one call, not a
    // step later: the item hears it lost the focus before the game lets go of it.
    [Fact]
    public void RemovingTheFocusedItem_ReleasesItAndLandsTheNextLiveItemAtOnce()
    {
        using Menu menu = Column().Open();

        Assert.True(menu.Drop(0));

        Assert.Equal(["unfocused 0", "focused 1", "changed 1"], menu.Log);
        Assert.Equal(1, menu.FocusedIndex);
        Assert.Equal(1, menu.OnlyFocusedIndex);
        Assert.Equal(1, menu.Navigator.Items.Length);
    }

    [Fact]
    public void RemovingAnItemTheNavigatorDoesNotHold_ReturnsFalseAndRaisesNothing()
    {
        using Menu menu = Column().Open().Rest();

        Assert.False(menu.Navigator.Remove(Item(Vector2.Zero)));
        Assert.Throws<ArgumentNullException>(() => menu.Navigator.Remove(null!));
        Assert.Empty(menu.Log);
        Assert.Equal(0, menu.FocusedIndex);
    }

    // A menu rebuilt inside a step: every row leaves, new rows arrive queued to join the scene, and
    // the one the menu remembers takes the focus at once. A row queued to join is live enough to hold
    // the focus, so the next step reads its input from there.
    [Fact]
    public void AMenuRebuiltInsideAStep_OpensOnTheItemItAskedForAtOnce()
    {
        using Menu menu = Column().Open();

        menu.At(0).Pressed += () =>
        {
            menu.Navigator.Remove(menu.At(0));
            menu.Navigator.Remove(menu.At(1));
            menu.Remove(0).Remove(1);

            menu.Navigator.Add(menu.Latecomer(Vector2.Zero));
            menu.Navigator.Add(menu.Latecomer(new Vector2(0f, 40f)));
            menu.Navigator.Add(menu.Latecomer(new Vector2(0f, 80f)));
            menu.Navigator.Focus(menu.At(3));
        };

        menu.Tap(Key.Enter);

        Assert.Equal(3, menu.FocusedIndex);
        Assert.Equal(3, menu.OnlyFocusedIndex);

        menu.Tap(Key.Down);

        Assert.Equal(4, menu.FocusedIndex);
        Assert.Equal(4, menu.OnlyFocusedIndex);
    }

    // Nothing waits for a later step: a request naming an item that is not live lands the first live
    // item there and then.
    [Fact]
    public void ARequestForAnItemThatIsNotLive_LandsTheFirstLiveItemAtOnce()
    {
        using Menu menu = Column().Open();
        Focusable absent = menu.Latecomer(new Vector2(0f, 80f));

        menu.Tap(Key.Down).Rest().Remove(2).Navigator.Add(absent);
        menu.FocusOn(2);

        Assert.Equal(["unfocused 1", "focused 0", "changed 0"], menu.Log);
        Assert.Equal(0, menu.FocusedIndex);
    }
}
