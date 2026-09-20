using System.Numerics;
using Capsule.Assets.Generated;
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

    // Canvas pixels between neighbouring items' centres.
    private const float ItemSpacing = 20f;

    private readonly MenuItem _start = new(Anchor.Center, new Vector2(0f, -ItemSpacing), "Start");
    private readonly MenuItem _options = new(Anchor.Center, Vector2.Zero, "Options");
    private readonly MenuItem _exit = new(Anchor.Center, new Vector2(0f, ItemSpacing), "Exit");

    public TitleMenu()
        : base(Anchor.Top, new Vector2(0f, TitleMargin))
    {
        Add(new Label(CapsuleAssets.Fonts.Menu, "Minimal Game")
        {
            Pivot = Pivot.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        // The items' order carries no layout: the navigator reads each direction from where the items
        // sit, so Up and Down walk this column and a grid needs no more than its cells listed.
        Add(new FocusNavigator(GameInput.MenuFocus, _start.Focusable, _options.Focusable, _exit.Focusable));

        _start.Pressed += StartGame;
        _options.Pressed += OpenOptions;
        _exit.Pressed += Quit;
    }

    // The items are anchored to the canvas's centre rather than to this entity, so they are the
    // scene's peers: an entity adds another by reaching the scene it has just joined.
    /// <inheritdoc/>
    protected override void OnAddedToScene()
    {
        Scene.Add(_start);
        Scene.Add(_options);
        Scene.Add(_exit);
    }

    private void StartGame() => Run.RequestScene<Room>();

    private void OpenOptions() => Run.RequestScene<Options>();

    private void Quit() => Run.RequestExit();
}
