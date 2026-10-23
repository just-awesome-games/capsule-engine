using System.Buffers.Binary;
using System.Text;
using Capsule.Localization;

namespace Capsule.Tests.Localization;

// One small table the keyed text specs share, laid out as the build lays a generated one out: English
// first, then German, French and Polish, each placeholder already the marker byte and its argument's
// digit. hud.bolts is "{count, plural, =0 {No bolts} one {# bolt} other {# bolts}}" and its translations.
internal static class StringTableFixtures
{
    internal static readonly StringTable Table = new FixtureTable(
        ["en", "de", "fr", "pl"],
        fallback: 0,
        ["menu.title", "hud.score", "hud.pair", "hud.bolts"],
        [0, 1, 2, 1],
        [0, 0, 0, 1],
        ["Title", "Score: \u00010", "\u00010 of \u00011", "\u00020\u00040\u0005No bolts\u00031\u0006 bolt\u00035\u0006 bolts\u0007"],
        ["Titel", "Punkte: \u00010", "\u00011 hat \u00010", "\u00020\u00031\u0006 Bolzen\u00035\u0006 Bolzen\u0007"],
        ["Titre", "Score : \u00010", "\u00010 sur \u00011", "\u00020\u00031\u0006 boulon\u00035\u0006 boulons\u0007"],
        ["Tytuł", "Wynik: \u00010", "\u00010 z \u00011", "\u00020\u00031\u0006 śruba\u00033\u0006 śruby\u00034\u0006 śrub\u00035\u0006 śruby\u0007"]);

    internal static TextKey Title => new(Table, 0);

    // A table with no keys shipping these languages, the first its fallback, for specs of which language a run starts in.
    internal static StringTable Shipping(params string[] languages) =>
        new FixtureTable(languages, fallback: 0, [], [], [], [.. languages.Select(static _ => Array.Empty<string>())]);

    internal static TextKey Score => new(Table, 1);

    internal static TextKey Pair => new(Table, 2);

    internal static TextKey Bolts => new(Table, 3);

    private sealed class FixtureTable : StringTable
    {
        private readonly byte[] _counts;
        private readonly byte[] _plurals;
        private readonly byte[] _paths;
        private readonly byte[] _pathEnds;
        private readonly byte[] _sorted;
        private readonly byte[][] _texts;
        private readonly byte[][] _ends;

        internal FixtureTable(string[] languages, int fallback, string[] paths, byte[] counts, byte[] plurals, params string[][] texts)
            : base(languages, fallback)
        {
            _counts = counts;
            _plurals = plurals;
            (_paths, _pathEnds) = Laid(paths);
            _sorted = new byte[paths.Length * sizeof(int)];
            int[] order = [.. Enumerable.Range(0, paths.Length).OrderBy(index => Encoding.UTF8.GetBytes(paths[index]), Comparer<byte[]>.Create(static (left, right) => left.AsSpan().SequenceCompareTo(right)))];
            for (int i = 0; i < order.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(_sorted.AsSpan(i * sizeof(int)), order[i]);
            }
            _texts = new byte[texts.Length][];
            _ends = new byte[texts.Length][];
            for (int i = 0; i < texts.Length; i++)
            {
                (_texts[i], _ends[i]) = Laid(texts[i]);
            }
        }

        protected override ReadOnlySpan<byte> ArgumentCounts => _counts;

        protected override ReadOnlySpan<byte> PluralArguments => _plurals;

        protected override ReadOnlySpan<byte> SortedKeys => _sorted;

        protected override ReadOnlySpan<byte> GetText(int language, out ReadOnlySpan<byte> ends)
        {
            ends = _ends[language];

            return _texts[language];
        }

        protected override ReadOnlySpan<byte> GetKeyPaths(out ReadOnlySpan<byte> ends)
        {
            ends = _pathEnds;

            return _paths;
        }

        // English's rules for every language but Polish, whose integer rules are the CLDR's.
        protected override PluralCategory SelectCategory(int language, ulong count) => language switch
        {
            3 when count == 1 => PluralCategory.One,
            3 when count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14) => PluralCategory.Few,
            3 => PluralCategory.Many,
            _ => count == 1 ? PluralCategory.One : PluralCategory.Other,
        };

        private static (byte[] Text, byte[] Ends) Laid(string[] texts)
        {
            byte[] ends = new byte[texts.Length * sizeof(int)];
            int end = 0;
            for (int i = 0; i < texts.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(ends.AsSpan(i * sizeof(int)), end += Encoding.UTF8.GetByteCount(texts[i]));
            }

            return (Encoding.UTF8.GetBytes(string.Concat(texts)), ends);
        }
    }
}
