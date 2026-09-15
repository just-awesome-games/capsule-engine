using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.DevTools;
using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

public sealed class OverlaySceneTests
{
    private static readonly int LineHeight = BitmapFont.Default.LineHeight;

    // The last row a menu of Items(50) shows before the window moves, and the window's last
    // position over it.
    private const int LastShown = OverlayScene.MaxRows - 1;
    private const int LastFirst = 50 - OverlayScene.MaxRows;

    [Fact]
    public void MenusNest_AndPoppingReturnsTheFocusToTheItemThatOpenedEachLevel()
    {
        OverlayScene scene = new("~");
        int activated = 0;
        Menu inner = new("Inner", [new MenuItem("Leaf", () => activated++)]);
        Menu outer = new("Outer", [new MenuItem("Noop", static () => { }), new MenuItem("Deeper", () => scene.Push(inner))]);
        scene.Push(new Menu(null, [new MenuItem("First", static () => { }), new MenuItem("Nested", () => scene.Push(outer))]));
        using SimulationHost host = CreateHost(scene);

        Press(host, Key.Down);
        Press(host, Key.Enter);

        Assert.Equal("Outer", scene.Title);
        Assert.Equal(2, scene.Depth);
        Assert.Equal(0, scene.FocusedIndex);

        Press(host, Key.Down);
        Press(host, Key.Enter);
        Press(host, Key.Enter);

        Assert.Equal("Inner", scene.Title);
        Assert.Equal(3, scene.Depth);
        Assert.Equal(1, activated);

        Press(host, Key.Backspace);

        Assert.Equal("Outer", scene.Title);
        Assert.Equal("Deeper", scene.Current.Items[scene.FocusedIndex].Label);

        Press(host, Key.Left);

        Assert.Equal(string.Empty, scene.Title);
        Assert.Equal("Nested", scene.Current.Items[scene.FocusedIndex].Label);

        Press(host, Key.Backspace);

        Assert.Equal(1, scene.Depth);
        Assert.Equal("Nested", scene.Current.Items[scene.FocusedIndex].Label);
    }

    [Fact]
    public void ARow_ShowsItsHotkeyInTheColumnPastTheWidestLabelOrTheBareLabel()
    {
        OverlayScene scene = new("~");
        scene.Push(new Menu(null, [new MenuItem("Go", static () => { }, OverlayActions.Restart), new MenuItem("Longer", static () => { })]));

        Assert.Equal("Go      R", scene.RowText(0));
        Assert.Equal("Longer", scene.RowText(1));
    }

    [Fact]
    public void ThePanel_DrawsTheBackdropFirstAndTheHighlightOnTheFocusedRowAlone()
    {
        OverlayScene scene = new("~");
        scene.Push(new Menu("Paused", [new MenuItem("Resume", static () => { }), new MenuItem("Step", static () => { })]));
        scene.SetReadout("Gameplay", 17);
        using SimulationHost host = CreateHost(scene);

        Press(host, Key.Down);

        SpriteIntent[] sprites = host.Simulation.View.ScreenSprites.ToArray();
        SpriteIntent backdrop = sprites[0];
        SpriteIntent highlight = Assert.Single(sprites, static sprite => sprite.Color == new ColorRgba(255, 255, 255, 64));
        float widest = BitmapFont.Default.Measure(OverlayScene.Legend("~")).X;

        Assert.Empty(host.Simulation.View.Sprites.ToArray());
        Assert.Equal(Sprite.White, backdrop.Sprite);
        Assert.Equal(new ColorRgba(0, 0, 0, 160), backdrop.Color);
        Assert.Equal(Vector2.Zero, backdrop.Position);
        Assert.Equal(
            new Vector2((int)widest + (2 * OverlayScene.Padding), (8 * LineHeight) + (2 * OverlayScene.Padding)),
            backdrop.Size);
        Assert.Equal(new Vector2(0f, OverlayScene.Padding + (4 * LineHeight)), highlight.Position);
        Assert.Equal(new Vector2(backdrop.Size.X, LineHeight), highlight.Size);
        Assert.Equal("Gameplay  tick 17", scene.Readout);
        Assert.Equal("Paused", scene.Title);
    }

    [Fact]
    public void AMenuLongerThanTheWindow_HidesTheRowsOutsideItAndTheWindowFollowsTheFocus()
    {
        OverlayScene scene = new("~");
        scene.Push(new Menu("Long", Items(50)));
        using SimulationHost host = CreateHost(scene);

        Assert.Equal(50, scene.RowCount);
        Assert.Equal(0, scene.First);
        Assert.True(scene.IsRowShown(LastShown));
        Assert.False(scene.IsRowShown(OverlayScene.MaxRows));
        Assert.True(scene.RowBounds(LastShown).Size.X > 0f);
        Assert.Equal(LineHeight, scene.RowBounds(LastShown).Size.Y);
        Assert.Equal(Vector2.Zero, scene.RowBounds(OverlayScene.MaxRows).Size);
        Assert.Equal($"Item {OverlayScene.MaxRows}", scene.RowText(OverlayScene.MaxRows));

        for (int press = 0; press < OverlayScene.MaxRows; press++)
        {
            Press(host, Key.Down);
        }

        Assert.Equal(OverlayScene.MaxRows, scene.FocusedIndex);
        Assert.Equal(1, scene.First);
        Assert.False(scene.IsRowShown(0));
        Assert.True(scene.IsRowShown(OverlayScene.MaxRows));

        for (int press = 0; press < OverlayScene.MaxRows; press++)
        {
            Press(host, Key.Up);
        }

        Assert.Equal(0, scene.FocusedIndex);
        Assert.Equal(0, scene.First);
        Assert.True(scene.IsRowShown(0));
    }

