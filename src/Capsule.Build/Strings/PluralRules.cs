using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Capsule.Localization;

namespace Capsule.Build.Strings;

/// <summary>One locale's CLDR cardinal plural rules, read for integer counts only.</summary>
/// <remarks>
/// The rules are the Unicode CLDR's <c>plurals.json</c>, embedded in this assembly. A count is an integer.
/// The operands <c>n</c> and <c>i</c> are its magnitude, and <c>v</c>, <c>w</c>, <c>f</c>, <c>t</c>, <c>c</c>
/// and <c>e</c> are zero. The same model selects a category at build time and writes the C# that selects
/// it at run time.
/// </remarks>
internal sealed partial class PluralRules
{
    /// <summary>The CLDR category names, indexed by <see cref="PluralCategory"/>.</summary>
    internal static readonly string[] Names = ["zero", "one", "two", "few", "many", "other"];

    /// <summary>The largest count whose category a translation must provide a form for.</summary>
    internal const int RequiredUpTo = 999;

    private const string Resource = "Capsule.Build.Strings.plurals.json";

    private const string CategoryPrefix = "pluralRule-count-";

    private static readonly Lazy<Dictionary<string, PluralRules>> Cldr = new(Load);

    // Each category but other, in CLDR order, with the condition that selects it as alternatives of
    // relations that must all hold.
    private readonly (PluralCategory Category, Relation[][] Condition)[] _rules;

    private readonly Dictionary<PluralCategory, string[]> _samples;

    /// <summary>The CLDR locale these rules are for.</summary>
    internal string Locale { get; }

    /// <summary>The categories a plural text in this language must provide: every one a count up to <see cref="RequiredUpTo"/> selects, and other.</summary>
    /// <remarks>
    /// Settled on first read. Every build loads all of CLDR's locales and reads this for a game's few.
    /// </remarks>
    internal PluralCategory[] Required => field ??= SelectedUpTo(RequiredUpTo);

    private PluralRules(string locale, (PluralCategory, Relation[][])[] rules, Dictionary<PluralCategory, string[]> samples)
    {
        Locale = locale;
        _rules = rules;
        _samples = samples;
    }

    /// <summary>Every CLDR locale's rules, keyed by its tag.</summary>
    internal static IReadOnlyDictionary<string, PluralRules> All => Cldr.Value;

    /// <summary>
    /// The rules a shipped language uses: its own tag's, else the tag's with trailing subtags dropped, as
    /// <c>pt-BR</c> to <c>pt</c>, or null when CLDR has none.
    /// </summary>
    internal static PluralRules? For(string tag)
    {
        for (string shorter = tag; ; shorter = shorter[..shorter.LastIndexOf('-')])
        {
            if (Cldr.Value.TryGetValue(shorter, out PluralRules? rules))
            {
                return rules;
            }

            if (!shorter.Contains('-', StringComparison.Ordinal))
            {
                return null;
            }
        }
    }

    /// <summary>The category name <paramref name="name"/> spells, or null for none.</summary>
    internal static PluralCategory? Named(string name) =>
        Array.IndexOf(Names, name) is >= 0 and int index ? (PluralCategory)index : null;

    /// <summary>The category these rules select for a count of magnitude <paramref name="n"/>.</summary>
    internal PluralCategory Select(ulong n)
    {
        foreach ((PluralCategory category, Relation[][] condition) in _rules)
        {
            if (condition.Any(all => all.All(relation => relation.Holds(n))))
            {
                return category;
            }
        }

        return PluralCategory.Other;
    }

    /// <summary>Whether some integer selects <paramref name="category"/>, as CLDR's integer samples for it show.</summary>
    internal bool Produces(PluralCategory category) =>
        category == PluralCategory.Other || (_samples.TryGetValue(category, out string[]? samples) && samples.Length > 0);

