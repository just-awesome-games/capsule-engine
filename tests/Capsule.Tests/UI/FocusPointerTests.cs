using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.UI;

using static Capsule.Tests.UI.FocusFixtures;

using Menu = Capsule.Tests.UI.FocusFixtures.Menu;

namespace Capsule.Tests.UI;

public sealed class FocusPointerTests
{
    // The move is what the pointer focuses on: a mouse left lying on an item must not fight a player
    // driving the menu from a gamepad.
    [Fact]
    public void APointerThatMoved_FocusesTheItemUnderItAndARestingOneFocusesNothing()
    {
        using Menu menu = Column().Open();

        menu.Pointer(InSecond).Rest();

        Assert.Equal(1, menu.FocusedIndex);
        Assert.Equal(["unfocused 0", "focused 1", "changed 1"], menu.Log);

        menu.Tap(Key.Up);

        Assert.Equal(0, menu.FocusedIndex);

        // Still lying on the second item, and the focus stays where the direction put it.
        menu.Rest();

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);
    }

    [Fact]
    public void APointerThatMovedOntoNoItem_LeavesTheFocusWhereItWas()
    {
        using Menu menu = Column().Open();

        menu.Pointer(InNeither).Rest();

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);
    }

    [Fact]
    public void AClickOverNoItem_PressesNothing()
    {
        using Menu menu = Column().Open();

        menu.Pointer(InNeither).Tap(MouseButton.Left);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);
    }

    // A click reaches the item under the pointer and nothing else, so a navigator whose actions name no
    // click is deaf to the mouse button however the game bound it.
    [Fact]
    public void AClick_DoesNothingWhereTheActionsNameNoClick()
    {
        using Menu menu = new(new FocusActions(Up, Down, Left, Right, Confirm), Item(Vector2.Zero), Item(new Vector2(0f, 40f)));

        menu.Open().Pointer(InFirst).Tap(MouseButton.Left);

        Assert.Empty(menu.Log);
    }

    // Whether items overlap is the game's business; which one the pointer picks is not. The second item
    // here has no extent, so it is never under the pointer, and a direction still reaches it: its centre
    // is its position, which is the nearest one below the first item.
    [Fact]
    public void OverlappingItems_AreHitTestedInListOrderAndAnItemWithNoExtentIsNeverUnderThePointer()
    {
        using Menu menu = new(
            Item(Vector2.Zero),
            Item(new Vector2(10f, 8f), Vector2.Zero),
            Item(new Vector2(0f, 5f)));

        menu.Open().Pointer(new Vector2(10f, 7f)).Rest();

        Assert.Equal(0, menu.FocusedIndex);

        Assert.Equal(1, menu.Tap(Key.Down).FocusedIndex);
    }

    // The pointer is a canvas position and a world item's bounds are world units under a camera that
    // moves, so the two are never compared: a world item is reached by the directions and confirm alone.
    [Fact]
    public void APointerOverAWorldItem_NeverPicksOrPressesIt()
    {
        using Menu menu = new(Item(new Vector2(0f, 40f)), WorldItem(Vector2.Zero));

        menu.Open().Pointer(InFirst).Tap(MouseButton.Left);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);

        menu.Tap(Key.Up);

        Assert.Equal(1, menu.FocusedIndex);
    }
}
