using System.Numerics;
using System.Text;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Rendering;

// One small font the text specs share: two ASCII glyphs that kern against each other, a space a wrap
// can break at, one glyph outside the Basic Multilingual Plane, and one page. A label spec shows its label
// and reads back what drew through the helpers below.
internal static class FontFixtures
{
    internal const int LineHeight = 10;

    internal const int Grin = 0x1F600;

    internal static readonly TextureHandle Page = new("fonts/menu", ".png");

    internal static readonly Glyph A = new('A', 0, new TextureRegion(0, 0, 4, 6), 1, 2, 5);

    internal static readonly Glyph B = new('B', 0, new TextureRegion(4, 0, 4, 6), 0, 2, 6);

    internal static readonly Glyph Emoji = new(Grin, 0, new TextureRegion(8, 0, 8, 8), 0, 1, 9);

    // Drawn, not blank: a glyph with no texels would be culled rather than counted, which would hide
    // what a wrap did with it.
    internal static readonly Glyph Space = new(' ', 0, new TextureRegion(16, 0, 3, 6), 0, 2, 4);

    internal static readonly KerningPair AgainstB = new('A', 'B', -2);

    // "AB" laid out: A's advance, the pair's kerning behind it, then B's advance.
    internal const int WordWidth = 9;

    // "AB AB" on one line: two words and the space between them.
    internal const int TwoWordsWidth = (2 * WordWidth) + 4;

    internal static BitmapFont Font() =>
        new(LineHeight, 8, [Page], [B, A, Emoji, Space], [AgainstB]);

    // Each glyph a run of text lays out in font, with its pen and line.
    internal static (Glyph Glyph, int PenX, int Line)[] Placements(BitmapFont font, string text)
    {
        List<(Glyph, int, int)> placed = [];
        foreach (GlyphPlacement placement in new GlyphRun(font, text))
        {
            placed.Add((placement.Glyph, placement.PenX, placement.Line));
        }

        return [.. placed];
    }

    // A label on an entity at the origin, alone in a scene on run or a default one.
    internal static SimulationHost Shown(Label label, Run? run = null) =>
        SceneFixtures.Simulate(run, new SceneFixtures.Holder(Vector2.Zero, label));

    // The characters of alphabet host drew, each told by its glyph's region in font. Given a baseline, a
    // colour in angle brackets marks wherever a glyph's colour differs from the one before.
    internal static string Describe(SimulationHost host, BitmapFont font, string alphabet, ColorRgba? baseline = null)
    {
        StringBuilder text = new();
        ColorRgba? last = baseline;
        foreach (SpriteIntent drawn in host.Simulation.View.Sprites.ToArray())
        {
            if (last is { } previous && drawn.Color != previous)
            {
                last = drawn.Color;
                text.Append($"<{last.Value.R:x2}{last.Value.G:x2}{last.Value.B:x2}{last.Value.A:x2}>");
            }

            foreach (char c in alphabet)
            {
                if (font.TryGetGlyph(c, out Glyph glyph) && glyph.Region == drawn.Sprite.Region)
                {
                    text.Append(c);
                }
            }
        }

        return text.ToString();
    }
}
