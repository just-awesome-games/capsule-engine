using System.Globalization;
using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Localization;

// The development build's pseudo-language, qps-ploc, which shows the fallback language's text accented and
// lengthened so a layout's headroom is checked with no translation. A shipping build shows the fallback as
// it is.
internal static class PseudoLanguage
{
    internal const string Tag = "qps-ploc";

    // A lone caption of this many letters or fewer gains its whole length, where any other word gains 40%.
    private const int ShortCaption = 10;

    // Whether tag asks for the pseudo-language, whose text is the fallback's in every build.
    internal static bool Matches(string? tag) => tag is not null && LanguageMatcher.SameTag(tag, Tag);

    // Whether tag asks for the pseudo-language, in a build that rewrites it.
    internal static bool Shows(string? tag) => Development.IsSupported && Matches(tag);

    // Writes text into buffer at length, growing it while it is too short, and returns the new length. Each
    // word gains 40% of its letters rounded up, or doubles where lone says the text is a caption of one short
    // word, by repeating its vowels, else its last letter. A letter becomes its Latin-1 accented form where
    // font carries that form or no font is given. A [..] span is copied as it is, and inTag carries one left
    // open into the next call.
    internal static int Write(ReadOnlySpan<char> text, bool lone, BitmapFont? font, ref bool inTag, ref char[] buffer, int length)
    {
        bool doubles = lone && IsShortCaption(text, inTag);
        int index = 0;
        while (index < text.Length)
        {
            char c = text[index];
            if (inTag || c == '[' || !char.IsLetter(c))
            {
                inTag = inTag ? c != ']' : c == '[' && !text[index..].StartsWith("[[");
                int copied = c == '[' && !inTag ? Math.Min(2, text.Length - index) : 1;
                KeyedText.Reserve(ref buffer, length + copied);
                text.Slice(index, copied).CopyTo(buffer.AsSpan(length));
                length += copied;
                index += copied;
                continue;
            }

            int end = index;
            while (end < text.Length && InWord(text[end]))
            {
                end++;
            }

            length = WriteWord(text[index..end], doubles, font, ref buffer, length);
            index = end;
        }

        return length;
    }

    // Whether text, outside its [..] spans, holds one word of ShortCaption letters or fewer.
    private static bool IsShortCaption(ReadOnlySpan<char> text, bool inTag)
    {
        int words = 0;
        int letters = 0;
        bool inWord = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!inTag && text[i..].StartsWith("[["))
            {
                i++;
                inWord = false;
                continue;
            }

            inTag = inTag ? c != ']' : c == '[';
            bool word = !inTag && InWord(c);
            words += word && !inWord ? 1 : 0;
            letters += word && char.IsLetter(c) ? 1 : 0;
            inWord = word;
        }

        return words == 1 && letters <= ShortCaption;
    }

    private static int WriteWord(ReadOnlySpan<char> word, bool doubles, BitmapFont? font, ref char[] buffer, int length)
    {
        int letters = 0;
        int vowels = 0;
        int last = 0;
        for (int i = 0; i < word.Length; i++)
        {
            letters += char.IsLetter(word[i]) ? 1 : 0;
            vowels += IsVowel(word[i]) ? 1 : 0;
            last = char.IsLetter(word[i]) ? i : last;
        }

        // Ceiling of 40%, as 2n/5 rounded up.
        int extra = doubles ? letters : ((letters * 2) + 4) / 5;

        KeyedText.Reserve(ref buffer, length + word.Length + extra);
        int vowel = 0;
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            int repeats = vowels == 0
                ? (i == last ? extra : 0)
                : IsVowel(c) ? (extra / vowels) + (vowel++ < extra % vowels ? 1 : 0) : 0;
            char shown = Accented(c, font);
            buffer.AsSpan(length, repeats + 1).Fill(shown);
            length += repeats + 1;
        }

        return length;
    }

    // A letter, or a combining mark that belongs to the letter before it.
    private static bool InWord(char c) => char.IsLetter(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;

    private static bool IsVowel(char c) => (c | 0x20) is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    // The marks mix a ring, a circumflex and a diaeresis above with a cedilla below, to test a font's headroom both ways.
    private static char Accented(char c, BitmapFont? font)
    {
        char accented = c switch
        {
            'A' => 'Å',
            'a' => 'å',
            'C' => 'Ç',
            'c' => 'ç',
            'E' => 'É',
            'e' => 'é',
            'I' => 'Î',
            'i' => 'î',
            'N' => 'Ñ',
            'n' => 'ñ',
            'O' => 'Ö',
            'o' => 'ö',
            'U' => 'Ü',
            'u' => 'ü',
            'Y' => 'Ý',
            'y' => 'ý',
            _ => c,
        };

        return font is null || font.TryGetGlyph(accented, out _) ? accented : c;
    }
}
