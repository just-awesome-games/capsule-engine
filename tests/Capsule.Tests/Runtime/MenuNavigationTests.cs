using System.Numerics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Scenes.Input;
using Capsule.Scenes.Rendering;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Rendering;

namespace Capsule.Tests.Runtime;

// One menu of two screen-space items, reached three ways: by gamepad, by pointer, and through the
// whole host. The canvas the items are laid out in is the run's, so the box the pointer hits is the
// same one headless as it is under a window.
[Collection(LogSinkCollection.Name)]
public sealed class MenuNavigationTests
{
    private static readonly Vector2 Canvas = new(200f, 120f);

    // The item boxes, which the entities' positions are the top-left corners of.
    private static readonly Vector2 ItemBox = new(60f, 20f);

    // Inside the second item's box, which spans (20, 60) to (80, 80) on that canvas.
    private static readonly Vector2 OnTheSecondItem = new(50f, 70f);

    private static readonly InputAction Up = new("Up");
    private static readonly InputAction Down = new("Down");
    private static readonly InputAction Left = new("Left");
    private static readonly InputAction Right = new("Right");
    private static readonly InputAction Confirm = new("Confirm");
    private static readonly InputAction Click = new("Click");

    private static readonly FocusActions Actions = new(Up, Down, Left, Right, Confirm, Click);

    [Fact]
    public void AGamepadAndAPointer_PressTheSameItemOfTheSameMenu()
    {
        Assert.Equal("second", Play(ByGamepad()));
        Assert.Equal("second", Play(ByPointer()));
    }

    // The canvas is a run constant the simulation knows, so the pointer lands on the same item with no
    // window, no device and nothing drawn.
    [Fact]
    public void AHeadlessRun_PressesThatItemByPointerToo()
    {
        Menu? played = null;

        HeadlessRunResult result = CapsuleEngine.Configure(
                "Menu Game",
                new SceneRegistry(
                    new EntityRegistry([]),
                    [SceneRegistration.Plain(typeof(Menu), () => played = new Menu())]))
            .WithRenderResolution((int)Canvas.X, (int)Canvas.Y)
            .WithInput(Bind)
            .WithoutCrashLog()
            .WithoutLogging()
            .RunHeadless<Menu>(ByPointer());

        Assert.True(result.ExitRequested);
        Assert.Equal("second", played?.Pressed);
    }

    private static string? Play(IInputDriver driver)
    {
        Menu menu = new();

        using SceneRun run = new(menu, new InputState(Bound(new ActionBindings())), canvas: Canvas);
        run.Play(driver);

        Assert.True(menu.SecondIsFocused);

        return menu.Pressed;
    }

    // One step to move the focus down, one to confirm it.
    private static IInputDriver ByGamepad() =>
        new InputScript().Tap(PadButton.DPadDown).Tap(PadButton.South).Build();

    // One step of pointer movement onto the second item, which focuses it, then a click on it.
    private static IInputDriver ByPointer() =>
        new InputScript().MoveTo(OnTheSecondItem).Wait(1).Tap(MouseButton.Left).Build();

    private static void Bind(InputConfiguration input) => Bound(input.Bindings);

    private static ActionBindings Bound(ActionBindings bindings) =>
        bindings
            .Bind(Up, Key.Up, PadButton.DPadUp)
            .Bind(Down, Key.Down, PadButton.DPadDown)
            .Bind(Left, Key.Left, PadButton.DPadLeft)
            .Bind(Right, Key.Right, PadButton.DPadRight)
            .Bind(Confirm, Key.Enter, PadButton.South)
            .Bind(Click, MouseButton.Left);

    // The shape a game's menu takes: the scene instantiates its items in its constructor and the items
    // own what pressing them means.
    private sealed class Menu : Scene
    {
        private readonly Item _second;

        internal Menu()
        {
            Item first = new(new Vector2(20f, 20f));
            _second = new Item(new Vector2(20f, 60f));

            Add(first);
            Add(_second);
            Add(new Holder(new FocusNavigator(Actions, first.Focusable, _second.Focusable)));

            first.Focusable.Pressed += () => Press("first");
            _second.Focusable.Pressed += () => Press("second");
        }

        internal string? Pressed { get; private set; }

        internal bool SecondIsFocused => _second.Focusable.IsFocused;

        private void Press(string item)
        {
            Pressed = item;
            RequestExit();
        }
    }

    // A label in a 60x20 box on the screen layer, anchored to the canvas's top-left corner so its box
    // is the canvas pixels the entity was offset to, with a focusable over the same box.
    private sealed class Item : ScreenEntity
    {
        internal Item(Vector2 offset)
            : base(Anchor.TopLeft, offset)
        {
            // 'A' is a glyph the fixture font carries, so the run lays out a box rather than nothing.
            Add(new Label(FontFixtures.Font(), "A") { Size = ItemBox });
            Add(Focusable);
        }

        internal Focusable Focusable { get; } = new(ItemBox);
    }

    // The navigator is a component like any other, so it rides an entity of its own.
    private sealed class Holder : ScreenEntity
    {
        internal Holder(Component navigator)
            : base(Anchor.TopLeft, Vector2.Zero) =>
            Add(navigator);
    }
}
