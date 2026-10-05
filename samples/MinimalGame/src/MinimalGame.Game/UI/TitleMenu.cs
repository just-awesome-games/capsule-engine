using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.UI;

/// <summary>
/// The title screen's menu: the title itself, the three items, and the <see cref="FocusNavigator"/>
/// that is the one focus over them. The scene adds this and nothing else, and what each item does is
/// the menu's own.
/// </summary>
public sealed class TitleMenu : ScreenEntity
{
    // Canvas pixels down from the canvas's top edge to the title's own top edge.
    private const float TitleMargin = 28f;

    private readonly MenuItem _start = new(Anchor.Center, new Vector2(0f, -MenuItem.Spacing), "Start");
    private readonly MenuItem _options = new(Anchor.Center, Vector2.Zero, "Options");
    private readonly MenuItem _exit = new(Anchor.Center, new Vector2(0f, MenuItem.Spacing), "Exit");

    public TitleMenu()
        : base(Anchor.Fill, Vector2.Zero)
    {
        // The label fills the canvas this menu spans. Centring the line centres the title.
        Add(new Label(CapsuleAssets.Fonts.MenuFont, "Minimal Game")
        {
            Offset = new Vector2(0f, TitleMargin),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        _start.Parent = this;
        _options.Parent = this;
        _exit.Parent = this;

        // The items' order carries no layout: the navigator reads each direction from where the items
        // sit, so Up and Down walk this column and a grid needs no more than its cells listed.
        Add(new FocusNavigator(GameInput.MenuFocus, _start.Focusable, _options.Focusable, _exit.Focusable));

        _start.Pressed += StartGame;
        _options.Pressed += OpenOptions;
        _exit.Pressed += Quit;
    }

    private void StartGame() => Run.RequestScene(CapsuleAssets.Scenes.RoomScene);

    private void OpenOptions() => Run.RequestScene<Options>();

    private void Quit() => Run.RequestExit();
}