    // Down held through the delay and every interval runs the focus and the window to the last
    // item, and the wrap takes both back to the top.
    [Fact]
    public void AHeldDown_RepeatsToTheEndOfAWindowedMenuAndWrapsToTheTop()
    {
        OverlayScene scene = new("~");
        scene.Push(new Menu("Long", Items(50)));
        using SimulationHost host = CreateHost(scene);

        host.Step(DeviceSnapshot.Of(Key.Down));
        Assert.Equal(1, scene.FocusedIndex);

        // The delay, then one repeat every interval: 48 more moves reach item 49.
        host.Step(OverlayScene.RepeatDelayFrames + (OverlayScene.RepeatIntervalFrames * 47), DeviceSnapshot.Of(Key.Down));

        Assert.Equal(49, scene.FocusedIndex);
        Assert.Equal(LastFirst, scene.First);
        Assert.True(scene.IsRowShown(49));
        Assert.False(scene.IsRowShown(LastFirst - 1));

        host.Step(OverlayScene.RepeatIntervalFrames, DeviceSnapshot.Of(Key.Down));

        Assert.Equal(0, scene.FocusedIndex);
        Assert.Equal(0, scene.First);
    }

    [Fact]
    public void TheWheel_ScrollsTheWindowWithoutMovingTheFocusAndTheNextPressSnapsItBack()
    {
        OverlayScene scene = new("~");
        scene.Push(new Menu("Long", Items(50)));
        using SimulationHost host = CreateHost(scene);

        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -2f)));
        host.Step(DeviceSnapshot.Empty);

        Assert.Equal(6, scene.First);
        Assert.Equal(0, scene.FocusedIndex);
        Assert.False(scene.IsRowShown(0));

        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 10f)));
        Assert.Equal(0, scene.First);

        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -30f)));
        Assert.Equal(LastFirst, scene.First);
        Assert.Equal(0, scene.FocusedIndex);

        // A fine wheel: a tenth of a notch is RowsPerNotch tenths of a row, and the fourth step
        // makes one.
        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 0.1f)));
        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 0.1f)));
        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 0.1f)));
        Assert.Equal(LastFirst, scene.First);

        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 0.1f)));
        Assert.Equal(LastFirst - 1, scene.First);

        Press(host, Key.Down);

        Assert.Equal(1, scene.FocusedIndex);
        Assert.Equal(1, scene.First);
        Assert.True(scene.IsRowShown(1));
    }

    // A row with no action is drawn and skipped: Down from the row above lands past it, and a
    // replacement whose remembered index falls on one lands on the interactive row after it.
    [Fact]
    public void ANonInteractiveRow_IsSkippedByTheFocusAndByReplace()
    {
        OverlayScene scene = new("~");
        MenuItem[] items = [new("Head", static () => { }), new(string.Empty, null), new("Tail", static () => { })];
        scene.Push(new Menu("Sections", items));
        using SimulationHost host = CreateHost(scene);

        Press(host, Key.Down);

        Assert.Equal(2, scene.FocusedIndex);
        Assert.Equal(string.Empty, scene.RowText(1));

        Press(host, Key.Up);
        Assert.Equal(0, scene.FocusedIndex);

        scene.Replace(new Menu("Sections", [new(string.Empty, null), new("Only", static () => { })]));
        host.Step(DeviceSnapshot.Empty);

        Assert.Equal(1, scene.FocusedIndex);

        scene.Replace(new Menu("Sections", [new("First", static () => { }), new(string.Empty, null)]));
        host.Step(DeviceSnapshot.Empty);

        Assert.Equal(0, scene.FocusedIndex);
    }

    // The remembered row is a note under a heading; the nearest interactive row is the heading
    // one above, not the heading two below the blank.
    [Fact]
    public void AFocusRememberedOnANote_LandsOnTheNearestInteractiveRowPreferringTheOneAbove()
    {
        OverlayScene scene = new("~");
        MenuItem[] items =
        [
            new("[Heading]", static () => { }),
            new("Field", static () => { }),
            new(string.Empty, null),
            new("[Next]", static () => { }),
        ];
        scene.Push(new Menu("Panel", items));
        using SimulationHost host = CreateHost(scene);
        Press(host, Key.Down);
        Assert.Equal(1, scene.FocusedIndex);

        scene.Replace(new Menu("Panel", [new("[Heading]", static () => { }), new("<Nothing to show>", null), new(string.Empty, null), new("[Next]", static () => { })]));
        host.Step(DeviceSnapshot.Empty);

        Assert.Equal(0, scene.FocusedIndex);
    }

    // Readout, blank, items on the root; readout, title, blank, items on a titled menu.
    [Fact]
    public void ABlankRow_SeparatesTheHeaderFromTheItemsWithoutDoublingOnTheUntitledRoot()
    {
        OverlayScene scene = new("~");
        Menu titled = new("Sub", [new MenuItem("Leaf", static () => { })]);
        scene.Push(new Menu(null, [new MenuItem("Open", () => scene.Push(titled))]));
        using SimulationHost host = CreateHost(scene);

        Assert.Equal(OverlayScene.Padding + (2 * LineHeight), scene.RowBounds(0).Position.Y);

        Press(host, Key.Enter);

        Assert.Equal("Sub", scene.Title);
        Assert.Equal(OverlayScene.Padding + (3 * LineHeight), scene.RowBounds(0).Position.Y);
    }

    // The bar spans the row window at the panel's right edge; the thumb is the window's share of
    // the list and travels the rest of the track as First runs to the end. A menu that fits has
    // no bar at all.
    [Fact]
    public void AWindowedMenu_ShowsAScrollbarWhoseThumbFollowsFirstAndAFittingOneShowsNone()
    {
        OverlayScene scene = new("~");
        scene.Push(new Menu("Long", Items(50)));
        using SimulationHost host = CreateHost(scene);
        host.Step(DeviceSnapshot.Empty);

        float trackHeight = OverlayScene.MaxRows * LineHeight;
        float thumbHeight = trackHeight * OverlayScene.MaxRows / 50;
        Rect track = scene.ScrollTrack;
        Rect thumb = scene.ScrollThumb;
        Assert.Equal(scene.RowBounds(0).Position.Y, track.Position.Y);
        Assert.Equal(trackHeight, track.Size.Y);
        Assert.Equal(thumbHeight, thumb.Size.Y);
        Assert.Equal(track.Position.Y, thumb.Position.Y);
        Assert.Equal(track.Position.X, thumb.Position.X);
        Assert.True(track.Position.X > scene.RowBounds(0).Position.X);
        Assert.True(track.Position.X + track.Size.X <= scene.RowBounds(0).Position.X + scene.RowBounds(0).Size.X);

        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -2f)));
        host.Step(DeviceSnapshot.Empty);

        Assert.Equal(6, scene.First);
        Assert.Equal(
            track.Position.Y + ((trackHeight - thumbHeight) * (2 * OverlayScene.RowsPerNotch) / LastFirst),
            scene.ScrollThumb.Position.Y,
            3);

        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -30f)));

        Assert.Equal(LastFirst, scene.First);
        Assert.Equal(track.Position.Y + trackHeight - thumbHeight, scene.ScrollThumb.Position.Y, 3);

        scene.Replace(new Menu("Short", Items(OverlayScene.MaxRows)));
        host.Step(DeviceSnapshot.Empty);

        Assert.Equal(Vector2.Zero, scene.ScrollTrack.Size);
        Assert.Equal(Vector2.Zero, scene.ScrollThumb.Size);
    }

    // With nothing to focus there is no row for the window to follow, so a rebuild keeps the
    // window where the wheel left it.
    [Fact]
    public void AMenuWithNothingToFocus_KeepsItsScrolledWindowAcrossAReplace()
    {
        OverlayScene scene = new("~");
        scene.Push(new Menu("Read", Notes(OverlayScene.MaxRows + 10)));
        using SimulationHost host = CreateHost(scene);

        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -2f)));
        Assert.Equal(6, scene.First);

        scene.Replace(new Menu("Read", Notes(OverlayScene.MaxRows + 10)));
        host.Step(DeviceSnapshot.Empty);

        Assert.Equal(6, scene.First);
        Assert.False(scene.IsRowShown(0));
    }

    private static MenuItem[] Notes(int count)
    {
        MenuItem[] items = new MenuItem[count];
        for (int index = 0; index < count; index++)
        {
            items[index] = new MenuItem($"Note {index}", null);
        }

        return items;
    }

    private static MenuItem[] Items(int count)
    {
        MenuItem[] items = new MenuItem[count];
        for (int index = 0; index < count; index++)
        {
            items[index] = new MenuItem($"Item {index}", static () => { });
        }

        return items;
    }

    // One press: the key's step and the release after it.
    private static void Press(SimulationHost host, Key key)
    {
        host.Step(DeviceSnapshot.Of(key));
        host.Step(DeviceSnapshot.Empty);
    }

    private static SimulationHost CreateHost(OverlayScene scene) =>
        new(
            scene,
            new InputState(OverlayActions.Bindings),
            run: new Run
            {
                Canvas = new Vector2(640f, 360f),
                Sampling = TextureSampling.Point,
            });
}
