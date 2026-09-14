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
        Assert.Equal(new Vector2((int)widest + 8f, (7 * LineHeight) + 8f), backdrop.Size);
        Assert.Equal(new Vector2(0f, 4f + (3 * LineHeight)), highlight.Position);
        Assert.Equal(new Vector2(backdrop.Size.X, LineHeight), highlight.Size);
        Assert.Equal("Gameplay  tick 17", scene.Readout);
        Assert.Equal("Paused", scene.Title);
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
