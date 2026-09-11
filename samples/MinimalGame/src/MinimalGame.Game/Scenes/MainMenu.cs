using System.Numerics;
using Capsule;
using Capsule.Assets.Generated;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Input;
using Capsule.Scenes.Rendering;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// The boot scene, and the one backed by no <c>*.scene.json</c>: its public parameterless constructor
/// is what marks it class-only. Everything it draws is a <see cref="ScreenEntity"/> anchored on the
/// canvas — a title on the top edge, two items on the centre, and one highlight bar on an entity of
/// lower <see cref="Entity.ZIndex"/> so it draws under the labels. A <see cref="FocusNavigator{T}"/>
/// owns which item is focused and raises the events this scene shows that focus from.
/// </summary>
public sealed class MainMenu : Scene
{
    // Canvas pixels down from the canvas's top edge to the title's own top edge.
    private const float TitleMargin = 28f;

    // Canvas pixels between the two items' centres.
    private const float ItemSpacing = 20f;

    // Wider and taller than either caption, so the hit target is the box rather than the glyphs alone.
    private static readonly Vector2 ItemBox = new(88f, 16f);

    private static readonly ColorRgba FocusedInk = ColorRgba.Black;
    private static readonly ColorRgba RestingInk = ColorRgba.White;

    private readonly Label _start = Caption("Start");
    private readonly Label _exit = Caption("Exit");

    private readonly ColorRect _bar = new(Vector2.Zero);
    private readonly ScreenEntity _highlight = new(Anchor.TopLeft, Vector2.Zero) { ZIndex = -1 };

    private readonly FocusNavigator<Label> _focus;

    public MainMenu()
    {
        _focus = new FocusNavigator<Label>(GameInput.MenuFocus, _start, _exit);

        _focus.FocusChanged += Show;
        _focus.Activated += item =>
        {
            if (ReferenceEquals(item, _start))
            {
                RequestScene<Room>();
            }
            else
            {
                RequestExit();
            }
        };
    }

    /// <inheritdoc/>
    protected override void OnStart()
    {
        Camera.ViewportSize = World.ViewportSize;

        ScreenEntity title = new(Anchor.Top, new Vector2(0f, TitleMargin));
        title.Add(new Label(CapsuleAssets.Fonts.Menu, "Minimal Game")
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        Add(title);

        _highlight.Add(_bar);
        Add(_highlight);

        ScreenEntity start = new(Anchor.Center, new Vector2(0f, -ItemSpacing / 2f));
        start.Add(_start);
        Add(start);

        ScreenEntity exit = new(Anchor.Center, new Vector2(0f, ItemSpacing / 2f));
        exit.Add(_exit);
        Add(exit);

        // The first item's focus raises no move, and a label measures only once it is in the scene.
        Show(_focus.Focused!);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        _focus.Step(context.Input);

        if (context.Input.WasPressed(GameInput.Quit))
        {
            RequestExit();
        }
    }

    private static Label Caption(string text) =>
        new(CapsuleAssets.Fonts.Menu, text)
        {
            Size = ItemBox,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Middle,
            Color = RestingInk,
        };

    private void Show(Label focused)
    {
        foreach (Label item in _focus.Items)
        {
            item.Color = ReferenceEquals(item, focused) ? FocusedInk : RestingInk;
        }

        // Teleported, because a bar that interpolated would trail a step behind the focus it marks.
        Rect box = focused.Bounds;
        _highlight.Teleport(box.Position);
        _bar.Size = box.Size;
    }
}
