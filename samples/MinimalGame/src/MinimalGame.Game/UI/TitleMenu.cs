using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.UI;

/// <summary>
/// The title screen's menu: the title itself, a column of three items, and the <see cref="FocusNavigator"/>
/// that is the one focus over them. The scene adds this and nothing else, and what each item does is
/// the menu's own.
/// </summary>
public sealed class TitleMenu : ScreenEntity
{
    // Canvas pixels down from the canvas's top edge to the title's own top edge.
    private const float TitleMargin = 28f;

    private readonly MenuItem _start = new("Start");
    private readonly MenuItem _options = new("Options");
    private readonly MenuItem _exit = new("Exit");

    public TitleMenu()
        : base(Anchor.Fill, Vector2.Zero)
    {
        // The label fills the canvas this menu spans. Centring the line centres the title.
        Add(new Label(CapsuleAssets.Fonts.MenuFont, "Minimal Game")
        {
            Offset = new Vector2(0f, TitleMargin),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        BoxContainer column = new(Axis.Vertical, Anchor.Center, Vector2.Zero) { Parent = this, Spacing = MenuItem.Spacing };
        _start.Parent = column;
        _options.Parent = column;
        _exit.Parent = column;

        // The navigator gathers the items under this menu and reads each direction from where they sit.
        Add(new FocusNavigator(GameInput.MenuFocus));

        _start.Pressed += StartGame;
        _options.Pressed += OpenOptions;
        _exit.Pressed += Quit;
    }

    private void StartGame() => Run.RequestScene(CapsuleAssets.Scenes.RoomScene);

    private void OpenOptions() => Run.RequestScene<Options>();

    private void Quit() => Run.RequestExit();
}
