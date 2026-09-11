using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.Scenes.Input;
using Capsule.Scenes.Rendering;

namespace Capsule.Tests.Input;

// The focus machine: one dimension, edges only, and a pointer that picks an item by the rect its
// renderer reports. The items here are flat rects, so what the pointer is inside is arithmetic.
public sealed class FocusNavigatorTests
{
    private static readonly InputAction Backward = new("Backward");
    private static readonly InputAction Forward = new("Forward");
    private static readonly InputAction Confirm = new("Confirm");
    private static readonly InputAction Click = new("Click");

    // Two 20x10 items: the first spans (0, 0) to (20, 10) and the second (0, 20) to (20, 30).
    private static readonly Vector2 InFirst = new(10f, 5f);
    private static readonly Vector2 InSecond = new(10f, 25f);
    private static readonly Vector2 InNeither = new(10f, 15f);

    [Fact]
    public void AForwardPress_MovesOneItemAndWrapsPastTheEnd()
    {
        Menu menu = Two().Tap(Key.Down);

        Assert.Equal(1, menu.FocusedIndex);
        Assert.True(menu.FocusChanged);
        Assert.False(menu.Activated);

        // Released, then pressed again: an edge needs a step without it.
        menu.Rest().Tap(Key.Down);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.True(menu.FocusChanged);
    }

    [Fact]
    public void ABackwardPress_MovesOneItemAndWrapsPastTheStart()
    {
        Menu menu = Two().Tap(Key.Up);

        Assert.Equal(1, menu.FocusedIndex);

        menu.Rest().Tap(Key.Up);

        Assert.Equal(0, menu.FocusedIndex);
    }

    // Edge-triggered, which is what the second step of the same held key proves.
    [Fact]
    public void AHeldDirection_MovesTheFocusOnceAndNeverRepeats()
    {
        Menu menu = Two().Hold(Key.Down);

        Assert.Equal(1, menu.FocusedIndex);

        menu.Rest();

        Assert.Equal(1, menu.FocusedIndex);
        Assert.False(menu.FocusChanged);
    }

    [Fact]
    public void APointerThatMovedOntoAnItem_FocusesIt()
    {
        Menu menu = Two().Pointer(InSecond).Rest();

        Assert.Equal(1, menu.FocusedIndex);
        Assert.True(menu.FocusChanged);
        Assert.False(menu.Activated);
    }

    // The reason the move matters: a mouse left lying on an item must not fight a player driving the
    // menu from a gamepad.
    [Fact]
    public void APointerRestingOnAnItem_NeverStealsTheFocusBack()
    {
        Menu menu = Two().Pointer(InSecond).Rest().Tap(Key.Down);

        Assert.Equal(0, menu.FocusedIndex);

        menu.Rest();

        Assert.Equal(0, menu.FocusedIndex);
        Assert.False(menu.FocusChanged);
    }

    [Fact]
    public void APointerThatMovedOntoNoItem_LeavesTheFocusWhereItWas()
    {
        Menu menu = Two().Pointer(InNeither).Rest();

        Assert.Equal(0, menu.FocusedIndex);
        Assert.False(menu.FocusChanged);
    }

    [Fact]
    public void AClickOverAnItem_FocusesAndActivatesItInOneStep()
    {
        Menu menu = Two().Pointer(InSecond).Tap(MouseButton.Left);

        Assert.Equal(1, menu.FocusedIndex);
        Assert.True(menu.FocusChanged);
        Assert.True(menu.Activated);
    }

    [Fact]
    public void AClickOverNoItem_ActivatesNothing()
    {
        Menu menu = Two().Pointer(InNeither).Tap(MouseButton.Left);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.False(menu.Activated);
    }

    // A click reaches the item under the pointer and nothing else, so a navigator stepped without a
    // click action is deaf to the mouse button however the game bound it.
    [Fact]
    public void AClick_DoesNothingWhereTheStepNamesNoClickAction()
    {
        Menu menu = Two().WithoutAClickAction().Pointer(InFirst).Tap(MouseButton.Left);

        Assert.False(menu.Activated);
    }

    [Fact]
    public void AConfirmPress_ActivatesTheFocusedItemWhereverThePointerIs()
    {
        Menu menu = Two().Tap(Key.Down).Pointer(InNeither).Tap(Key.Enter);

        Assert.True(menu.Activated);
        Assert.Equal(1, menu.FocusedIndex);
    }

    [Fact]
    public void AStepWithNoPressAtAll_MovesNothingAndActivatesNothing()
    {
        Menu menu = Two().Rest();

        Assert.Equal(0, menu.FocusedIndex);
        Assert.False(menu.FocusChanged);
        Assert.False(menu.Activated);
    }

