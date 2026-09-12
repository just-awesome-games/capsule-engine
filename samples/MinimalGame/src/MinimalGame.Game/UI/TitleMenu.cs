using System.Numerics;
using Capsule.Assets.Generated;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Input;
using Capsule.Scenes.Rendering;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.UI;

/// <summary>
/// The title screen's menu: the title itself, the two items, and the
/// <see cref="FocusNavigator"/> that is the one focus over them. The scene adds this and nothing
/// else — what the menu contains and what each item does are the menu's own.
/// </summary>
public sealed class TitleMenu : ScreenEntity
{
    // Canvas pixels down from the canvas's top edge to the title's own top edge.
    private const float TitleMargin = 28f;

    // Canvas pixels between the two items' centres.
    private const float ItemSpacing = 20f;

    private readonly TitleMenuItem _start = new(Anchor.Center, new Vector2(0f, -ItemSpacing / 2f), "Start");
    private readonly TitleMenuItem _exit = new(Anchor.Center, new Vector2(0f, ItemSpacing / 2f), "Exit");

    public TitleMenu()
        : base(Anchor.Top, new Vector2(0f, TitleMargin))
    {
        Add(new Label(CapsuleAssets.Fonts.Menu, "Minimal Game")
        {
            Pivot = Pivot.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        // The items' order carries no layout: the navigator reads each direction from where the items
        // sit, so Up and Down walk this column, Left and Right find nothing, and a grid needs no more
        // than its cells listed.
        Add(new FocusNavigator(GameInput.MenuFocus, _start.Focusable, _exit.Focusable));

        _start.Pressed += StartGame;
        _exit.Pressed += Quit;
    }

    // The items are anchored to the canvas's centre rather than to this entity, so they are the
    // scene's peers: an entity adds another by reaching the scene it has just joined.
    /// <inheritdoc/>
    protected override void OnAddedToScene()
    {
        Scene!.Add(_start);
        Scene!.Add(_exit);
    }

    private void StartGame() => Scene!.RequestScene<Room>();

    private void Quit() => Scene!.RequestExit();
}
