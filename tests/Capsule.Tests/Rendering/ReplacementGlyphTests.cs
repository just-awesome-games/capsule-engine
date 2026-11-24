using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

// What a codepoint the font lacks draws: the replacement character, else the question mark, else nothing,
// and a development build says so once per codepoint.
[Collection(LogSinkCollection.Name)]
public sealed class ReplacementGlyphTests : IDisposable
{
    private static readonly Glyph Replacement = new('�', 0, new TextureRegion(20, 0, 5, 6), 0, 2, 7);

    private static readonly Glyph Question = new('?', 0, new TextureRegion(26, 0, 4, 6), 0, 2, 5);

    // A tab and a soft hyphen glyph a hand-made font might carry.
    private static readonly Glyph Tab = new('	', 0, new TextureRegion(31, 0, 2, 6), 0, 2, 8);

    private static readonly Glyph SoftHyphen = new(173, 0, new TextureRegion(34, 0, 2, 6), 0, 2, 8);

    private readonly CollectingLogSink _log = new();

    public ReplacementGlyphTests() => Log.UseSink(_log);

    public void Dispose() => Log.UseSink(null);

    // The replacement advances and kerns as itself, and the pair either side of it no longer kerns. A
    // control or format character draws nothing, even in a font carrying a glyph for it.
    [Fact]
    public void ACodepointTheFontLacks_DrawsTheReplacement_AndAControlCharacterNothing()
    {
        BitmapFont font = Font(Replacement, Question);

        Assert.Equal([(FontFixtures.A, 0), (Replacement, 5), (FontFixtures.B, 11)], Placements(font, "A§B"));
        Assert.Equal([(FontFixtures.A, 0), (FontFixtures.B, 3)], Placements(font, "A\tB"));
        Assert.Equal([(FontFixtures.A, 0), (FontFixtures.B, 3)], Placements(font, "A\u200BB"));
        Assert.Equal([(FontFixtures.A, 0), (FontFixtures.B, 3)], Placements(font, "A\u00ADB"));
        Assert.Equal([(FontFixtures.A, 0), (Question, 5), (FontFixtures.B, 10)], Placements(Font(Question), "A§B"));
    }

    // A font with nothing to draw in its place warns too.
    [Fact]
    public void EachCodepointAFontLacks_WarnsOnce()
    {
        BitmapFont font = Font(Replacement);

        _ = font.Measure("§§ §");
        _ = font.Measure("¤");
        _ = Font().Measure("¶");

        Assert.Equal(3, _log.Entries.Count);
        Assert.All(_log.Entries, static entry => Assert.Equal(LogLevel.Warning, entry.Level));
        Assert.Contains("U+00A7", _log.Entries[0].Message, StringComparison.Ordinal);
        Assert.Contains("U+00A4", _log.Entries[1].Message, StringComparison.Ordinal);
        Assert.Contains("U+00B6 and draws nothing", _log.Entries[2].Message, StringComparison.Ordinal);
    }

    private static BitmapFont Font(params Glyph[] stand) =>
        new(FontFixtures.LineHeight, 8, [FontFixtures.Page], [FontFixtures.A, FontFixtures.B, FontFixtures.Space, Tab, SoftHyphen, .. stand], [FontFixtures.AgainstB, new KerningPair('�', 'B', -1)]);

    private static (Glyph Glyph, int PenX)[] Placements(BitmapFont font, string text)
    {
        List<(Glyph, int)> placed = [];
        foreach (GlyphPlacement placement in new GlyphRun(font, text))
        {
            placed.Add((placement.Glyph, placement.PenX));
        }

        return [.. placed];
    }
}
