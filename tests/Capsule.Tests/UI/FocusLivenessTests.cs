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

    // Hiding counts as leaving for the focus. The step that notices moves the focus off, and the pointer
    // over the hidden box lands nothing.
    [Fact]
    public void AHiddenItem_LosesTheFocusAndIsNotReachedByThePointer()
    {
        using Menu menu = Column().Open();

        menu.At(0).Entity!.Visible = false;
        menu.Pointer(InFirst).Rest();

        Assert.Equal(["unfocused 0", "focused 1", "changed 1"], menu.Log);

        menu.Tap(MouseButton.Left);

        Assert.Empty(menu.Log);
        Assert.Equal(1, menu.FocusedIndex);
    }

    // With nothing live to land on, the navigator holds no focus at all. It takes one again by itself
    // once an item is live, and an item re-entering the scene is simply usable again.
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

    // An item beneath an entity removed this step is no longer live, though it stays in the scene until
    // the step ends. A request for it lands the first live item instead.
    [Fact]
    public void AnItemBeneathAnEntityRemovedThisStep_CannotTakeTheFocus()
    {
        using Menu menu = Column().Open();
        Focusable nested = new(Box);
        _ = new WorldHolder(Vector2.Zero, nested) { Parent = menu.At(1).Entity };
        menu.Navigator.Add(nested);
        menu.Rest();

        menu.At(0).Pressed += () =>
        {
            menu.Remove(1);
            menu.Navigator.Focus(nested);
        };
        menu.Tap(Key.Enter);

        Assert.Same(menu.At(0), menu.Navigator.Focused);
        Assert.False(nested.IsFocused);
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

    // A navigator given no items gathers its subtree in tree order, leaves a nested gathering navigator
    // its own subtree, and follows items joining and leaving under it after it started. A focused item
    // that leaves drops out at once, and its focus moves on at the next step.
    [Fact]
    public void AGatheringNavigator_HoldsItsSubtree_AndFollowsItemsJoiningAndLeaving()
    {
        FocusNavigator navigator = new(Actions);
        FocusNavigator nearer = new(Actions);
        ScreenHolder root = new(Vector2.Zero, navigator);
        Focusable first = Under(root, Vector2.Zero);
        Focusable owned = Under(new ScreenHolder(new Vector2(40f, 0f), nearer) { Parent = root }, Vector2.Zero);
        Focusable second = Under(root, new Vector2(0f, 40f));

        Scene scene = new();
        scene.Add(root);
        using SimulationHost run = new(scene, run: new Run { Canvas = Canvas });

        Assert.Equal([first, second], navigator.Items.ToArray());
        Assert.Equal([owned], nearer.Items.ToArray());
        Assert.Same(first, navigator.Focused);

        Focusable late = Under(root, new Vector2(0f, 80f));
        run.Step();
        Assert.Equal([first, second, late], navigator.Items.ToArray());

        scene.Remove(first.Entity!);
        Assert.Equal([second, late], navigator.Items.ToArray());
        Assert.Same(first, navigator.Focused);

        run.Step();
        Assert.Same(second, navigator.Focused);
    }

    // Closing a menu or stopping its scene tears down its items with it. Nothing flickers through them on
    // the way out, and the same menu added back holds each item once with the focus where it was.
    [Fact]
    public void TearingDownAGatheredMenu_RaisesNoFocusEvents_AndReaddingItHoldsEachItemOnce()
    {
        FocusNavigator navigator = new(Actions);
        ScreenHolder root = new(Vector2.Zero, navigator);
        Focusable first = Under(root, Vector2.Zero);
        Focusable second = Under(root, new Vector2(0f, 40f));
        Closer closer = new();

        List<string> log = [];
        first.Focused += () => log.Add("focused first");
        second.Focused += () => log.Add("focused second");
        first.Unfocused += () => log.Add("unfocused first");
        navigator.FocusChanged += _ => log.Add("changed");

        Scene scene = new();
        scene.Add(root);
        scene.Add(closer);
        using SimulationHost run = new(scene, run: new Run { Canvas = Canvas });
        log.Clear();

        closer.Closing = root;
        run.Step();

        Assert.Empty(log);
        Assert.Equal(0, navigator.Items.Length);

        scene.Add(root);
        run.Step();

        Assert.Empty(log);
        Assert.Equal([first, second], navigator.Items.ToArray());
        Assert.Same(first, navigator.Focused);

        run.Dispose();
        Assert.Empty(log);
    }

    // The focused item leaves and comes back as a root of its own before the navigator steps. It is live
    // again but no longer held, so the next step repairs the focus and the confirm never reaches it.
    [Fact]
    public void AFocusedGatheredItemThatLeavesAndIsLiveAgainElsewhere_IsRepairedAwayFromAndNeverPressed()
    {
        FocusNavigator navigator = new(Actions);
        ScreenHolder root = new(Vector2.Zero, navigator);
        Focusable first = Under(root, Vector2.Zero);
        Focusable second = Under(root, new Vector2(0f, 40f));
        bool firstPressed = false;
        first.Pressed += () => firstPressed = true;

        Scene scene = new();
        scene.Add(root);
        Run game = new() { Canvas = Canvas };
        game.Input.Bindings.Bind(Confirm, Key.Enter);
        using SimulationHost run = new(scene, run: game);

        scene.Remove(first.Entity!);
        scene.Add(first.Entity!);
        run.Step(default(DeviceSnapshot).With(Key.Enter));

        Assert.Same(second, navigator.Focused);
        Assert.False(first.IsFocused);

        run.Step();
        run.Step(default(DeviceSnapshot).With(Key.Enter));

        Assert.False(firstPressed);
        Assert.Equal([second], navigator.Items.ToArray());
    }

    // A focused item moves to another menu's subtree before its old navigator steps. The new navigator
    // holds no focus and takes it, and the old one's repair leaves an item it no longer holds alone.
    [Fact]
    public void AFocusedItemMovingToAnotherGatheringNavigator_KeepsTheFocusItWasGivenThere()
    {
        FocusNavigator departed = new(Actions);
        FocusNavigator joined = new(Actions);
        ScreenHolder from = new(Vector2.Zero, departed);
        ScreenHolder to = new(new Vector2(100f, 0f), joined);
        Focusable moving = Under(from, Vector2.Zero);
        Focusable staying = Under(from, new Vector2(0f, 40f));
        int unfocused = 0;
        moving.Unfocused += () => unfocused++;

        Scene scene = new();
        scene.Add(from);
        scene.Add(to);
        using SimulationHost run = new(scene, run: new Run { Canvas = Canvas });

        scene.Remove(moving.Entity!);
        moving.Entity!.Parent = to;
        run.Step();

        Assert.Same(moving, joined.Focused);
        Assert.True(moving.IsFocused);
        Assert.Equal(0, unfocused);
        Assert.Same(staying, departed.Focused);
        Assert.True(staying.IsFocused);
    }

    private static Focusable Under(Entity parent, Vector2 position)
    {
        Focusable item = new(Box);
        _ = new ScreenHolder(position, item) { Parent = parent };

        return item;
    }

    // Removes the entity it is handed from inside its own step, as a menu's close does.
    private sealed class Closer() : ScreenEntity(Anchor.TopLeft, Vector2.Zero)
    {
        internal Entity? Closing { get; set; }

        protected internal override void OnStep(in StepContext context)
        {
            if (Closing is { } closing)
            {
                Closing = null;
                Scene.Remove(closing);
            }
        }
    }
}
