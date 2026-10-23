using System.Globalization;
using System.Text.Json;
using Capsule.Assets;

namespace Capsule.Build.Strings;

/// <summary>Reads the <c>&lt;tag&gt;.strings.json</c> format, a nested JSON object whose leaves are texts or arrays of texts.</summary>
/// <remarks>
/// The keys come from the JSON nesting alone. Moving a file never renames one. An array's texts are keyed by
/// position, as <c>speech.0</c>. A root <c>$schema</c> is ignored. The language is the file name before
/// <c>.strings.json</c>.
/// </remarks>
internal static class StringFile
{
    internal const string Extension = ".strings.json";

    /// <summary>Reads and validates the string table at <paramref name="path"/>.</summary>
    /// <exception cref="FormatException">The file name is no canonical language tag, or the JSON breaks the format.</exception>
    internal static StringDocument Read(string path)
    {
        string language = Language(path);
        byte[] json = File.ReadAllBytes(path);
        List<StringEntry> entries = [];
        try
        {
            Utf8JsonReader reader = new(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                throw new FormatException("is not a JSON object. A string table maps each key to a text in quotes, an object of more keys, or an array of texts.");
            }

            ReadGroup(ref reader, string.Empty, entries, new HashSet<string>(StringComparer.Ordinal));
        }
        catch (JsonException ex)
        {
            throw new FormatException($"is not valid JSON at line {ex.LineNumber + 1}, byte {ex.BytePositionInLine + 1}. Correct the JSON there.", ex);
        }

        return new StringDocument(language, [.. entries]);
    }

    /// <summary>The language a string table's file name carries.</summary>
    /// <exception cref="FormatException">The name is no language tag, or not spelled as .NET spells it.</exception>
    internal static string Language(string path)
    {
        string name = Path.GetFileName(path);
        string tag = name[..^Extension.Length];
        string canonical = CanonicalTag(tag);

        if (canonical.Length == 0)
        {
            throw new FormatException(
                $"is named for \"{tag}\", which is no language tag. Name a string table for its language's BCP 47 tag, as de{Extension} or pt-BR{Extension}.");
        }

        return string.Equals(tag, canonical, StringComparison.Ordinal)
            ? tag
            : throw new FormatException($"is named for \"{tag}\", which BCP 47 spells \"{canonical}\". Rename it {canonical}{Extension}.");
    }

    /// <summary>How .NET spells <paramref name="tag"/> as a predefined culture, or empty when it knows no such tag.</summary>
    internal static string CanonicalTag(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag, predefinedOnly: true).Name;
        }
        catch (CultureNotFoundException)
        {
            return string.Empty;
        }
    }

    // Reads every key of the object the reader has just entered, through its closing brace.
    private static void ReadGroup(ref Utf8JsonReader reader, string prefix, List<StringEntry> entries, HashSet<string> defined)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            if (prefix.Length == 0 && name == SchemaKeyConverter.Key)
            {
                reader.Read();
                reader.Skip();
                continue;
            }

            string path = prefix + name;
            if (name.Length == 0 || name.Contains('.', StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"{path}: has a key \"{name}\", which holds a '.' or nothing. Key paths join names with '.'. Nest an object for a group, and give every key a name.");
            }

            if (!defined.Add(path))
            {
                throw new FormatException($"{path}: is defined twice in this file. Keep one.");
            }

            reader.Read();
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    string text = reader.GetString()!;
                    _ = StringText.Placeholders(text, path);
                    entries.Add(new StringEntry(path, text));
                    break;

                case JsonTokenType.StartObject:
                    entries.Add(new StringEntry(path, Text: null));
                    ReadGroup(ref reader, path + ".", entries, defined);
                    break;

                case JsonTokenType.StartArray:
                    ReadList(ref reader, path, entries);
                    break;

                default:
                    throw new FormatException($"{path}: is {Kind(reader.TokenType)}. Write a text in quotes, an object of more keys, or an array of texts.");
            }
        }
    }

    // Reads the texts of the array the reader has just entered, through its closing bracket, each keyed by its position.
    private static void ReadList(ref Utf8JsonReader reader, string path, List<StringEntry> entries)
    {
        int list = entries.Count;
        entries.Add(new StringEntry(path, Text: null));
        int count = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            string element = $"{path}.{count}";
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new FormatException($"{element}: is {Kind(reader.TokenType)}. A list holds texts in quotes alone.");
            }

            string text = reader.GetString()!;
            _ = StringText.Placeholders(text, element);
            entries.Add(new StringEntry(element, text));
            count++;
        }

        if (count == 0)
        {
            throw new FormatException($"{path}: is an empty list. Give it a text, or remove it.");
        }

        entries[list] = entries[list] with { Count = count };
    }

    private static string Kind(JsonTokenType token) => token switch
    {
        JsonTokenType.Number => "a number",
        JsonTokenType.True or JsonTokenType.False => "a boolean",
        JsonTokenType.Null => "null",
        JsonTokenType.StartObject => "an object",
        _ => "an array",
    };
}
