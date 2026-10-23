using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.UI;

/// <summary>
/// The title screen's menu: the title itself, a column of three items, the <see cref="FocusNavigator"/>
/// that is the one focus over them, and a tip along the bottom that turns in place. The scene adds this
/// and nothing else, and what each item does is the menu's own.
/// </summary>
public sealed class TitleMenu : ScreenEntity
{
    // Canvas pixels down from the canvas's top edge to the title's own top edge.
    private const float TitleMargin = 28f;

    // Canvas pixels up from the canvas's bottom edge to the tip's own bottom edge.
    private const float TipMargin = 10f;

    // Steps each tip shows before the next.
    private const int TipTicks = 240;

    private readonly MenuItem _start = new(CapsuleStrings.Menu.Start);
    private readonly MenuItem _options = new(CapsuleStrings.Menu.Options);
    private readonly MenuItem _exit = new(CapsuleStrings.Menu.Exit);

    private readonly Label _tip = new(CapsuleAssets.Fonts.MenuFont, CapsuleStrings.Tips[0])
    {
        Offset = new Vector2(0f, -TipMargin),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
    };

    private readonly Sequence _tips = new();

    public TitleMenu()
        : base(Anchor.Fill, Vector2.Zero)
    {
        // The label fills the canvas this menu spans. Centring the line centres the title.
        Add(new Label(CapsuleAssets.Fonts.MenuFont, CapsuleStrings.Title)
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
        Add(_tip);

        _start.Pressed += StartGame;
        _options.Pressed += OpenOptions;
        _exit.Pressed += Quit;
    }

    protected override void OnStart() => _tips.Start(TurnTips());

    protected override void OnStep(in StepContext context) => _tips.Step();

    // Shows each tip in turn, back to the first after the last.
    private IEnumerable<Wait> TurnTips()
    {
        for (int tip = 0; ; tip = (tip + 1) % CapsuleStrings.Tips.Count)
        {
            _tip.SetText(CapsuleStrings.Tips[tip]);
            yield return Wait.Ticks(TipTicks);
        }
    }

    private void StartGame() => Run.RequestScene(CapsuleAssets.Scenes.RoomScene);

    private void OpenOptions() => Run.RequestScene<Options>();

    private void Quit() => Run.RequestExit();
}
