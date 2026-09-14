using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.DevTools;
using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

public sealed class DebugSceneTests
{
    private static readonly int LineHeight = BitmapFont.Default.LineHeight;

    [Fact]
    public void MenusNest_AndPoppingReturnsTheFocusToTheItemThatOpenedEachLevel()
    {
        DebugScene scene = new("~");
        int activated = 0;
        DebugMenu inner = new("Inner", [new DebugMenuItem("Leaf", () => activated++)]);
        DebugMenu outer = new("Outer", [new DebugMenuItem("Noop", static () => { }), new DebugMenuItem("Deeper", () => scene.Push(inner))]);
        scene.Push(new DebugMenu(null, [new DebugMenuItem("First", static () => { }), new DebugMenuItem("Nested", () => scene.Push(outer))]));
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
        Assert.Equal("Deeper", scene.Menu.Items[scene.FocusedIndex].Label);

        Press(host, Key.Left);

        Assert.Equal(string.Empty, scene.Title);
        Assert.Equal("Nested", scene.Menu.Items[scene.FocusedIndex].Label);

        Press(host, Key.Backspace);

        Assert.Equal(1, scene.Depth);
        Assert.Equal("Nested", scene.Menu.Items[scene.FocusedIndex].Label);
    }

    [Fact]
    public void ARow_ShowsItsHotkeyInTheColumnPastTheWidestLabelOrTheBareLabel()
    {
        DebugScene scene = new("~");
        scene.Push(new DebugMenu(null, [new DebugMenuItem("Go", static () => { }, DebugInput.Restart), new DebugMenuItem("Longer", static () => { })]));

        Assert.Equal("Go      R", scene.RowText(0));
        Assert.Equal("Longer", scene.RowText(1));
    }

    [Fact]
    public void ThePanel_DrawsTheBackdropFirstAndTheHighlightOnTheFocusedRowAlone()
    {
        DebugScene scene = new("~");
        scene.Push(new DebugMenu("Paused", [new DebugMenuItem("Resume", static () => { }), new DebugMenuItem("Step", static () => { })]));
        scene.SetReadout("Gameplay", 17);
        using SimulationHost host = CreateHost(scene);

        Press(host, Key.Down);

        SpriteIntent[] sprites = host.Simulation.View.ScreenSprites.ToArray();
        SpriteIntent backdrop = sprites[0];
        SpriteIntent highlight = Assert.Single(sprites, static sprite => sprite.Color == new ColorRgba(255, 255, 255, 64));
        float widest = BitmapFont.Default.Measure("[~] close   [Up/Dn] move   [Enter] select   [Bksp/Left] back").X;

        Assert.Empty(host.Simulation.View.Sprites.ToArray());
        Assert.Equal(Sprite.White, backdrop.Sprite);
        Assert.Equal(new ColorRgba(0, 0, 0, 160), backdrop.Color);
        Assert.Equal(Vector2.Zero, backdrop.Position);
        Assert.Equal(new Vector2((int)widest + 8f, (8 * LineHeight) + 8f), backdrop.Size);
        Assert.Equal(new Vector2(0f, 4f + (4 * LineHeight)), highlight.Position);
        Assert.Equal(new Vector2(backdrop.Size.X, LineHeight), highlight.Size);
        Assert.Equal("Gameplay  tick 17", scene.Readout);
        Assert.Equal("Paused", scene.Title);
    }

    [Fact]
    public void AMenuLongerThanTheWindow_HidesTheRowsOutsideItAndTheWindowFollowsTheFocus()
    {
        DebugScene scene = new("~");
        scene.Push(new DebugMenu("Long", Items(50)));
        using SimulationHost host = CreateHost(scene);

        Assert.Equal(50, scene.RowCount);
        Assert.Equal(0, scene.First);
        Assert.True(scene.IsRowShown(23));
        Assert.False(scene.IsRowShown(24));
        Assert.True(scene.RowBounds(23).Size.X > 0f);
        Assert.Equal(LineHeight, scene.RowBounds(23).Size.Y);
        Assert.Equal(Vector2.Zero, scene.RowBounds(24).Size);
        Assert.Equal("Item 24", scene.RowText(24));

        for (int press = 0; press < 24; press++)
        {
            Press(host, Key.Down);
        }

        Assert.Equal(24, scene.FocusedIndex);
        Assert.Equal(1, scene.First);
        Assert.False(scene.IsRowShown(0));
        Assert.True(scene.IsRowShown(24));

        for (int press = 0; press < 24; press++)
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
        DebugScene scene = new("~");
        scene.Push(new DebugMenu("Long", Items(50)));
        using SimulationHost host = CreateHost(scene);

        host.Step(DeviceSnapshot.Of(Key.Down));
        Assert.Equal(1, scene.FocusedIndex);

        // The delay, then one repeat every interval: 48 more moves reach item 49.
        host.Step(DebugScene.RepeatDelayFrames + (DebugScene.RepeatIntervalFrames * 47), DeviceSnapshot.Of(Key.Down));

        Assert.Equal(49, scene.FocusedIndex);
        Assert.Equal(26, scene.First);
        Assert.True(scene.IsRowShown(49));
        Assert.False(scene.IsRowShown(25));

        host.Step(DebugScene.RepeatIntervalFrames, DeviceSnapshot.Of(Key.Down));

        Assert.Equal(0, scene.FocusedIndex);
        Assert.Equal(0, scene.First);
    }

