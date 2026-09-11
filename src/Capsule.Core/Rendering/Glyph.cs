namespace Capsule.Rendering;

/// <summary>
/// One character of a <see cref="BitmapFont"/>: the texels it is cut from, and how it sits on the
/// line. Every measure is in font pixels — the units the font was baked at, which
/// <see cref="TextIntent.Scale"/> turns into world units.
/// </summary>
/// <param name="Codepoint">The Unicode scalar value this glyph draws.</param>
/// <param name="Page">The index into <see cref="BitmapFont.Pages"/> of the page it is cut from.</param>
/// <param name="Region">The glyph's texels on that page.</param>
/// <param name="XOffset">Font pixels right from the pen to the region's left edge.</param>
/// <param name="YOffset">Font pixels down from the line's top edge to the region's top edge.</param>
/// <param name="XAdvance">Font pixels the pen moves right after this glyph, before the next pair's kerning.</param>
public readonly record struct Glyph(
    int Codepoint,
    int Page,
    TextureRegion Region,
    int XOffset,
    int YOffset,
    int XAdvance);
