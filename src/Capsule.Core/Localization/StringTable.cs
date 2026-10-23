using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;

namespace Capsule.Localization;

/// <summary>The base of the generated <c>CapsuleStrings.Table</c>, which holds every language's texts. Generated code derives it.</summary>
/// <remarks>
/// Each language's texts are one UTF-8 block in the same key order, with each placeholder already
/// resolved to its argument index. Every offset is a little-endian 32-bit integer. Only the run's active
/// language is ever read.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class StringTable
{
    // A text is UTF-8 with these control bytes, and no other:
    //   0x01 d          a placeholder, d the ASCII digit of its argument index
    //   0x02 d          opens a plural block selecting on argument d, followed by its branches
    //   0x03 c          a branch for the category whose PluralCategory value is the ASCII digit c
    //   0x04 N 0x05     a branch for the exact count N, in ASCII digits
    //   0x06            the count's text inside a branch
    //   0x07            closes the plural block
    //   0x08            leads a text a translation lacks, which holds the source language's text, and
    //                   selects its plural forms by the source language's rules
    // A branch's body runs to the next 0x03, 0x04 or 0x07. Exact branches come before category branches,
    // and other is always present.
    internal const byte Placeholder = 0x01;
    internal const byte PluralOpen = 0x02;
    internal const byte CategoryBranch = 0x03;
    internal const byte ExactBranch = 0x04;
    internal const byte ExactEnd = 0x05;
    internal const byte Count = 0x06;
    internal const byte PluralClose = 0x07;
    internal const byte SourceText = 0x08;

    private readonly string[] _languages;

    // The shipped tags, the source language first, which a language index counts.
    internal ReadOnlySpan<string> Languages => _languages;

    // The index a request no shipped language matches resolves to.
    internal int Fallback { get; }

    // How many keys every language holds.
    internal int KeyCount => ArgumentCounts.Length;

    /// <summary>How many arguments each key's text takes, by key index.</summary>
    protected abstract ReadOnlySpan<byte> ArgumentCounts { get; }

    /// <summary>Which arguments of each key's text select a plural form, by key index, as bit <c>i</c> for argument <c>i</c>.</summary>
    protected abstract ReadOnlySpan<byte> PluralArguments { get; }

    /// <summary>Every key index as a little-endian 32-bit integer, in ordinal order of the key paths' UTF-8 bytes.</summary>
    protected abstract ReadOnlySpan<byte> SortedKeys { get; }

    /// <summary>Starts a table over <paramref name="languages"/>, falling back to the one at <paramref name="fallback"/>.</summary>
    /// <param name="languages">The shipped BCP 47 tags: the source language first, then the rest in ordinal order.</param>
    /// <param name="fallback">The index in <paramref name="languages"/> a run with no matching language shows.</param>
    protected StringTable(string[] languages, int fallback)
    {
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentOutOfRangeException.ThrowIfZero(languages.Length, nameof(languages));
        ArgumentOutOfRangeException.ThrowIfNegative(fallback);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fallback, languages.Length);

        _languages = languages;
        Fallback = fallback;
    }

    /// <summary>The text whose key path is <paramref name="path"/>, or null when the table holds none. Generated code calls it.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TextKey? Find(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // Only a scene's composition searches. A path too long for the stack allocates its bytes.
        const int StackBytes = 256;
        int length = Encoding.UTF8.GetByteCount(path);
        Span<byte> query = length <= StackBytes ? stackalloc byte[StackBytes] : new byte[length];
        query = query[..Encoding.UTF8.GetBytes(path, query)];

        ReadOnlySpan<byte> paths = GetKeyPaths(out ReadOnlySpan<byte> ends);
        ReadOnlySpan<byte> sorted = SortedKeys;
        int low = 0;
        int high = (sorted.Length / sizeof(int)) - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int index = BinaryPrimitives.ReadInt32LittleEndian(sorted[(middle * sizeof(int))..]);
            int order = Entry(paths, ends, index).SequenceCompareTo(query);
            if (order == 0)
            {
                return new TextKey(this, index);
            }

            if (order < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return null;
    }

    // The language a requested tag shows: the closest shipped one, or the fallback for null or no match.
    internal int Resolve(string? tag)
    {
        if (tag is null)
        {
            return Fallback;
        }

        int matched = LanguageMatcher.Match(_languages, new ReadOnlySpan<string>(in tag));

        return matched < 0 ? Fallback : matched;
    }

    // The one seam every lookup goes through: a language's texts, and each key's exclusive end in them.
    internal ReadOnlySpan<byte> Text(int language, out ReadOnlySpan<byte> ends) => GetText(language, out ends);

    // One key's encoded text in one language.
    internal ReadOnlySpan<byte> Template(int language, int index)
    {
        ReadOnlySpan<byte> texts = Text(language, out ReadOnlySpan<byte> ends);

        return Entry(texts, ends, index);
    }

    internal int ArgumentCount(int index) => ArgumentCounts[index];

    internal int PluralMask(int index) => PluralArguments[index];

    // The category language's rules select for a count of this magnitude, whose sign they ignore.
    internal PluralCategory Category(int language, ulong magnitude) => SelectCategory(language, magnitude);

    // A key's dot-joined path, as its source language authors it.
    internal string KeyPath(int index)
    {
        ReadOnlySpan<byte> paths = GetKeyPaths(out ReadOnlySpan<byte> ends);

        return Encoding.UTF8.GetString(Entry(paths, ends, index));
    }

    // The body of the branch a plural block selects for a count of magnitude and sign: an exact branch
    // equal to a non-negative count, else the one for category, else other. The block holds its branches,
    // from after the opening argument's digit to before the closing byte.
    internal static ReadOnlySpan<byte> Branch(ReadOnlySpan<byte> block, ulong magnitude, bool negative, PluralCategory category)
    {
        ReadOnlySpan<byte> chosen = default;
        ReadOnlySpan<byte> other = default;
        bool found = false;
        while (!block.IsEmpty)
        {
            byte kind = block[0];
            int header = kind == ExactBranch ? block.IndexOf(ExactEnd) + 1 : 2;
            int length = block[header..].IndexOfAny(CategoryBranch, ExactBranch);
            ReadOnlySpan<byte> body = length < 0 ? block[header..] : block.Slice(header, length);
            if (kind == ExactBranch)
            {
                ulong exact = 0;
                foreach (byte digit in block[1..(header - 1)])
                {
                    exact = (exact * 10) + (ulong)(digit - '0');
                }

                if (!negative && exact == magnitude)
                {
                    return body;
                }
            }
            else if ((PluralCategory)(block[1] - '0') == category)
            {
                chosen = body;
                found = true;
            }
            else if ((PluralCategory)(block[1] - '0') == PluralCategory.Other)
            {
                other = body;
            }

            block = length < 0 ? default : block[(header + length)..];
        }

        return found ? chosen : other;
    }

    /// <summary>The texts of the language at <paramref name="language"/>, and each key's exclusive end offset in them.</summary>
    protected abstract ReadOnlySpan<byte> GetText(int language, out ReadOnlySpan<byte> ends);

    /// <summary>Every key's dot-joined path as UTF-8, and each key's exclusive end offset in it.</summary>
    protected abstract ReadOnlySpan<byte> GetKeyPaths(out ReadOnlySpan<byte> ends);

    /// <summary>The plural category the rules of the language at <paramref name="language"/> select for a count of magnitude <paramref name="count"/>.</summary>
    /// <param name="language">The index of the language in the shipped tags.</param>
    /// <param name="count">The count's magnitude, its sign dropped.</param>
    protected abstract PluralCategory SelectCategory(int language, ulong count);

    private static ReadOnlySpan<byte> Entry(ReadOnlySpan<byte> blob, ReadOnlySpan<byte> ends, int index)
    {
        int start = index == 0 ? 0 : BinaryPrimitives.ReadInt32LittleEndian(ends[((index - 1) * sizeof(int))..]);

        return blob[start..BinaryPrimitives.ReadInt32LittleEndian(ends[(index * sizeof(int))..])];
    }
}
