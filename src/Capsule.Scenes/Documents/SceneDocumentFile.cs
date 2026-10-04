using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Capsule.Scenes.Documents;

/// <summary>Reads and writes the scene document format.</summary>
public static class SceneDocumentFile
{
    /// <summary>Reads scene document JSON from a string.</summary>
    /// <exception cref="SceneDocumentFormatException">The JSON is malformed or the document breaks the format.</exception>
    public static SceneDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            return Translate(Deserialize(json));
        }
        catch (ArgumentException ex)
        {
            // The document model reports a defect as a bad argument. Coming from a file, the same
            // defect is a malformed document, so translate it here.
            throw new SceneDocumentFormatException(ex.Message, ex);
        }
    }

    /// <summary>Serializes <paramref name="document"/> to compact JSON, which <see cref="Parse"/> reads back.</summary>
    /// <remarks>An importer writes the document it builds with this. The same document always writes the same text.</remarks>
    public static string ToJson(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        ReadOnlySpan<SceneDocumentEntry> placed = document.Entries;
        SceneEntryJson[] entries = new SceneEntryJson[placed.Length];
        for (int i = 0; i < placed.Length; i++)
        {
            SceneDocumentEntry entry = placed[i];

            // An absent position or rotation is 0 and an absent scale is identity, so each is skipped at its default.
            entries[i] = new SceneEntryJson
            {
                Id = entry.Id,
                Type = entry.Type,
                X = entry.X == 0f ? null : entry.X,
                Y = entry.Y == 0f ? null : entry.Y,
                Rotation = entry.RotationDegrees == 0f ? null : entry.RotationDegrees,
                Scale = entry.ScaleX == 1f && entry.ScaleY == 1f ? null : [entry.ScaleX, entry.ScaleY],
                ZIndex = entry.ZIndex,
                ScrollFactor = entry.ScrollFactor is { } factor ? [factor.X, factor.Y] : null,
                Members = Members(entry.Properties),
            };
        }

        SceneDocumentJson file = new()
        {
            BaseScene = document.BaseScene,
            Entities = entries,
            Members = Members(document.Properties),
        };

        // The scene's own members sit ahead of the entries.
        JsonObject root = JsonSerializer.SerializeToNode(file, SceneDocumentJsonContext.Default.SceneDocumentJson)!.AsObject();
        JsonNode? listed = root["entities"];
        root.Remove("entities");
        root.Add("entities", listed);

        return root.ToJsonString();
    }

    // Turns parsed JSON into the document model. Checks the format's shape. The document model
    // checks its own invariants.
    private static SceneDocument Translate(SceneDocumentJson file)
    {
        if (file.Entities is not { } entries)
        {
            throw new SceneDocumentFormatException(
                "the scene document has no entities. Write an empty list for a scene with nothing in it.");
        }

        SceneDocumentEntry[] documentEntries = new SceneDocumentEntry[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] is not { } entry)
            {
                throw new SceneDocumentFormatException($"entities[{i}] is null. Write an object with a type.");
            }

            Vector2 scale = Pair(entry.Scale, i, "scale") ?? Vector2.One;
            documentEntries[i] = new SceneDocumentEntry(
                entry.Type ?? string.Empty,
                entry.X ?? 0f,
                entry.Y ?? 0f,
                scale.X,
                scale.Y,
                entry.ZIndex,
                Pair(entry.ScrollFactor, i, "scrollFactor"),
                entry.Rotation ?? 0f,
                Element(entry.Members))
            {
                Id = entry.Id,
            };
        }

        return new SceneDocument(documentEntries, Element(file.Members), file.BaseScene);
    }

    // Reads a two-component pair, returning null when the field is absent. Checks the component
    // count only. The document model validates the values.
    private static Vector2? Pair(float[]? pair, int index, string field)
    {
        if (pair is null)
        {
            return null;
        }

        if (pair.Length != 2)
        {
            throw new SceneDocumentFormatException(
                $"entities[{index}] has a {field} of {pair.Length} components. Write it as [x, y], or omit the field.");
        }

        return new Vector2(pair[0], pair[1]);
    }

    // An object's undeclared keys as the element an applier reads, or null when it has none.
    private static JsonElement? Element(Dictionary<string, JsonElement>? members) =>
        members is { Count: > 0 } ? JsonSerializer.SerializeToElement(members, SceneDocumentJsonContext.Default.DictionaryStringJsonElement) : null;

    // The members an applier reads as an object's undeclared keys, in authored order, or null when there are none.
    private static Dictionary<string, JsonElement>? Members(JsonElement? properties) =>
        properties is { ValueKind: JsonValueKind.Object } members
            ? members.EnumerateObject().ToDictionary(static member => member.Name, static member => member.Value, StringComparer.Ordinal)
            : null;

    private static SceneDocumentJson Deserialize(string json)
    {
        SceneDocumentJson? document;
        try
        {
            document = JsonSerializer.Deserialize(json, SceneDocumentJsonContext.Default.SceneDocumentJson);
        }
        catch (JsonException ex)
        {
            throw new SceneDocumentFormatException($"malformed scene document JSON: {ex.Message}", ex);
        }

        return document ?? throw new SceneDocumentFormatException("the scene document file is empty.");
    }
}
