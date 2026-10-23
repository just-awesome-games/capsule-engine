using System.Text;
using Capsule.Build.Caching;
using Capsule.Localization;

namespace Capsule.Build.Strings;

/// <summary>
/// Reads every string table, checks each translation against the source language, and generates
/// <c>CapsuleStrings</c> declaring a key per source text and a list per source array.
/// </summary>
/// <remarks>
/// Only the source language's files declare keys. Every language's texts ship in the same key order: the
/// source files by asset key, then each file's document order. A key a translation lacks shows the source
/// text in a development build, and fails a shipping one. A list's texts are keys like any other, and a
/// translation holds the source's count. A plural form in each language provides every
/// category that language's CLDR rules select for a count up to 999.
/// </remarks>
internal static class StringsStep
{
    private const string Step = "strings";

    /// <summary>The generated <c>CapsuleStrings</c> source, or null when the game authors no string table or a table has a defect.</summary>
    internal static string? Run(PipelinePass pass)
    {
        Source[] sources = [.. pass.Of(AssetType.Strings)];
        if (sources.Length == 0)
        {
            return null;
        }

        List<(Source Source, StringDocument Document)> read = pass.Each(
            Step,
            sources,
            source => Derivation.Of(source),
            (source, _) => StringFile.Read(source.Path),
            DerivationCacheJsonContext.Default.StringDocument);
        if (read.Count < sources.Length)
        {
            return null;
        }

        int failures = pass.Failures;
        SortedDictionary<string, List<(Source Source, StringDocument Document)>> languages = new(StringComparer.Ordinal);
        foreach ((Source file, StringDocument document) in read.OrderBy(static file => file.Source.Key, StringComparer.Ordinal))
        {
            if (!languages.TryGetValue(document.Language, out List<(Source, StringDocument)>? files))
            {
                languages.Add(document.Language, files = []);
            }

            files.Add((file, document));
        }

        string sourceLanguage = pass.Configuration.SourceLanguage;
        string fallback = pass.Configuration.FallbackLanguage ?? sourceLanguage;
        string found = string.Join(", ", languages.Keys);
        if (!languages.TryGetValue(sourceLanguage, out List<(Source, StringDocument)>? sourceFiles))
        {
            pass.Fail(pass.Requests.AssetRoot, $"has no string table in the source language \"{sourceLanguage}\". Set WithSourceLanguage to one of: {found}.");

            return null;
        }

        if (!languages.ContainsKey(fallback))
        {
            pass.Fail(
                pass.Requests.AssetRoot,
                $"has no string table in the fallback language \"{fallback}\". Add {fallback}{StringFile.Extension}, or set WithFallbackLanguage to one of: {found}.");

            return null;
        }

        Merged source = Merge(pass, sourceFiles);
        List<(string Path, Defined Defined, string[] Names)> keys = [];
        List<(string Path, string File, int First, int Count)> lists = [];
        foreach ((string path, Defined defined) in source.Entries)
        {
            if (defined.Text is { } text)
            {
                keys.Add((path, defined, StringText.Placeholders(text, path)));
                CheckPlurals(pass, sourceLanguage, path, defined);
            }
            else if (defined.Count > 0)
            {
                // A list's texts follow it in its document and take consecutive keys.
                lists.Add((path, defined.File, keys.Count, defined.Count));
            }
        }

        // The source first, then the rest in ordinal order, each laid out in the source's key order.
        string[] tags = [sourceLanguage, .. languages.Keys.Where(tag => tag != sourceLanguage)];
        string[][] texts = new string[tags.Length][];
        byte[] plurals = new byte[keys.Count];
        texts[0] = Laid(keys, translation: null, plurals);
        for (int i = 1; i < tags.Length; i++)
        {
            Merged translation = Merge(pass, languages[tags[i]]);
            Check(pass, tags[i], source, translation, keys, languages[tags[i]]);
            texts[i] = Laid(keys, translation, plurals);
        }

        if (pass.Failures > failures)
        {
            return null;
        }

        return StringsMembers.Render(
            pass,
            tags,
            Array.IndexOf(tags, fallback),
            texts,
            [.. keys.Select((key, i) => (key.Path, key.Defined.File, key.Defined.Text!, key.Names.Length, plurals[i]))],
            [.. lists]);
    }

    // Every key of one language's files, in key order. A text or list defined twice, or a key of one kind
    // in one file and another kind in another, fails. A group may span files.
    private static Merged Merge(PipelinePass pass, List<(Source Source, StringDocument Document)> files)
    {
        Merged merged = new();
        foreach ((Source file, StringDocument document) in files)
        {
            foreach (StringEntry entry in document.Entries)
            {
                if (!merged.Defined.TryGetValue(entry.Path, out Defined? other))
                {
                    Defined defined = new(file.Path, entry.Text, entry.Count);
                    merged.Defined.Add(entry.Path, defined);
                    merged.Entries.Add((entry.Path, defined));
                }
                else if (Kind(entry.Text, entry.Count) != other.Kind)
                {
                    pass.Fail(file.Path, $"{entry.Path}: is {Kind(entry.Text, entry.Count)} here and {other.Kind} in {other.File}. Make both one or the other.");
                }
                else if (entry.Text is not null || entry.Count > 0)
                {
                    pass.Fail(file.Path, $"{entry.Path}: is already defined in {other.File}. Keep it in one file.");
                }
            }
        }

        return merged;
    }

