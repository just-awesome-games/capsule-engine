namespace MinimalGame.Game;

/// <summary>The prompt art for each button <see cref="GameInput"/> binds by default, all cut from one sheet.</summary>
public static class GamePrompts
{
    private static readonly Dictionary<InputButton, Sprite> Art = new()
    {
        [PadButton.South] = CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.PadSouth,
        [PadButton.East] = CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.PadEast,
        [PadButton.West] = CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.PadWest,
        [PadButton.North] = CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.PadNorth,
        [PadButton.Start] = CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.PadStart,
        [Key.Escape] = CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.KeyEscape,
        [Key.Space] = CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.KeySpace,
        [Key.Enter] = CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.KeyEnter,
        [MouseButton.Left] = CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.MouseLeft,
        [MouseButton.Right] = CapsuleAssets.Sprites.Ui.PromptsSheet.Frames.MouseRight,
    };

    /// <summary>The art for <paramref name="button"/>, or false where the sheet has none.</summary>
    public static bool TryGet(InputButton button, out Sprite sprite) => Art.TryGetValue(button, out sprite);
}
