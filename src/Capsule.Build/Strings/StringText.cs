using System.Globalization;
using System.Text;
using Capsule.Localization;

namespace Capsule.Build.Strings;

/// <summary>
/// One authored text's placeholders and plural forms: <c>{name}</c> fills an argument, and
/// <c>{count, plural, one {# bolt} other {# bolts}}</c> picks a form by an integer argument.
/// </summary>
/// <remarks>
/// <para>
/// A plural form is the ICU MessageFormat subset: branches selected by an exact <c>=N</c> or by one of the
/// CLDR categories <c>zero</c>, <c>one</c>, <c>two</c>, <c>few</c>, <c>many</c> and <c>other</c>, and
/// <c>other</c> always present. A branch holds text, placeholders, and <c>#</c> for the count's text. A
/// plural inside a branch, <c>select</c>, <c>selectordinal</c> and <c>offset:</c> are refused.
/// </para>
/// <para>
/// Braces are always structural. An apostrophe quotes one character as ICU does: <c>'{'</c> and
/// <c>'}'</c> are literal braces anywhere, and <c>'#'</c> a literal <c>#</c> in a branch. Every other
/// apostrophe is text, and <c>'#'</c> outside a branch is text too.
/// </para>
/// <para>
/// The build resolves all of it before the text ships, in the encoding <c>StringTable</c> reads, and the
/// runtime never reads a brace.
/// </para>
/// </remarks>
internal static class StringText
{
    /// <summary>The most placeholders a text may name, which is how many arguments a label takes.</summary>
    internal const int MaxPlaceholders = 3;

    /// <summary>The distinct placeholder names in <paramref name="text"/>, a plural's selector among them, in order of first appearance.</summary>
    /// <param name="path">The key path, which starts every refusal.</param>
    /// <exception cref="FormatException">The text breaks the syntax, holds a control character other than <c>\n</c>, or names more than three placeholders.</exception>
    internal static string[] Placeholders(string text, string path)
    {
        List<string> names = [];
        Named(Parse(text, path), names);

        return names.Count <= MaxPlaceholders
            ? [.. names]
            : throw new FormatException(
                $"{path}: names {names.Count} placeholders ({Listed(names)}). A label fills at most {MaxPlaceholders}. Split the text, or fill the rest before it.");
    }

    /// <summary>Every plural form in <paramref name="text"/>, in order.</summary>
    /// <exception cref="FormatException">The text breaks the syntax.</exception>
    internal static Plural[] Plurals(string text, string path) => [.. Parse(text, path).OfType<Plural>()];

    /// <summary>Appends <paramref name="text"/> as it ships: braces resolved, each placeholder and plural encoded with the index <paramref name="order"/> gives its name.</summary>
    /// <param name="order">The source text's placeholder names, in order of first appearance there.</param>
    internal static void Resolve(string text, string path, string[] order, StringBuilder resolved) =>
        Encode(Parse(text, path), order, resolved);

    /// <summary>Names as a refusal lists them, as <c>{a}, {b}</c>, or <c>none</c>.</summary>
    internal static string Listed(IEnumerable<string> names) =>
        string.Join(", ", names.Select(static name => "{" + name + "}")) is { Length: > 0 } listed ? listed : "none";

    private static void Named(IEnumerable<Part> parts, List<string> names)
    {
        foreach (Part part in parts)
        {
            if (part is Hole { Name: var hole } && !names.Contains(hole))
            {
                names.Add(hole);
            }
            else if (part is Plural plural)
            {
                if (!names.Contains(plural.Selector))
                {
                    names.Add(plural.Selector);
                }

                foreach (Branch branch in plural.Branches)
                {
                    Named(branch.Body, names);
                }
            }
        }
    }

    private static void Encode(IEnumerable<Part> parts, string[] order, StringBuilder resolved)
    {
        foreach (Part part in parts)
        {
            switch (part)
            {
                case Run run:
                    resolved.Append(run.Text);
                    break;

                case Hole hole:
                    resolved.Append((char)StringTable.Placeholder).Append(Digit(order, hole.Name));
                    break;

                case CountMark:
                    resolved.Append((char)StringTable.Count);
                    break;

                case Plural plural:
                    resolved.Append((char)StringTable.PluralOpen).Append(Digit(order, plural.Selector));
                    foreach (Branch branch in plural.Branches.OrderBy(static branch => branch.Exact is null))
                    {
                        if (branch.Exact is { } exact)
                        {
                            resolved.Append((char)StringTable.ExactBranch).Append(exact.ToString(CultureInfo.InvariantCulture)).Append((char)StringTable.ExactEnd);
                        }
                        else
                        {
                            resolved.Append((char)StringTable.CategoryBranch).Append((char)('0' + (int)branch.Category!.Value));
                        }

                        Encode(branch.Body, order, resolved);
                    }

                    resolved.Append((char)StringTable.PluralClose);
                    break;
            }
        }
    }

