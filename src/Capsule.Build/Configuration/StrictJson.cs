using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Capsule.Assets;

namespace Capsule.Build.Configuration;

/// <summary>Reads the build's own authored formats, the config, atlas and sheet files, as exactly what their classes declare.</summary>
internal static class StrictJson
{
    /// <summary>Reads the file at <paramref name="path"/> as one <typeparamref name="T"/>.</summary>
    /// <param name="members">What a refusal calls the format's members and values, as <c>kind, setting or value</c>.</param>
    /// <param name="nullFix">
    /// What a refusal of a present null tells the author to write instead, or null when the format reads
    /// a null as the member left out.
    /// </param>
    /// <param name="shape">What the file holds and every member it may set, which a refusal of its shape ends in.</param>
    /// <exception cref="FormatException">
    /// The JSON is malformed, holds a refused null, or has a member or value the format does not declare.
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
            throw new FormatException($"is not valid JSON at line {ex.LineNumber + 1}, byte {ex.BytePositionInLine + 1}.", ex);
        }

        using (document)
        {
            if (nullFix is not null && NullAt(document.RootElement, "$") is { } at)
            {
                throw new FormatException($"has a null at {at}. {nullFix} {shape}");
            }

            // Read from the bytes, not the document, so a refusal carries the line and byte it is at.
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
                throw new FormatException($"has an unknown {members} at {ex.Path} (line {ex.LineNumber + 1}, byte {ex.BytePositionInLine + 1}). {shape}", ex);
            }
        }
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