    [Fact]
    public void TheWheel_ScrollsTheWindowWithoutMovingTheFocusAndTheNextPressSnapsItBack()
    {
        DebugScene scene = new("~");
        scene.Push(new DebugMenu("Long", Items(50)));
        using SimulationHost host = CreateHost(scene);

        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -2f)));
        host.Step(DeviceSnapshot.Empty);

        Assert.Equal(6, scene.First);
        Assert.Equal(0, scene.FocusedIndex);
        Assert.False(scene.IsRowShown(0));

        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 10f)));
        Assert.Equal(0, scene.First);

        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -30f)));
        Assert.Equal(26, scene.First);
        Assert.Equal(0, scene.FocusedIndex);

        // A fine wheel: a tenth of a notch is three tenths of a row, and the fourth step makes one.
        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 0.1f)));
        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 0.1f)));
        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 0.1f)));
        Assert.Equal(26, scene.First);

        host.Step(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, 0.1f)));
        Assert.Equal(25, scene.First);

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
        DebugScene scene = new("~");
        DebugMenuItem[] items = [new("Head", static () => { }), new(string.Empty, null), new("Tail", static () => { })];
        scene.Push(new DebugMenu("Sections", items));
        using SimulationHost host = CreateHost(scene);

        Press(host, Key.Down);

        Assert.Equal(2, scene.FocusedIndex);
        Assert.Equal(string.Empty, scene.RowText(1));

        Press(host, Key.Up);
        Assert.Equal(0, scene.FocusedIndex);

        scene.Replace(new DebugMenu("Sections", [new(string.Empty, null), new("Only", static () => { })]));
        host.Step(DeviceSnapshot.Empty);

        Assert.Equal(1, scene.FocusedIndex);

        scene.Replace(new DebugMenu("Sections", [new("First", static () => { }), new(string.Empty, null)]));
        host.Step(DeviceSnapshot.Empty);

        Assert.Equal(0, scene.FocusedIndex);
    }

    // The remembered row is a note under a heading; the nearest interactive row is the heading
    // one above, not the heading two below the blank.
    [Fact]
    public void AFocusRememberedOnANote_LandsOnTheNearestInteractiveRowPreferringTheOneAbove()
    {
        DebugScene scene = new("~");
        DebugMenuItem[] items =
        [
            new("[Heading]", static () => { }),
            new("Field", static () => { }),
            new(string.Empty, null),
            new("[Next]", static () => { }),
        ];
        scene.Push(new DebugMenu("Panel", items));
        using SimulationHost host = CreateHost(scene);
        Press(host, Key.Down);
        Assert.Equal(1, scene.FocusedIndex);

        scene.Replace(new DebugMenu("Panel", [new("[Heading]", static () => { }), new("<Nothing to inspect>", null), new(string.Empty, null), new("[Next]", static () => { })]));
        host.Step(DeviceSnapshot.Empty);

        Assert.Equal(0, scene.FocusedIndex);
    }

    // Readout, blank, items on the root; readout, title, blank, items on a titled menu.
    [Fact]
    public void ABlankRow_SeparatesTheHeaderFromTheItemsWithoutDoublingOnTheUntitledRoot()
    {
        DebugScene scene = new("~");
        DebugMenu titled = new("Sub", [new DebugMenuItem("Leaf", static () => { })]);
        scene.Push(new DebugMenu(null, [new DebugMenuItem("Open", () => scene.Push(titled))]));
        using SimulationHost host = CreateHost(scene);

        Assert.Equal(4f + (2 * LineHeight), scene.RowBounds(0).Position.Y);

        Press(host, Key.Enter);

        Assert.Equal("Sub", scene.Title);
        Assert.Equal(4f + (3 * LineHeight), scene.RowBounds(0).Position.Y);
    }

    [Fact]
    public void AMenuThatFits_StillWraps()
    {
        DebugScene scene = new("~");
        scene.Push(new DebugMenu("Short", Items(3)));
        using SimulationHost host = CreateHost(scene);

        Press(host, Key.Up);

        Assert.Equal(2, scene.FocusedIndex);
        Assert.Equal(3, scene.RowCount);
        Assert.Equal("Item 2", scene.RowText(2));
    }

    private static DebugMenuItem[] Items(int count)
    {
        DebugMenuItem[] items = new DebugMenuItem[count];
        for (int index = 0; index < count; index++)
        {
            items[index] = new DebugMenuItem($"Item {index}", static () => { });
        }

        return items;
    }

    // One press: the key's step and the release after it.
    private static void Press(SimulationHost host, Key key)
    {
        host.Step(DeviceSnapshot.Of(key));
        host.Step(DeviceSnapshot.Empty);
    }

    private static SimulationHost CreateHost(DebugScene scene) =>
        new(
            scene,
            new InputState(DebugInput.Bindings()),
            run: new Run
            {
                Canvas = new Vector2(640f, 360f),
                Sampling = TextureSampling.Point,
            });
}