    private static char Digit(string[] order, string name) => (char)('0' + Array.IndexOf(order, name));

    private static List<Part> Parse(string text, string path)
    {
        int at = 0;

        return new Parser(text, path).Parts(ref at, inBranch: false);
    }

    /// <summary>One piece of a parsed text.</summary>
    internal abstract record Part;

    /// <summary>Literal text, braces already resolved.</summary>
    internal sealed record Run(string Text) : Part;

    /// <summary>A <c>{name}</c> placeholder.</summary>
    internal sealed record Hole(string Name) : Part;

    /// <summary>A <c>#</c> in a plural branch, which shows the count.</summary>
    internal sealed record CountMark : Part;

    /// <summary>A plural form selecting a branch by the integer argument <paramref name="Selector"/> names.</summary>
    internal sealed record Plural(string Selector, Branch[] Branches) : Part;

    /// <summary>One branch of a plural form: an exact count or a category, and its text.</summary>
    internal sealed record Branch(ulong? Exact, PluralCategory? Category, Part[] Body)
    {
        /// <summary>The selector as authored, as <c>=0</c> or <c>few</c>.</summary>
        internal string Selector => Exact is { } exact ? "=" + exact.ToString(CultureInfo.InvariantCulture) : PluralRules.Names[(int)Category!.Value];
    }

    private sealed class Parser(string text, string path)
    {
        // Reads parts from at, through the end of the text or the brace that closes the branch it is in.
        internal List<Part> Parts(ref int at, bool inBranch)
        {
            List<Part> parts = [];
            StringBuilder literal = new();
            while (true)
            {
                if (at == text.Length)
                {
                    if (inBranch)
                    {
                        throw Unclosed();
                    }

                    Flush(parts, literal);

                    return parts;
                }

                char character = text[at];
                if (character == '\'' && at + 2 < text.Length && text[at + 2] == '\'' && (text[at + 1] is '{' or '}' || (inBranch && text[at + 1] == '#')))
                {
                    literal.Append(text[at + 1]);
                    at += 3;
                }
                else if (character is '{' or '}' && at + 1 < text.Length && text[at + 1] == character && !(character == '}' && inBranch))
                {
                    throw new FormatException(
                        $"{path}: has \"{character}{character}\" at character {at + 1}. Braces always open or close a placeholder or plural. Write '{character}' for a literal brace.");
                }
                else if (character == '{')
                {
                    Flush(parts, literal);
                    parts.Add(Placeholder(ref at, inBranch));
                }
                else if (character == '}' && inBranch)
                {
                    Flush(parts, literal);
                    at++;

                    return parts;
                }
                else if (character == '}')
                {
                    throw new FormatException($"{path}: has a '}}' at character {at + 1} that closes no placeholder. Write '}}' for a literal brace.");
                }
                else if (inBranch && character == '#')
                {
                    Flush(parts, literal);
                    parts.Add(new CountMark());
                    at++;
                }
                else if (char.IsControl(character) && character != '\n')
                {
                    throw new FormatException(
                        $"{path}: has the control character U+{(int)character:X4} at character {at + 1}. A text holds no control character but \\n, which breaks the line.");
                }
                else if (char.IsSurrogate(character) && !(char.IsHighSurrogate(character) && at + 1 < text.Length && char.IsLowSurrogate(text[at + 1])))
                {
                    throw new FormatException($"{path}: has a lone UTF-16 surrogate at character {at + 1}, which no UTF-8 text holds. Write the whole character.");
                }
                else
                {
                    literal.Append(character);
                    if (char.IsHighSurrogate(character))
                    {
                        literal.Append(text[++at]);
                    }

                    at++;
                }
            }
        }

        private static void Flush(List<Part> parts, StringBuilder literal)
        {
            if (literal.Length > 0)
            {
                parts.Add(new Run(literal.ToString()));
                literal.Clear();
            }
        }

