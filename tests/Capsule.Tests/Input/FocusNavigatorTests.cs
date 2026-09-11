using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.Scenes.Input;

namespace Capsule.Tests.Input;

// The focus machine as a component: items that own their own reaction to the focus, four directions
// resolved by geometry, a pointer that picks a screen item by its hit box, and a press that reaches the
// focused item alone. Every item here is a box on a screen entity anchored to the canvas's top-left
// corner, so what lies in a direction and what the pointer is inside are both arithmetic.
public sealed class FocusNavigatorTests
{
    private static readonly InputAction Up = new("Up");
    private static readonly InputAction Down = new("Down");
    private static readonly InputAction Left = new("Left");
    private static readonly InputAction Right = new("Right");
    private static readonly InputAction Confirm = new("Confirm");
    private static readonly InputAction Click = new("Click");

    private static readonly FocusActions Actions = new(Up, Down, Left, Right, Confirm, Click);

    private static readonly Vector2 Box = new(20f, 10f);
    private static readonly Vector2 Canvas = new(200f, 120f);

    // A column of two items spanning (0, 0) to (20, 10) and (0, 40) to (20, 50): inside the first,
    // inside the second, and in the gap between them.
    private static readonly Vector2 InFirst = new(10f, 5f);
    private static readonly Vector2 InSecond = new(10f, 45f);
    private static readonly Vector2 InNeither = new(10f, 25f);

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

