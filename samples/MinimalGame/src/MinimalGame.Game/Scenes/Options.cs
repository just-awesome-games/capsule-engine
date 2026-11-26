using MinimalGame.Game.UI;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// The options screen, a class-only scene showing the <see cref="OptionsMenu"/> and, along the bottom edge,
/// what the menu's buttons do.
/// </summary>
public sealed class Options : Scene
{
    // Canvas pixels between the prompts and the canvas's edges, between neighbouring prompts, between a
    // prompt's button and its caption, and between rows.
    private const float Margin = 8f;
    private const float PromptSpacing = 12f;
    private const float CaptionGap = 4f;
    private const float RowSpacing = 2f;

    public Options()
    {
        Add(new OptionsMenu());

        // Prompts too wide for one row, as a translation's can be, wrap onto centred rows.
        FlowContainer prompts = new(Anchor.Bottom, new Vector2(0f, -Margin))
        {
            MaxSize = new Vector2(World.ViewportSize.X - (2f * Margin), float.PositiveInfinity),
            Spacing = new Vector2(PromptSpacing, RowSpacing),
            Alignment = HorizontalAlignment.Center,
        };
        Prompt(prompts, GameInput.Confirm, CapsuleStrings.Prompts.Select);
        Prompt(prompts, GameInput.Reset, CapsuleStrings.Prompts.Reset);
        Prompt(prompts, GameInput.Back, CapsuleStrings.Menu.Back);
        Add(prompts);
    }

    private static void Prompt(FlowContainer prompts, InputAction action, TextKey caption)
    {
        BoxContainer prompt = new(Axis.Horizontal, Anchor.TopLeft, Vector2.Zero) { Parent = prompts, Spacing = CaptionGap };
        _ = new ActionPrompt(new ScreenEntity(Anchor.Left, Vector2.Zero) { Parent = prompt }, action);
        new ScreenEntity(Anchor.Left, Vector2.Zero) { Parent = prompt }.Add(new Label(CapsuleAssets.Fonts.MenuFont, caption));
    }
}