    /// <summary>
    /// CLDR's integer samples of <paramref name="category"/>: each listed value and each value of a listed
    /// range. A sample in compact notation, as <c>1c6</c>, carries an exponent the integer model holds at
    /// zero, and is left out.
    /// </summary>
    internal IEnumerable<ulong> Samples(PluralCategory category)
    {
        foreach (string sample in _samples.GetValueOrDefault(category, []))
        {
            if (sample.Contains('c', StringComparison.Ordinal) || sample.Contains('e', StringComparison.Ordinal) || !char.IsAsciiDigit(sample[0]))
            {
                continue;
            }

            string[] ends = sample.Split('~');
            ulong low = ulong.Parse(ends[0], CultureInfo.InvariantCulture);
            ulong high = ends.Length == 2 ? ulong.Parse(ends[1], CultureInfo.InvariantCulture) : low;
            for (ulong value = low; value <= high; value++)
            {
                yield return value;
            }
        }
    }

    /// <summary>
    /// Appends C# statements that return the category of a <c>ulong</c> magnitude named
    /// <paramref name="count"/>, each line starting with <paramref name="indent"/>.
    /// </summary>
    internal void Write(StringBuilder code, string indent, string count)
    {
        foreach ((PluralCategory category, Relation[][] condition) in _rules)
        {
            // Relations on the operands an integer holds at zero fold away, and so does a condition they decide.
            List<string[]> alternatives = [];
            bool always = false;
            foreach (Relation[] all in condition)
            {
                if (all.Any(static relation => relation.Constant && !relation.Holds(0)))
                {
                    continue;
                }

                string[] tests = [.. all.Where(static relation => !relation.Constant).Select(relation => relation.ToCSharp(count))];
                if (tests.Length == 0)
                {
                    always = true;
                    break;
                }

                alternatives.Add(tests);
            }

            string returned = "return global::Capsule.Localization.PluralCategory." + category + ";";
            if (always)
            {
                code.Append(indent).AppendLine(returned);

                return;
            }

            if (alternatives.Count > 0)
            {
                code.Append(indent).Append("if (").AppendJoin(" || ", alternatives.Select(tests => tests.Length > 1 && alternatives.Count > 1
                    ? "(" + string.Join(" && ", tests) + ")"
                    : string.Join(" && ", tests))).AppendLine(")");
                code.Append(indent).AppendLine("{");
                code.Append(indent).Append("    ").AppendLine(returned);
                code.Append(indent).AppendLine("}");
                code.AppendLine();
            }
        }

        code.Append(indent).AppendLine("return global::Capsule.Localization.PluralCategory.Other;");
    }

    // Other, and every category a count up to last selects, in category order.
    private PluralCategory[] SelectedUpTo(int last)
    {
        HashSet<PluralCategory> selected = [PluralCategory.Other];
        for (ulong n = 0; n <= (ulong)last; n++)
        {
            selected.Add(Select(n));
        }

        return [.. selected.Order()];
    }

    private static Dictionary<string, PluralRules> Load()
    {
        using Stream stream = typeof(PluralRules).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The build carries no {Resource}. Rebuild Capsule.Build.");
        using JsonDocument document = JsonDocument.Parse(stream);
        Dictionary<string, PluralRules> locales = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty locale in document.RootElement.GetProperty("supplemental").GetProperty("plurals-type-cardinal").EnumerateObject())
        {
            List<(PluralCategory, Relation[][])> rules = [];
            Dictionary<PluralCategory, string[]> samples = [];
            foreach (JsonProperty rule in locale.Value.EnumerateObject())
            {
                PluralCategory category = Named(rule.Name[CategoryPrefix.Length..])
                    ?? throw new FormatException($"{Resource}: {locale.Name} names the unknown category {rule.Name}.");
                string text = rule.Value.GetString()!;
                int at = text.IndexOf('@', StringComparison.Ordinal);
                string condition = (at < 0 ? text : text[..at]).Trim();
                samples[category] = IntegerSamples(text);
                if (category != PluralCategory.Other)
                {
                    rules.Add((category, Parse(condition, locale.Name)));
                }
            }

            locales.Add(locale.Name, new PluralRules(locale.Name, [.. rules.OrderBy(static rule => rule.Item1)], samples));
        }

