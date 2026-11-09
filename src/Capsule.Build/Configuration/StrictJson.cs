using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Capsule.Assets;

namespace Capsule.Build.Configuration;

/// <summary>Reads the build's own authored formats, the config, atlas and sheet files, as exactly what their classes declare.</summary>
internal static class StrictJson
{
    /// <summary>Reads the file at <paramref name="path"/> as one <typeparamref name="T"/>.</summary>
    /// <param name="members">What a refusal calls the format's members, as <c>kind or setting</c>.</param>
    /// <param name="nullFix">
    /// What a refusal of a present null tells the author to write instead, or null when the format reads
    /// a null as the member left out.
    /// </param>
    /// <param name="shape">What the file holds and every member it may set, which a refusal of its shape ends in.</param>
    /// <exception cref="FormatException">
    /// The JSON is malformed, holds a refused null, has a member the format does not declare, or has a value
    /// its member does not take.
    /// </exception>
    internal static T Read<T>(string path, JsonTypeInfo<T> type, string members, string? nullFix, string shape)
    {
        byte[] json = File.ReadAllBytes(path);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw Malformed(ex);
        }

        using (document)
        {
            if (nullFix is not null && NullAt(document.RootElement, "$") is { } at)
            {
                throw new FormatException($"has a null at {at}. {nullFix} {shape}");
            }

            // Read from the bytes, not the document. A refusal carries the line and byte it is at.
            try
            {
                return JsonSerializer.Deserialize(json, type) ?? throw new FormatException($"is not a JSON object. {shape}");
            }
            catch (JsonException ex) when (ex.Path == "$." + SchemaKeyConverter.Key)
            {
                throw new FormatException(ex.Message, ex);
            }
            catch (JsonException ex)
            {
                string where = $"{ex.Path} (line {ex.LineNumber + 1}, byte {ex.BytePositionInLine + 1})";
                throw new FormatException(IsUndeclared(type, ex.Path) ? $"has an unknown {members} at {where}. {shape}" : $"has an invalid value at {where}. {shape}", ex);
            }
        }
    }

    /// <summary>The refusal of JSON the reader could not get through, at the line and byte <paramref name="ex"/> stopped at.</summary>
    internal static FormatException Malformed(JsonException ex) =>
        new($"is not valid JSON at line {ex.LineNumber + 1}, byte {ex.BytePositionInLine + 1}. Correct the JSON there.", ex);

    // Whether the last member on path is one its object's type does not declare. A path through a member
    // the type declares fails at that member's value instead.
    private static bool IsUndeclared(JsonTypeInfo root, string? path)
    {
        if (path is null || !path.StartsWith('$'))
        {
            return false;
        }

        JsonTypeInfo? info = root;
        int at = 1;
        while (at < path.Length && info is not null)
        {
            string? name;
            if (path[at] == '.')
            {
                int end = path.IndexOfAny(['.', '['], at + 1);
                end = end < 0 ? path.Length : end;
                name = path[(at + 1)..end];
                at = end;
            }
            else if (path.AsSpan(at).StartsWith("['", StringComparison.Ordinal))
            {
                int end = path.IndexOf("']", at, StringComparison.Ordinal);
                if (end < 0)
                {
                    return false;
                }

                name = path[(at + 2)..end];
                at = end + 2;
            }
            else
            {
                int end = path.IndexOf(']', at);
                if (end < 0)
                {
                    return false;
                }

                name = null;
                at = end + 1;
            }

            if (name is null || info.Kind == JsonTypeInfoKind.Dictionary)
            {
                info = info.ElementType is { } element ? info.Options.GetTypeInfo(element) : null;
            }
            else if (info.Kind == JsonTypeInfoKind.Object)
            {
                JsonPropertyInfo? property = info.Properties.FirstOrDefault(declared => declared.Name == name);
                if (property is null)
                {
                    return at == path.Length;
                }

                info = info.Options.GetTypeInfo(property.PropertyType);
            }
            else
            {
                return false;
            }
        }

        return false;
    }

    // The path of the first null in element, or null when it holds none.
    private static string? NullAt(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return path;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                // A root "$schema" of any value is its converter's to refuse. A nested one is an unknown
                // member whatever its value, which deserializing reports.
                if (property.NameEquals(SchemaKeyConverter.Key))
                {
                    continue;
                }

                if (NullAt(property.Value, $"{path}.{property.Name}") is { } found)
                {
                    return found;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (NullAt(item, $"{path}[{index++}]") is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }
}
