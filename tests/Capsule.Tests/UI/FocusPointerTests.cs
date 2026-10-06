using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
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

    // A hit box is placed through every anchor and padding above it.
    [Fact]
    public void APointer_HitsAnItemNestedTwoLevelsDeep()
    {
        ScreenEntity panel = new(Anchor.BottomRight, new Vector2(-10f, -10f)) { Size = new Vector2(60f, 40f), Padding = new Insets(5f) };
        ScreenEntity row = new(Anchor.BottomWide, Vector2.Zero) { Parent = panel, Size = new Vector2(0f, 10f) };
        ScreenEntity cell = new(Anchor.Right, Vector2.Zero) { Parent = row, Size = new Vector2(20f, 10f) };
        Focusable nested = new();
        cell.Add(nested);

        using Menu menu = new(Item(Vector2.Zero), nested);

        menu.Open().Pointer(new Vector2(175f, 100f)).Rest();

        // The panel's inside is (135, 75) to (185, 105), the row its bottom ten pixels, and the cell the row's right end.
        Assert.Equal(new Rect(165f, 95f, 185f, 105f), nested.Bounds);
        Assert.Equal(1, menu.FocusedIndex);
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

    // A slider dragged past its end must keep the pointer, or the row below grabs the focus mid-drag.
    [Fact]
    public void AHeldClick_CapturesThePointerForTheItemItPressedUntilReleased()
    {
        using Menu menu = Column().Open();
        List<PointerDrag> drags = [];
        menu.At(0).Dragged += drags.Add;

        menu.Pointer(InFirst).Hold(MouseButton.Left);

        Assert.Equal(["pressed 0"], menu.Log);
        Assert.Equal([new PointerDrag(InFirst, InFirst)], drags);

        menu.Pointer(InSecond).Rest();

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);
        Assert.Equal(new PointerDrag(InFirst, InSecond), drags[^1]);

        menu.Release(MouseButton.Left);

        Assert.Equal(2, drags.Count);
        Assert.Equal(0, menu.FocusedIndex);

        menu.Pointer(InSecond + Vector2.UnitX).Rest();

        Assert.Equal(1, menu.FocusedIndex);
        Assert.Equal(2, drags.Count);
    }

    // A direction that moves the focus mid-drag must take the drag with it, or the item left behind keeps sliding.
    [Fact]
    public void ADirectionThatMovesTheFocusMidDrag_EndsTheCapture()
    {
        using Menu menu = Column().Open();
        int drags = 0;
        menu.At(0).Dragged += _ => drags++;

        menu.Pointer(InFirst).Hold(MouseButton.Left).Tap(Key.Down);

        Assert.Equal(1, menu.FocusedIndex);

        menu.Pointer(InSecond).Rest();

        Assert.Equal(1, drags);
    }

    // A wheel that reports finer than a notch must still step a slider once per notch, not once per report.
    [Fact]
    public void TheWheelOverTheFocusedAdjustingItem_AdjustsItOncePerWholeNotch()
    {
        using Menu menu = Column();
        menu.At(0).Adjusts = Axis.Horizontal;

        menu.Open().Pointer(InFirst).Wheel(0.5f);

        Assert.Empty(menu.Log);

        menu.Wheel(0.5f);

        Assert.Equal(["adjusted 0 1"], menu.Log);

        menu.Wheel(-1f);

        Assert.Equal(["adjusted 0 -1"], menu.Log);
    }

    // A slider whose step opens a prompt must not keep stepping behind it for the rest of a fast turn.
    [Fact]
    public void AnAdjustThatTurnsTheNavigatorOff_EndsTheTurnsRemainingNotches()
    {
        using Menu menu = Column();
        menu.At(0).Adjusts = Axis.Horizontal;
        menu.At(0).Adjusted += _ => menu.Navigator.Interactable = false;

        menu.Open().Pointer(InFirst).Wheel(2f);

        Assert.Equal(["adjusted 0 1"], menu.Log);
    }

    // A list scrolled under a resting pointer must not have a slider it passes grab the wheel.
    [Fact]
    public void TheWheelOverAnAdjustingItemWithoutTheFocus_DoesNothing()
    {
        using Menu menu = Column();
        menu.At(1).Adjusts = Axis.Horizontal;

        menu.Open().Pointer(InSecond).Rest().Tap(Key.Up).Wheel(1f);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);
    }
}
