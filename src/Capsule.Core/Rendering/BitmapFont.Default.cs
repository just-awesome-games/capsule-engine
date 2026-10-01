// Source font: Spleen 8x16 2.2.0 by Frederic Cambus, BSD-2-Clause.
// License: THIRD-PARTY-NOTICES.md.

using Capsule.Assets;

namespace Capsule.Rendering;

public sealed partial class BitmapFont
{
    /// <summary>
    /// The engine's Spleen 8x16 monospace font. Its line metrics, glyph metrics and page regions
    /// are in font pixels, and it carries Unicode codepoints 32-126 and 160-255.
    /// </summary>
    /// <remarks>
    /// Its page is engine-owned and ships with Capsule.Runtime, with no file under <c>assets/</c>.
    /// See <c>THIRD-PARTY-NOTICES.md</c> for the BSD-2-Clause license.
    /// </remarks>
    public static BitmapFont Default { get; } = new(16, 12, [TextureHandle.DefaultFontPage], DefaultGlyphs(), []);

    // The page holds the glyphs in codepoint order, 16 cells of 8x16 to a row.
    private static Glyph[] DefaultGlyphs()
    {
        Glyph[] glyphs = new Glyph[191];
        for (int cell = 0; cell < glyphs.Length; cell++)
        {
            int codepoint = cell < 95 ? 32 + cell : 160 + cell - 95;
            glyphs[cell] = new Glyph(codepoint, 0, new TextureRegion((cell % 16) * 8, (cell / 16) * 16, 8, 16), 0, 0, 8);
        }

        return glyphs;
    }
}