    [Fact]
    public void AConfirmPress_PressesTheFocusedItemWhereverThePointerIs()
    {
        using Menu menu = Column().Open();

        menu.Tap(Key.Down).Pointer(InNeither).Tap(Key.Enter);

        Assert.Equal(["pressed 1"], menu.Log);
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

    [Fact]
    public void FocusOnAnItemTheNavigatorDoesNotHold_IsRefused()
    {
        using Menu menu = Column().Open();

        Assert.Throws<ArgumentException>(() => menu.Navigator.Focus(Item(Vector2.Zero)));
        Assert.Throws<ArgumentNullException>(() => menu.Navigator.Focus(null!));
        Assert.Equal(0, menu.FocusedIndex);
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

    // A handler that moves the focus on cannot be allowed to interleave with the sequence raising it:
    // the sequence finishes, then the redirect runs whole from the item that just landed.
    [Fact]
    public void AMoveAskedForFromAnUnfocusedHandler_RunsAsItsOwnSequenceAfterTheOneRaisingIt()
    {
        using Menu menu = Triple().RedirectOnUnfocused(0, 2).Open();

        menu.Tap(Key.Down);

        Assert.Equal(["unfocused 0", "focused 1", "changed 1", "unfocused 1", "focused 2", "changed 2"], menu.Log);
        Assert.Equal(2, menu.FocusedIndex);
        Assert.Equal(2, menu.OnlyFocusedIndex);
    }

    [Fact]
    public void AMoveAskedForFromAFocusedHandler_RunsAsItsOwnSequenceAfterTheOneRaisingIt()
    {
        using Menu menu = Triple().RedirectOnFocused(1, 2).Open();

        menu.Tap(Key.Down);

        Assert.Equal(["unfocused 0", "focused 1", "changed 1", "unfocused 1", "focused 2", "changed 2"], menu.Log);
        Assert.Equal(2, menu.FocusedIndex);
        Assert.Equal(2, menu.OnlyFocusedIndex);
    }

    // The starting item's own handler, which is the earliest a redirect can arrive, adding the item it
    // then asks for.
    [Fact]
    public void AnAddAndAMoveAskedForAtTheStart_RunAsTheirOwnSequenceAfterTheStartingItemLanded()
    {
        using Menu menu = Column();
        Focusable latecomer = menu.Latecomer(new Vector2(0f, 80f));

        menu.At(0).Focused += () =>
        {
            menu.Navigator.Add(latecomer);
            menu.Navigator.Focus(latecomer);
        };

        menu.Open();

        Assert.Equal(["focused 0", "changed 0", "unfocused 0", "focused 2", "changed 2"], menu.Log);
        Assert.Equal(2, menu.FocusedIndex);
        Assert.Equal(2, menu.OnlyFocusedIndex);
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

    private static Menu Column() => new(Item(Vector2.Zero), Item(new Vector2(0f, 40f)));

    // The column with a third box on the same 40-pixel centres, for the cases that need an item past
    // the one they remove or redirect away from.
    private static Menu Triple() => new(Item(Vector2.Zero), Item(new Vector2(0f, 40f)), Item(new Vector2(0f, 80f)));

    // A 2x2 grid of 20x10 boxes on 40-pixel centres, in reading order: top-left, top-right,
    // bottom-left, bottom-right.
    private static Menu Grid() =>
        new(
            Item(Vector2.Zero),
            Item(new Vector2(40f, 0f)),
            Item(new Vector2(0f, 40f)),
            Item(new Vector2(40f, 40f)));

    private static Focusable Item(Vector2 position, Vector2? size = null)
    {
        Focusable item = new(size ?? Box);
        _ = new ScreenHolder(position, item);

        return item;
    }

    private static Focusable WorldItem(Vector2 position)
    {
        Focusable item = new(Box);
        _ = new WorldHolder(position, item);

        return item;
    }

    private sealed class ScreenHolder : ScreenEntity
    {
        internal ScreenHolder(Vector2 position, Component item)
            : base(Anchor.TopLeft, position) =>
            Add(item);
    }

    private sealed class WorldHolder : Entity
    {
        internal WorldHolder(Vector2 position, Component item)
            : base(position) =>
            Add(item);
    }

    // A scene holding one item per entity and a navigator over all of them, stepped once per call, so a
    // spec reads as the sequence of steps it means. The log carries what the step just taken raised, in
    // order, and the items are named by their place in the navigator's list.
    private sealed class Menu : IDisposable
    {
        private readonly Scene _scene = new();
        private readonly List<string> _log = [];
        private readonly List<Focusable> _items = [];

        private SceneRun? _run;
        private DeviceSnapshot _held;

        internal Menu(params ReadOnlySpan<Focusable> items)
            : this(Actions, items)
        {
        }

        internal Menu(FocusActions actions, params ReadOnlySpan<Focusable> items)
        {
            Navigator = new FocusNavigator(actions);

            for (int i = 0; i < items.Length; i++)
            {
                Watch(items[i]);
                Navigator.Add(items[i]);
            }

            Navigator.FocusChanged += item => _log.Add($"changed {IndexOf(item)}");
            _scene.Add(new WorldHolder(Vector2.Zero, Navigator));
        }

        internal FocusNavigator Navigator { get; }

        /// <summary>What the step just taken raised, in the order it was raised.</summary>
        internal IReadOnlyList<string> Log => _log;

        /// <summary>Which item has the focus, as its place in the list, or -1 where none has it.</summary>
        internal int FocusedIndex => IndexOf(Navigator.Focused);

        /// <summary>
        /// The one item reporting <see cref="Focusable.IsFocused"/>, or -1 where none does; -2 where
        /// more than one does, which is the state a half-applied move would leave.
        /// </summary>
        internal int OnlyFocusedIndex
        {
            get
            {
                int only = -1;

                for (int i = 0; i < _items.Count; i++)
                {
                    if (!_items[i].IsFocused)
                    {
                        continue;
                    }

                    if (only >= 0)
                    {
                        return -2;
                    }

                    only = i;
                }

                return only;
            }
        }

        internal Focusable At(int index) => _items[index];

        /// <summary>
        /// An item in the scene and in the log from here on, but not yet one of the navigator's: what
        /// a handler calling <see cref="FocusNavigator.Add"/> mid-flight is handed.
        /// </summary>
        internal Focusable Latecomer(Vector2 position) => Watch(Item(position));

        /// <summary>Takes the entity holding item <paramref name="index"/> out of the scene.</summary>
        internal Menu Remove(int index)
        {
            _scene.Remove(_items[index].Entity!);

            return this;
        }

        /// <summary>Puts it back, which is all an item needs to be navigable again.</summary>
        internal Menu Restore(int index)
        {
            _scene.Add(_items[index].Entity!);

            return this;
        }

        /// <summary>
        /// Takes every item's entity out of the scene, for the cases that must reach the navigator
        /// while none of its items is live; <see cref="Seat"/> puts them all back.
        /// </summary>
        internal Menu Unseated()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                Remove(i);
            }

            return this;
        }

        internal Menu Seat()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                Restore(i);
            }

            return this;
        }

