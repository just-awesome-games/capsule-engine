namespace Capsule.Rendering;

/// <summary>
/// Everything a simulation wants drawn. Immutable, so a simulation builds one per
/// distinct visual state and holds it; the render path then allocates nothing.
/// A view rebuilt every frame is a defect, not a style choice.
/// </summary>
public sealed class FrameView
{
    private readonly TextBlock[] _textBlocks;

    public FrameView(params ReadOnlySpan<TextBlock> textBlocks) => _textBlocks = textBlocks.ToArray();

    /// <summary>Nothing to draw; the clear colour alone.</summary>
    public static FrameView Empty { get; } = new();

    public ReadOnlySpan<TextBlock> TextBlocks => _textBlocks;
}