        return locales;
    }

    // The entries after "@integer", up to "@decimal" or the end, as written.
    private static string[] IntegerSamples(string rule)
    {
        int start = rule.IndexOf("@integer", StringComparison.Ordinal);
        if (start < 0)
        {
            return [];
        }

        string listed = rule[(start + "@integer".Length)..];
        int end = listed.IndexOf('@', StringComparison.Ordinal);

        return [.. (end < 0 ? listed : listed[..end]).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(static sample => sample is not ("…" or "..."))];
    }

    // condition: and_condition ('or' and_condition)*, and_condition: relation ('and' relation)*,
    // relation: operand ('%' value)? ('=' | '!=') range_list, range_list: (value | value '..' value) (',' ...)*
    private static Relation[][] Parse(string condition, string locale)
    {
        string[] tokens = [.. Token().Matches(condition).Select(static match => match.Value)];
        int at = 0;
        List<Relation[]> alternatives = [];
        List<Relation> all = [];
        while (at < tokens.Length)
        {
            char operand = tokens[at++] is [var letter] && "niwvftce".Contains(letter, StringComparison.Ordinal)
                ? letter
                : throw Refused(locale, condition);
            ulong modulus = 0;
            if (tokens[at] == "%")
            {
                modulus = ulong.Parse(tokens[at + 1], CultureInfo.InvariantCulture);
                at += 2;
            }

            bool negated = tokens[at++] switch
            {
                "=" => false,
                "!=" => true,
                _ => throw Refused(locale, condition),
            };
            List<(ulong, ulong)> ranges = [];
            while (true)
            {
                ulong low = ulong.Parse(tokens[at++], CultureInfo.InvariantCulture);
                ulong high = low;
                if (at < tokens.Length && tokens[at] == "..")
                {
                    high = ulong.Parse(tokens[at + 1], CultureInfo.InvariantCulture);
                    at += 2;
                }

                ranges.Add((low, high));
                if (at == tokens.Length || tokens[at] != ",")
                {
                    break;
                }

                at++;
            }

            all.Add(new Relation(operand, modulus, negated, [.. ranges]));
            if (at == tokens.Length || tokens[at] == "or")
            {
                alternatives.Add([.. all]);
                all.Clear();
            }
            else if (tokens[at] != "and")
            {
                throw Refused(locale, condition);
            }

            at++;
        }

        return [.. alternatives];
    }

    private static FormatException Refused(string locale, string condition) =>
        new($"{Resource}: {locale} has the rule \"{condition}\", which the plural rule grammar does not read.");

    [GeneratedRegex(@"[a-z]+|!=|=|%|\.\.|,|\d+")]
    private static partial Regex Token();

    // One operand, optionally modulo a value, in or out of a list of ranges.
    private sealed record Relation(char Operand, ulong Modulus, bool Negated, (ulong Low, ulong High)[] Ranges)
    {
        // Whether the operand is one an integer holds at zero.
        internal bool Constant => Operand is not ('n' or 'i');

        internal bool Holds(ulong n)
        {
            ulong value = Constant ? 0 : n;
            if (Modulus != 0)
            {
                value %= Modulus;
            }

            return Ranges.Any(range => value >= range.Low && value <= range.High) != Negated;
        }

        internal string ToCSharp(string count)
        {
            string operand = Modulus == 0 ? count : $"{count} % {Modulus.ToString(CultureInfo.InvariantCulture)}";
            string[] patterns = [.. Ranges.Select(static range => range.Low == range.High
                ? range.Low.ToString(CultureInfo.InvariantCulture)
                : FormattableString.Invariant($">= {range.Low} and <= {range.High}"))];
            string pattern = patterns.Length == 1
                ? patterns[0]
                : string.Join(" or ", patterns.Select(static item => item.Contains(' ', StringComparison.Ordinal) ? "(" + item + ")" : item));

            return !Negated ? $"{operand} is {pattern}"
                : pattern.Contains(' ', StringComparison.Ordinal) ? $"{operand} is not ({pattern})"
                : $"{operand} is not {pattern}";
        }
    }
}