        /// <summary>
        /// Has item <paramref name="index"/>, the first time it loses the focus, ask from inside its own
        /// handler for the focus to go to <paramref name="target"/> instead.
        /// </summary>
        internal Menu RedirectOnUnfocused(int index, int target) => Redirect(index, target, onFocused: false);

        /// <summary>The same redirect, asked for as the item takes the focus.</summary>
        internal Menu RedirectOnFocused(int index, int target) => Redirect(index, target, onFocused: true);

        /// <summary>
        /// Has item <paramref name="index"/>, the first time it takes the focus, take its own entity
        /// out of the scene from inside that handler: the item a step is about to press leaving
        /// mid-move.
        /// </summary>
        internal Menu RemoveOnFocused(int index)
        {
            bool spent = false;

            _items[index].Focused += () =>
            {
                if (spent)
                {
                    return;
                }

                spent = true;
                Remove(index);
            };

            return this;
        }

        /// <summary>Starts the scene, which is where the navigator's starting item takes the focus.</summary>
        internal Menu Open()
        {
            _run = new SceneRun(_scene, new InputState(Bound()), canvas: Canvas);

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

        /// <summary>Steps with both keys down at once, which is two directions asking for one move.</summary>
        internal Menu Tap(Key first, Key second) => Advance(_held.With(first).With(second));

        /// <summary>Steps with <paramref name="button"/> down on top of the held state, then releases it.</summary>
        internal Menu Tap(MouseButton button) => Advance(_held.With(button));

        /// <summary>Steps with both down at once, which is two actions asking for one press.</summary>
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
        internal Menu FocusOn(int index)
        {
            _log.Clear();
            Navigator.Focus(_items[index]);

            return this;
        }

        public void Dispose() => _run?.Dispose();

        private static ActionBindings Bound() =>
            new ActionBindings()
                .Bind(Up, Key.Up)
                .Bind(Down, Key.Down)
                .Bind(Left, Key.Left)
                .Bind(Right, Key.Right)
                .Bind(Confirm, Key.Enter)
                .Bind(Click, MouseButton.Left);

        private Menu Advance(in DeviceSnapshot snapshot)
        {
            _log.Clear();
            _run!.Step(snapshot);

            return this;
        }

        // Into the log under its place in the list, and into the scene, which is what makes it live.
        private Focusable Watch(Focusable item)
        {
            int index = _items.Count;

            item.Focused += () => _log.Add($"focused {index}");
            item.Unfocused += () => _log.Add($"unfocused {index}");
            item.Pressed += () => _log.Add($"pressed {index}");

            _items.Add(item);
            _scene.Add(item.Entity!);

            return item;
        }

        private Menu Redirect(int index, int target, bool onFocused)
        {
            bool spent = false;

            void Ask()
            {
                if (spent)
                {
                    return;
                }

                spent = true;
                Navigator.Focus(_items[target]);
            }

            if (onFocused)
            {
                _items[index].Focused += Ask;
            }
            else
            {
                _items[index].Unfocused += Ask;
            }

            return this;
        }

        private int IndexOf(Focusable? item)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (ReferenceEquals(_items[i], item))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
