namespace Capsule.Rendering;

/// <summary>How a run of text answers a box narrower than the run.</summary>
public enum TextWrap
{
    /// <summary>Lines break only where the text says; a long line runs past the box. The default.</summary>
    None,

    /// <summary>
    /// Lines break at the last space that fits, and a word wider than the box on its own breaks at
    /// the character. The space a line breaks at is not drawn and adds nothing to that line's width.
    /// </summary>
    Word,
}
