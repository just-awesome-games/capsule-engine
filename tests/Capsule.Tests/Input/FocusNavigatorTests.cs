using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.Scenes.Input;
using Capsule.Scenes.Rendering;

namespace Capsule.Tests.Input;

// The focus machine: one dimension, edges only, a pointer that picks a screen item by the rect its
// renderer reports, and two events that hand the item over. The items here are flat rects on screen
// entities, so what the pointer is inside is arithmetic.
public sealed class FocusNavigatorTests
{
    private static readonly InputAction Backward = new("Backward");
    private static readonly InputAction Forward = new("Forward");
    private static readonly InputAction Confirm = new("Confirm");
    private static readonly InputAction Click = new("Click");

    private static readonly FocusActions Actions = new(Backward, Forward, Confirm, Click);

    // Two 20x10 items: the first spans (0, 0) to (20, 10) and the second (0, 20) to (20, 30).
    private static readonly Vector2 InFirst = new(10f, 5f);
    private static readonly Vector2 InSecond = new(10f, 25f);
    private static readonly Vector2 InNeither = new(10f, 15f);

    [Fact]
    public void AForwardPress_MovesOneItemAndWrapsPastTheEnd()
    {
        Menu menu = Two().Tap(Key.Down);

        Assert.Equal(1, menu.FocusedIndex);
        Assert.Same(menu.Focused, menu.MovedTo);
        Assert.Null(menu.ActivatedItem);

        // Released, then pressed again: an edge needs a step without it.
        menu.Rest().Tap(Key.Down);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Same(menu.Focused, menu.MovedTo);
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
        Assert.Null(menu.MovedTo);
    }

    [Fact]
    public void APointerThatMovedOntoAnItem_FocusesIt()
    {
        Menu menu = Two().Pointer(InSecond).Rest();

        Assert.Equal(1, menu.FocusedIndex);
        Assert.Same(menu.Focused, menu.MovedTo);
        Assert.Null(menu.ActivatedItem);
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
        Assert.Null(menu.MovedTo);
    }

    [Fact]
    public void APointerThatMovedOntoNoItem_LeavesTheFocusWhereItWas()
    {
        Menu menu = Two().Pointer(InNeither).Rest();

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Null(menu.MovedTo);
    }

    [Fact]
    public void AClickOverAnItem_FocusesAndActivatesItInOneStep()
    {
        Menu menu = Two().Pointer(InSecond).Tap(MouseButton.Left);

        Assert.Equal(1, menu.FocusedIndex);
        Assert.Same(menu.Focused, menu.MovedTo);
        Assert.Same(menu.Focused, menu.ActivatedItem);
    }

    [Fact]
    public void AClickOverNoItem_ActivatesNothing()
    {
        Menu menu = Two().Pointer(InNeither).Tap(MouseButton.Left);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Null(menu.ActivatedItem);
    }

    // A click reaches the item under the pointer and nothing else, so a navigator whose actions name no
    // click is deaf to the mouse button however the game bound it.
    [Fact]
    public void AClick_DoesNothingWhereTheActionsNameNoClick()
    {
        Menu menu = new(new FocusActions(Backward, Forward, Confirm), Items());

        menu.Pointer(InFirst).Tap(MouseButton.Left);

        Assert.Null(menu.ActivatedItem);
    }

    [Fact]
    public void AConfirmPress_ActivatesTheFocusedItemWhereverThePointerIs()
    {
        Menu menu = Two().Tap(Key.Down).Pointer(InNeither).Tap(Key.Enter);

        Assert.Same(menu.Focused, menu.ActivatedItem);
        Assert.Equal(1, menu.FocusedIndex);
    }

    // The contract the game writes its handlers against: the focus is settled before either event, the
    // move is announced first, and a step raises each at most once however many actions asked for it.
    [Fact]
    public void AStepThatMovesAndActivates_RaisesTheMoveFirstAndEachEventOnce()
    {
        Menu menu = Two().Pointer(InSecond).Tap(MouseButton.Left, Key.Enter);

        Assert.Equal(["focus 1", "activated 1"], menu.Raised);
    }

