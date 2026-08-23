using System.Collections.Immutable;

namespace Capsule.Text;

/// <summary>
/// The engine's built-in blocky glyph set. Deliberately not a full font: glyphs
/// land when a game needs them, and a game wanting its own typeface brings one.
/// </summary>
public static class PixelFont
{
    /// <summary>Cell columns per glyph.</summary>
    public const int GlyphWidth = 5;

    /// <summary>Cell rows per glyph.</summary>
    public const int GlyphHeight = 7;

    /// <summary>Blank cell columns inserted between adjacent glyphs.</summary>
    public const int GlyphSpacing = 1;

    private static readonly Dictionary<char, ImmutableArray<string>> Glyphs = new()
    {
        [' '] =
        [
            ".....",
            ".....",
            ".....",
            ".....",
            ".....",
            ".....",
            ".....",
        ],
        ['H'] =
        [
            "#...#",
            "#...#",
            "#...#",
            "#####",
            "#...#",
            "#...#",
            "#...#",
        ],
        ['E'] =
        [
            "#####",
            "#....",
            "#....",
            "####.",
            "#....",
            "#....",
            "#####",
        ],
        ['L'] =
        [
            "#....",
            "#....",
            "#....",
            "#....",
            "#....",
            "#....",
            "#####",
        ],
        ['O'] =
        [
            ".###.",
            "#...#",
            "#...#",
            "#...#",
            "#...#",
            "#...#",
            ".###.",
        ],
        ['W'] =
        [
            "#...#",
            "#...#",
            "#...#",
            "#...#",
            "#.#.#",
            "##.##",
            "#...#",
        ],
        ['R'] =
        [
            "####.",
            "#...#",
            "#...#",
            "####.",
            "#.#..",
            "#..#.",
            "#...#",
        ],
        ['D'] =
        [
            "####.",
            "#...#",
            "#...#",
            "#...#",
            "#...#",
            "#...#",
            "####.",
        ],
    };

    /// <summary>The characters this font can lay out, in no particular order.</summary>
    public static IReadOnlyCollection<char> SupportedCharacters => Glyphs.Keys;

    public static bool Supports(char character) => Glyphs.ContainsKey(character);

    /// <summary>
    /// Rows of the glyph, top to bottom, each <see cref="GlyphWidth"/> characters
    /// wide, where '#' is a filled cell.
    /// </summary>
    /// <exception cref="ArgumentException">The character has no glyph.</exception>
    public static ImmutableArray<string> Rows(char character) =>
        Glyphs.TryGetValue(character, out ImmutableArray<string> rows)
            ? rows
            : throw new ArgumentException($"No glyph is defined for '{character}'.", nameof(character));
}
