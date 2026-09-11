using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

// One small font the text specs share: two ASCII glyphs that kern against each other, a space a wrap
// can break at, one glyph outside the Basic Multilingual Plane, and one page.
internal static class FontFixtures
{
    internal const int LineHeight = 10;

    internal const int Grin = 0x1F600;

    internal static readonly TextureHandle Page = TextureHandle.FontPage("menu", ".png");

    internal static readonly Glyph A = new('A', 0, new TextureRegion(0, 0, 4, 6), 1, 2, 5);

    internal static readonly Glyph B = new('B', 0, new TextureRegion(4, 0, 4, 6), 0, 2, 6);

    internal static readonly Glyph Emoji = new(Grin, 0, new TextureRegion(8, 0, 8, 8), 0, 1, 9);

    // Drawn, not blank: a glyph with no texels would be culled rather than counted, which would hide
    // what a wrap did with it.
    internal static readonly Glyph Space = new(' ', 0, new TextureRegion(16, 0, 3, 6), 0, 2, 4);

    // The space most fonts bake, the sample's menu.fnt among them: it advances the pen and cuts no
    // texels, so every sprite it submits culls on its own extent.
    internal static readonly Glyph BlankSpace = new(' ', 0, new TextureRegion(16, 0, 7, 0), 0, 2, 4);

    internal static readonly KerningPair AgainstB = new('A', 'B', -2);

    internal static BitmapFont Font() =>
        new(LineHeight, 8, [Page], [B, A, Emoji, Space], [AgainstB]);

    internal static BitmapFont BlankSpaceFont() =>
        new(LineHeight, 8, [Page], [B, A, Emoji, BlankSpace], [AgainstB]);
}