    [Fact]
    public void AStepWithNoPressAtAll_RaisesNothing()
    {
        Menu menu = Two().Rest();

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Raised);
    }

    [Fact]
    public void AnEmptyNavigator_FocusesNothingAndRaisesNothing()
    {
        Menu menu = new();

        menu.Pointer(InFirst).Tap(Key.Enter);

        Assert.Equal(0, menu.Count);
        Assert.Equal(-1, menu.FocusedIndex);
        Assert.Null(menu.Focused);
        Assert.Empty(menu.Raised);

        menu.Tap(MouseButton.Left);

        Assert.Empty(menu.Raised);
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

    // The pointer is a canvas position and a world item's bounds are world units under a camera that
    // moves, so the two are never compared: a world item is reached by the directions and confirm alone.
    [Fact]
    public void APointerOverAWorldItem_NeverPicksOrActivatesIt()
    {
        ColorRect screen = Item(new Vector2(0f, 20f));
        ColorRect world = WorldItem(Vector2.Zero);

        Scene scene = new();
        scene.Add(screen.Entity!);
        scene.Add(world.Entity!);

        using SceneRun run = new(scene, canvas: new Vector2(320f, 180f));

        // The camera looks 600 units away, so the canvas pixel the player points at names no part of the
        // world rect whose numbers happen to hold it.
        scene.Camera.Center = new Vector2(600f, 400f);
        scene.Camera.ViewportSize = new Vector2(320f, 180f);
        run.Step();

        Assert.True(world.Bounds.Contains(InFirst));

        Menu menu = new(screen, world);
        menu.Pointer(InFirst).Rest();

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Null(menu.MovedTo);

        menu.Tap(MouseButton.Left);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Null(menu.ActivatedItem);

        // Reachable all the same, by the actions that do not name a position.
        menu.Tap(Key.Down);

        Assert.Same(world, menu.Focused);
    }

    [Fact]
    public void Focus_MovesTheFocusAndAnnouncesItAsAnInputMoveDoes()
    {
        ColorRect[] items = Items();
        Menu menu = new(items);

        menu.FocusOn(items[1]);

        Assert.Equal(1, menu.FocusedIndex);
        Assert.Same(items[1], menu.MovedTo);
        Assert.Equal(["focus 1"], menu.Raised);

        // Already focused: nothing moved, so nothing is announced.
        menu.FocusOn(items[1]);

        Assert.Equal(1, menu.FocusedIndex);
        Assert.Empty(menu.Raised);
    }

    [Fact]
    public void FocusOnAnItemTheNavigatorDoesNotHold_IsRefused()
    {
        FocusNavigator<Renderer> focus = new(Actions, Items());

        Assert.Throws<ArgumentException>(() => focus.Focus(Item(Vector2.Zero)));
        Assert.Throws<ArgumentNullException>(() => focus.Focus(null!));
        Assert.Equal(0, focus.FocusedIndex);
    }

    [Fact]
    public void AnItemAdded_TakesTheFocusOnlyWhereThereWasNoneAndAnnouncesNoMove()
    {
        List<ColorRect> moves = [];
        FocusNavigator<ColorRect> focus = new(Actions);
        focus.FocusChanged += moves.Add;

        Assert.Equal(-1, focus.FocusedIndex);

        ColorRect first = Item(Vector2.Zero);
        focus.Add(first);

        Assert.Equal(0, focus.FocusedIndex);
        Assert.Same(first, focus.Focused);
        Assert.Empty(moves);

        focus.Add(Item(new Vector2(0f, 20f)));

        Assert.Equal(0, focus.FocusedIndex);
        Assert.Equal(2, focus.Count);
    }

    [Fact]
    public void Items_AreTheListInTheOrderFocusWalksThem()
    {
        ColorRect first = Item(Vector2.Zero);
        ColorRect second = Item(new Vector2(0f, 20f));
        FocusNavigator<ColorRect> focus = new(Actions, first, second);

        Assert.Equal([first, second], focus.Items.ToArray());
    }

    [Fact]
    public void ANullItemOrAMissingInputState_IsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new FocusNavigator<Renderer>(Actions).Add(null!));
        Assert.Throws<ArgumentNullException>(() => new FocusNavigator<Renderer>(Actions, (Renderer)null!));
        Assert.Throws<ArgumentNullException>(() => new FocusNavigator<Renderer>(Actions).Step(null!));
    }

    private static Menu Two() => new(Items());

    private static ColorRect[] Items() => [Item(Vector2.Zero), Item(new Vector2(0f, 20f))];

    // A rect on a screen entity of its own, which is what the pointer picks. A top-left anchor off any
    // scene resolves to no origin, so the rect's canvas pixels are the position it was given.
    private static ColorRect Item(Vector2 position)
    {
        ColorRect rect = new(new Vector2(20f, 10f));
        _ = new Holder(position, rect);

        return rect;
    }

    private static ColorRect WorldItem(Vector2 position)
    {
        ColorRect rect = new(new Vector2(20f, 10f));
        _ = new WorldHolder(position, rect);

        return rect;
    }

    private sealed class Holder : ScreenEntity
    {
        internal Holder(Vector2 position, Component drawn)
            : base(Anchor.TopLeft, position)
        {
            Add(drawn);
        }
    }

    private sealed class WorldHolder : Entity
    {
        internal WorldHolder(Vector2 position, Component drawn)
            : base(position)
        {
            Add(drawn);
        }
    }

    // A navigator, the device state it is driven from and one step per call, so a spec reads as the
    // sequence of steps it means. What the events delivered is kept for the step just taken alone.
    private sealed class Menu
    {
        private readonly FocusNavigator<Renderer> _focus;

        private readonly InputState _input = new(new ActionBindings()
            .Bind(Backward, Key.Up)
            .Bind(Forward, Key.Down)
            .Bind(Confirm, Key.Enter)
            .Bind(Click, MouseButton.Left));

        private readonly List<string> _raised = [];

        private DeviceSnapshot _held;

        internal Menu(params ReadOnlySpan<Renderer> items)
            : this(Actions, items)
        {
        }

        internal Menu(FocusActions actions, params ReadOnlySpan<Renderer> items)
        {
            _focus = new FocusNavigator<Renderer>(actions, items);

            // Logged with the navigator's own state as each handler saw it, which is what proves the
            // focus is settled before either event runs.
            _focus.FocusChanged += item =>
            {
                MovedTo = item;
                _raised.Add($"focus {_focus.FocusedIndex}");
            };

            _focus.Activated += item =>
            {
                ActivatedItem = item;
                _raised.Add($"activated {_focus.FocusedIndex}");
            };
        }

        internal int Count => _focus.Count;

        internal int FocusedIndex => _focus.FocusedIndex;

        internal Renderer? Focused => _focus.Focused;

        /// <summary>What the step just taken announced, or null where it announced no move.</summary>
        internal Renderer? MovedTo { get; private set; }

        /// <summary>What the step just taken activated, or null where it activated nothing.</summary>
        internal Renderer? ActivatedItem { get; private set; }

        /// <summary>The events the step just taken raised, in the order they were raised.</summary>
        internal IReadOnlyList<string> Raised => _raised;

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

        /// <summary>Steps with both down at once, which is two actions asking for one activation.</summary>
        internal Menu Tap(MouseButton button, Key key) => Advance(_held.With(button).With(key));

        /// <summary>Steps with <paramref name="key"/> down and leaves it held.</summary>
        internal Menu Hold(Key key)
        {
            _held = _held.With(key);

            return Advance(_held);
        }

        /// <summary>Steps with nothing new: the held state exactly as it stands.</summary>
        internal Menu Rest() => Advance(_held);

        /// <summary>Focuses an item outright, as a game opening a menu on one does; steps nothing.</summary>
        internal Menu FocusOn(Renderer item)
        {
            MovedTo = null;
            ActivatedItem = null;
            _raised.Clear();

            _focus.Focus(item);

            return this;
        }

        private Menu Advance(in DeviceSnapshot snapshot)
        {
            MovedTo = null;
            ActivatedItem = null;
            _raised.Clear();

            _input.Advance(snapshot);
            _focus.Step(_input);

            return this;
        }
    }
}