    // Holds a translation to the source's keys, kinds and placeholders, and reports each key it lacks.
    private static void Check(
        PipelinePass pass,
        string language,
        Merged source,
        Merged translation,
        List<(string Path, Defined Defined, string[] Names)> keys,
        List<(Source Source, StringDocument Document)> files)
    {
        foreach ((string path, Defined defined) in translation.Entries)
        {
            if (!source.Defined.TryGetValue(path, out Defined? authored))
            {
                pass.Fail(
                    defined.File,
                    source.Defined.TryGetValue(path[..Math.Max(path.LastIndexOf('.'), 0)], out Defined? list) && list.Count > 0
                        ? $"{path}: the source's list in {list.File} has {list.Count} texts. Remove this one, or add its text to the source."
                        : $"{path}: the source language has no such key. Rename it to match or remove it.");
            }
            else if (defined.Kind != authored.Kind)
            {
                pass.Fail(defined.File, $"{path}: is {defined.Kind} here and {authored.Kind} in {authored.File}. Make it {authored.Kind}, as the source does.");
            }
            else if (defined.Text is { } text)
            {
                string[] names = StringText.Placeholders(text, path);
                string[] expected = StringText.Placeholders(authored.Text!, path);
                if (!names.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
                {
                    pass.Fail(
                        defined.File,
                        $"{path}: names {StringText.Listed(names)}, and the source in {authored.File} names {StringText.Listed(expected)}. Name the source's placeholders, in any order.");
                }
                else
                {
                    CheckPlurals(pass, language, path, defined);
                }
            }
        }

        foreach ((string path, Defined authored, _) in keys)
        {
            if (translation.Defined.ContainsKey(path))
            {
                continue;
            }

            // The translation beside the source file that defines the key, or else its first.
            string anchor = files.FirstOrDefault(file => string.Equals(Path.GetDirectoryName(file.Source.Path), Path.GetDirectoryName(authored.File), StringComparison.Ordinal)).Source.Path
                ?? files[0].Source.Path;
            string missing = $"{path}: this language lacks the key {authored.File} defines. Add it.";
            if (pass.Requests.Shipping)
            {
                pass.Fail(anchor, missing + " A shipping build ships no text left untranslated.");
            }
            else
            {
                pass.Warn(anchor, missing + " The source text shows until then.");
            }
        }
    }

    // Holds each plural form in a text to the categories its language's CLDR rules select. A language with
    // no CLDR rules shows other alone.
    private static void CheckPlurals(PipelinePass pass, string language, string path, Defined defined)
    {
        PluralRules? rules = PluralRules.For(language);
        PluralCategory[] required = rules?.Required ?? [PluralCategory.Other];
        foreach (StringText.Plural plural in StringText.Plurals(defined.Text!, path))
        {
            PluralCategory[] authored = [.. plural.Branches.Select(static branch => branch.Category).OfType<PluralCategory>()];
            string on = $"{path}: the plural on {{{plural.Selector}}}";
            if (required.Except(authored).ToArray() is { Length: > 0 } missing)
            {
                pass.Fail(
                    defined.File,
                    $"{on} lacks {Categories(missing)}, which {language} selects for a count from 0 to {PluralRules.RequiredUpTo}. Add a branch for each.");
            }

            PluralCategory[] unused = [.. authored.Where(category => rules is null ? category != PluralCategory.Other : !rules.Produces(category))];
            if (unused.Length == 0)
            {
                continue;
            }

            if (rules is null)
            {
                pass.Fail(
                    defined.File,
                    $"{on} has {Categories(unused)}, and the CLDR has no plural rules for {language}, which shows other alone. Keep only other and exact =N branches.");
            }
            else
            {
                pass.Warn(defined.File, $"{on} has {Categories(unused)}, which {language} never selects for an integer. Remove it.");
            }
        }
    }

    private static string Categories(IEnumerable<PluralCategory> categories) =>
        string.Join(", ", categories.Select(static category => PluralRules.Names[(int)category]));

    // One language's texts as they ship, in key order, a key it lacks holding the source text. Each
    // argument a plural selects on is marked in plurals, by key.
    private static string[] Laid(List<(string Path, Defined Defined, string[] Names)> keys, Merged? translation, byte[] plurals)
    {
        string[] laid = new string[keys.Count];
        StringBuilder resolved = new();
        for (int i = 0; i < keys.Count; i++)
        {
            (string path, Defined authored, string[] names) = keys[i];
            string? own = translation is not null && translation.Defined.TryGetValue(path, out Defined? translated) ? translated.Text : null;
            bool lacks = translation is not null && own is null;
            string text = own ?? authored.Text!;

            // A slot the translation lacks holds the source text, marked to take the source language's plural rules.
            resolved.Clear();
            if (lacks)
            {
                resolved.Append((char)StringTable.SourceText);
            }

            StringText.Resolve(text, path, names, resolved);
            laid[i] = resolved.ToString();
            foreach (StringText.Plural plural in StringText.Plurals(text, path))
            {
                plurals[i] |= (byte)(1 << Array.IndexOf(names, plural.Selector));
            }
        }

        return laid;
    }

    private static string Kind(string? text, int count) => text is not null ? "a text" : count > 0 ? "a list" : "a group";

    // Where a key is defined, its text or null for a group or list, and a list's count.
    private sealed record Defined(string File, string? Text, int Count)
    {
        internal string Kind => StringsStep.Kind(Text, Count);
    }

    private sealed class Merged
    {
        internal Dictionary<string, Defined> Defined { get; } = new(StringComparer.Ordinal);

        internal List<(string Path, Defined Defined)> Entries { get; } = [];
    }
}