        // Reads a placeholder or a plural form from its opening brace.
        private Part Placeholder(ref int at, bool inBranch)
        {
            int open = at;
            string name = Name(at + 1);
            at += 1 + name.Length;
            if (name.Length > 0 && at < text.Length && text[at] == '}')
            {
                at++;

                return new Hole(name);
            }

            Whitespace(ref at);
            if (name.Length == 0 || at == text.Length || text[at] != ',')
            {
                throw new FormatException(
                    $"{path}: has a '{{' at character {open + 1} that starts no placeholder. Write {{name}} with a name of letters, digits and '_', or '{{' for a literal brace.");
            }

            at++;
            Whitespace(ref at);
            string form = Word(ref at);
            if (inBranch)
            {
                throw new FormatException(
                    $"{path}: has \"{{{name}, {form}\" inside a plural branch. A plural or select form nested in a branch is not supported. Write each combination as a branch of one plural, or split the text.");
            }

            if (form != "plural")
            {
                throw new FormatException(form is "select" or "selectordinal"
                    ? $"{path}: has \"{{{name}, {form}\", a {form} form, which is not supported. Write one text per case under keys of their own, or a plural on an integer."
                    : $"{path}: has \"{{{name}, {form}\", which is no form a text supports. Write {{{name}, plural, one {{...}} other {{...}}}}.");
            }

            Whitespace(ref at);
            if (at == text.Length || text[at] != ',')
            {
                throw new FormatException($"{path}: has a plural on {{{name}}} with no ',' before its branches. Write {{{name}, plural, one {{...}} other {{...}}}}.");
            }

            at++;
            Whitespace(ref at);
            if (text.AsSpan(at).StartsWith("offset:", StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"{path}: has a plural on {{{name}}} with offset:, which is not supported. Select on the count the text shows, and write exact branches as =N.");
            }

            List<Branch> branches = [];
            while (true)
            {
                Whitespace(ref at);
                if (at == text.Length)
                {
                    throw Unclosed();
                }

                if (text[at] == '}')
                {
                    at++;
                    break;
                }

                Branch branch = Selector(ref at, name);
                if (branches.Any(other => other.Selector == branch.Selector))
                {
                    throw new FormatException($"{path}: has a plural on {{{name}}} with two {branch.Selector} branches. Keep one.");
                }

                branches.Add(branch);
            }

            return branches.Any(static branch => branch.Category == PluralCategory.Other)
                ? new Plural(name, [.. branches])
                : throw new FormatException($"{path}: has a plural on {{{name}}} with no other branch. Add other {{...}}, which every plural needs.");
        }

        // Reads one branch: its selector, its opening brace and its text through the brace that closes it.
        private Branch Selector(ref int at, string selector)
        {
            ulong? exact = null;
            PluralCategory? category = null;
            int start = at;
            if (text[at] == '=')
            {
                at++;
                int digits = at;
                while (at < text.Length && char.IsAsciiDigit(text[at]))
                {
                    at++;
                }

                exact = ulong.TryParse(text.AsSpan(digits, at - digits), NumberStyles.None, CultureInfo.InvariantCulture, out ulong value)
                    ? value
                    : throw new FormatException(
                        $"{path}: has a plural on {{{selector}}} with the selector \"{text[start..at]}\". Write an exact selector as = and a whole number from 0 to {ulong.MaxValue}.");
            }
            else
            {
                string word = Word(ref at);
                category = PluralRules.Named(word) ?? throw new FormatException(
                    $"{path}: has a plural on {{{selector}}} with the selector \"{(word.Length > 0 ? word : text[at].ToString())}\", which is no plural category. Write zero, one, two, few, many, other, or =N for an exact count.");
            }

            Whitespace(ref at);
            if (at == text.Length || text[at] != '{')
            {
                throw new FormatException($"{path}: has a plural on {{{selector}}} whose {text[start..at].Trim()} branch opens no text. Write {text[start..at].Trim()} {{...}}.");
            }

            at++;

            return new Branch(exact, category, [.. Parts(ref at, inBranch: true)]);
        }

        // The name of letters, digits and '_' starting at index, not starting with a digit, or empty.
        private string Name(int index)
        {
            int end = index;
            while (end < text.Length && (text[end] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_' || (end > index && char.IsAsciiDigit(text[end]))))
            {
                end++;
            }

            return text[index..end];
        }

        private string Word(ref int at)
        {
            int start = at;
            while (at < text.Length && char.IsAsciiLetter(text[at]))
            {
                at++;
            }

            return text[start..at];
        }

        private void Whitespace(ref int at)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }
        }

        private FormatException Unclosed() =>
            new($"{path}: has a plural whose braces do not close. End each branch with }} and the plural with another }}.");
    }
}