    [Fact]
    public void AnEmptyNavigator_FocusesNothingAndActivatesNothing()
    {
        Menu menu = new();

        menu.Pointer(InFirst).Tap(Key.Enter);

        Assert.Equal(0, menu.Count);
        Assert.Equal(-1, menu.FocusedIndex);
        Assert.Null(menu.Focused);
        Assert.False(menu.FocusChanged);
        Assert.False(menu.Activated);

        menu.Tap(MouseButton.Left);

        Assert.False(menu.Activated);
    }

    // Whether items overlap is the game's business; which one the pointer picks is not.
    [Fact]
    public void OverlappingItems_AreHitTestedInListOrderAndTheFirstHitWins()
    {
        ColorRect under = Item(Vector2.Zero);
        ColorRect over = Item(new Vector2(0f, 5f));
        Menu menu = new(under, over);

        // Inside both: the top half of `over` lies inside `under`.
        menu.Pointer(new Vector2(10f, 7f)).Rest();

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Same(under, menu.Focused);
    }

    // A renderer with no rect to report cannot be picked, which is what empty bounds mean.
    [Fact]
    public void AnItemReportingNoBounds_IsNeverUnderThePointer()
    {
        ColorRect detached = new(new Vector2(20f, 10f));
        Menu menu = new(detached, Item(Vector2.Zero));

        Assert.True(detached.Bounds.IsEmpty);

        menu.Pointer(InFirst).Rest();

        Assert.Equal(1, menu.FocusedIndex);
    }

    [Fact]
    public void AnItemAdded_TakesTheFocusOnlyWhereThereWasNone()
    {
        FocusNavigator focus = new();

        Assert.Equal(-1, focus.FocusedIndex);

        ColorRect first = Item(Vector2.Zero);
        focus.Add(first);

        Assert.Equal(0, focus.FocusedIndex);
        Assert.Same(first, focus.Focused);
        Assert.False(focus.FocusChanged);

        focus.Add(Item(new Vector2(0f, 20f)));

        Assert.Equal(0, focus.FocusedIndex);
        Assert.Equal(2, focus.Count);
    }

    [Fact]
    public void ANullItemOrAMissingInputState_IsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new FocusNavigator().Add(null!));
        Assert.Throws<ArgumentNullException>(() => new FocusNavigator((Renderer)null!));
        Assert.Throws<ArgumentNullException>(() => new FocusNavigator().Step(null!, Backward, Forward, Confirm));
    }

    private static Menu Two() => new(Item(Vector2.Zero), Item(new Vector2(0f, 20f)));

    // A rect on an entity of its own, which is all a renderer needs to report a rect: in world space it
    // reads no canvas, so no scene and no run are involved.
    private static ColorRect Item(Vector2 position)
    {
        ColorRect rect = new(new Vector2(20f, 10f));
        _ = new Holder(position, rect);

        return rect;
    }

    private sealed class Holder : Entity
    {
        internal Holder(Vector2 position, Component drawn)
            : base(position)
        {
            Add(drawn);
        }
    }

    // A navigator, the device state it is driven from and one step per call, so a spec reads as the
    // sequence of steps it means.
    private sealed class Menu
    {
        private readonly FocusNavigator _focus;

        private readonly InputState _input = new(new ActionBindings()
            .Bind(Backward, Key.Up)
            .Bind(Forward, Key.Down)
            .Bind(Confirm, Key.Enter)
            .Bind(Click, MouseButton.Left));

        private DeviceSnapshot _held;
        private bool _clicks = true;

        internal Menu(params ReadOnlySpan<Renderer> items) => _focus = new FocusNavigator(items);

        internal int Count => _focus.Count;

        internal int FocusedIndex => _focus.FocusedIndex;

        internal Renderer? Focused => _focus.Focused;

        internal bool FocusChanged => _focus.FocusChanged;

        internal bool Activated => _focus.Activated;

        internal Menu WithoutAClickAction()
        {
            _clicks = false;

            return this;
        }

        /// <summary>Puts the pointer here from the next step on; emits no step of its own.</summary>
        internal Menu Pointer(Vector2 position)
        {
            _held = _held.WithPointer(position);

            return this;
        }

        /// <summary>Steps with <paramref name="key"/> down on top of the held state, then releases it.</summary>
        internal Menu Tap(Key key) => Advance(_held.With(key));

        /// <summary>Steps with <paramref name="button"/> down on top of the held state, then releases it.</summary>
        internal Menu Tap(MouseButton button) => Advance(_held.With(button));

        /// <summary>Steps with <paramref name="key"/> down and leaves it held.</summary>
        internal Menu Hold(Key key)
        {
            _held = _held.With(key);

            return Advance(_held);
        }

        /// <summary>Steps with nothing new: the held state exactly as it stands.</summary>
        internal Menu Rest() => Advance(_held);

        private Menu Advance(in DeviceSnapshot snapshot)
        {
            _input.Advance(snapshot);

            if (_clicks)
            {
                _focus.Step(_input, Backward, Forward, Confirm, Click);
            }
            else
            {
                _focus.Step(_input, Backward, Forward, Confirm);
            }

            return this;
        }
    }
}
