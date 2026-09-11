using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Scenes.Input;
using Capsule.Scenes.Rendering;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Rendering;

namespace Capsule.Tests.Runtime;

// One menu of two screen-space labels, reached three ways: by gamepad, by pointer, and through the
// whole host. The canvas the labels are laid out in is the run's, so the rect the pointer hits is the
// same one headless as it is under a window.
[Collection(LogSinkCollection.Name)]
public sealed class MenuNavigationTests
{
    private static readonly Vector2 Canvas = new(200f, 120f);

    // Inside the second label's box, which spans (20, 60) to (80, 80) on that canvas.
    private static readonly Vector2 OnTheSecondItem = new(50f, 70f);

    private static readonly InputAction Up = new("Up");
    private static readonly InputAction Down = new("Down");
    private static readonly InputAction Confirm = new("Confirm");
    private static readonly InputAction Click = new("Click");

    [Fact]
    public void AGamepadAndAPointer_ActivateTheSameItemOfTheSameMenu()
    {
        Assert.Equal("second", Play(ByGamepad()));
        Assert.Equal("second", Play(ByPointer()));
    }

    // The canvas is a run constant the simulation knows, so the pointer lands on the same label with no
    // window, no device and nothing drawn.
    [Fact]
    public void AHeadlessRun_ActivatesThatItemByPointerToo()
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
        Assert.Equal("second", played?.Activated);
        Assert.Equal(1, played?.FocusedIndex);
    }

    private static string? Play(IInputDriver driver)
    {
        Menu menu = new();

        using SceneRun run = new(menu, new InputState(Bound(new ActionBindings())), canvas: Canvas);
        run.Play(driver);

        Assert.Equal(1, menu.FocusedIndex);

        return menu.Activated;
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
            .Bind(Confirm, Key.Enter, PadButton.South)
            .Bind(Click, MouseButton.Left);

    private sealed class Menu : Scene
    {
        private readonly FocusNavigator _focus = new();

        private Item _first = null!;
        private Item _second = null!;

        internal string? Activated { get; private set; }

        internal int FocusedIndex => _focus.FocusedIndex;

        protected override void OnStart()
        {
            _first = new Item(new Vector2(20f, 20f));
            _second = new Item(new Vector2(20f, 60f));

            Add(_first);
            Add(_second);

            _focus.Add(_first.Caption);
            _focus.Add(_second.Caption);
        }

        protected override void OnStep(in StepContext context)
        {
            _focus.Step(context.Input, Up, Down, Confirm, Click);

            if (!_focus.Activated)
            {
                return;
            }

            Activated = ReferenceEquals(_focus.Focused, _second.Caption) ? "second" : "first";
            RequestExit();
        }
    }

    // A label in a 60x20 box on the screen layer, anchored to the canvas's top-left corner so its box
    // is the canvas pixels the entity was placed at.
    private sealed class Item : Entity
    {
        internal Item(Vector2 position)
            : base(position)
        {
            Space = RenderSpace.Screen;

            // 'A' is a glyph the fixture font carries, so the run lays out a box rather than nothing.
            Caption = new Label(FontFixtures.Font(), "A") { Size = new Vector2(60f, 20f) };

            Add(Caption);
        }

        internal Label Caption { get; }
    }
}
