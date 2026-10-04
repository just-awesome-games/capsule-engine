using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Capsule.Assets;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes.Documents;

// The file shape, mapped one to one onto the JSON. Declaration order is the written order, so reordering a
// member here changes every written document's bytes.
// Every member is nullable where the reader must tell an omitted field from a present one, so it can
// name the defect instead of reading a default. Each object's undeclared keys are its class's
// [Authorable] members, which the generated applier reads and refuses when no member takes one.
[Description("A scene document: every entity a scene places. Any other key sets the member the composing scene class marks [Authorable], the engine's own Scene members among them.")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
internal sealed class SceneDocumentJson
{
    // Read and ignored. The writer never sets it, so no written or shipped document carries it.
    [JsonPropertyName(SceneDocumentKeys.Document.Schema)]
    [JsonConverter(typeof(SchemaKeyConverter))]
    public string? Schema { get; set; }

    [JsonPropertyName(SceneDocumentKeys.Document.BaseScene)]
    [Description("The key of an abstract Scene subclass. The composed scene derives from it. Absent composes a plain Scene.")]
    public string? BaseScene { get; set; }

    // A null entry reaches the reader, which names it. An initializer here would invent data the format
    // never accepted.
    [JsonPropertyName(SceneDocumentKeys.Document.Entities)]
    [Description("Every entry the scene places, in document order. Write an empty list for a scene with nothing in it.")]
    [Required]
    public SceneEntryJson?[]? Entities { get; set; }

    // The writer moves these ahead of entities.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Members { get; set; }

    // Throws SceneDocumentFormatException when the JSON is malformed or empty.
    internal static SceneDocumentJson Read(string json)
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

    internal static string Write(SceneDocument document)
    {
        ReadOnlySpan<SceneDocumentEntry> placed = document.Entries;
        SceneEntryJson[] entries = new SceneEntryJson[placed.Length];
        for (int i = 0; i < placed.Length; i++)
        {
            SceneDocumentEntry entry = placed[i];
            EntitySpawn spawn = entry.Spawn;

            // An absent position or rotation is 0 and an absent scale is identity, so each is skipped at its default.
            entries[i] = new SceneEntryJson
            {
                Id = entry.Id,
                Type = entry.Type,
                X = spawn.Position.X == 0f ? null : spawn.Position.X,
                Y = spawn.Position.Y == 0f ? null : spawn.Position.Y,
                Rotation = spawn.Rotation == 0f ? null : Degrees(spawn.Rotation),
                Scale = spawn.Scale == Vector2.One ? null : [spawn.Scale.X, spawn.Scale.Y],
                ZIndex = spawn.ZIndex,
                ScrollFactor = spawn.ScrollFactor is { } factor ? [factor.X, factor.Y] : null,
                Members = ExtensionData(entry.Members),
            };
        }

        SceneDocumentJson file = new()
        {
            BaseScene = document.BaseScene,
            Entities = entries,
            Members = ExtensionData(document.Members),
        };

        // The scene's own members sit ahead of the entries.
        JsonObject root = JsonSerializer.SerializeToNode(file, SceneDocumentJsonContext.Default.SceneDocumentJson)!.AsObject();
        JsonNode? listed = root[SceneDocumentKeys.Document.Entities];
        root.Remove(SceneDocumentKeys.Document.Entities);
        root.Add(SceneDocumentKeys.Document.Entities, listed);

        return root.ToJsonString();
    }

    // Checks the format's shape. The document model checks its own invariants.
    internal SceneDocument ToDocument()
    {
        if (Entities is not { } entries)
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

            EntitySpawn spawn = new(new Vector2(entry.X ?? 0f, entry.Y ?? 0f))
            {
                Rotation = float.DegreesToRadians(entry.Rotation ?? 0f),
                Scale = Pair(entry.Scale, i, SceneDocumentKeys.Entry.Scale) ?? Vector2.One,
                ZIndex = entry.ZIndex,
                ScrollFactor = Pair(entry.ScrollFactor, i, SceneDocumentKeys.Entry.ScrollFactor),
            };
            documentEntries[i] = new SceneDocumentEntry(entry.Type ?? string.Empty, spawn, Element(entry.Members)) { Id = entry.Id };
        }

        return new SceneDocument(documentEntries, Element(Members), BaseScene);
    }

    // The shortest degrees that read back as exactly these radians. A turn read from degrees writes the
    // degrees it was read from, and a written document reads back equal.
    private static float Degrees(float radians)
    {
        float degrees = float.RadiansToDegrees(radians);
        for (int digits = 1; digits < 9; digits++)
        {
            float shorter = float.Parse(degrees.ToString("G" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (float.DegreesToRadians(shorter) == radians)
            {
                return shorter;
            }
        }

        return degrees;
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
    private static Dictionary<string, JsonElement>? ExtensionData(JsonElement? members) =>
        members is { ValueKind: JsonValueKind.Object } authored
            ? authored.EnumerateObject().ToDictionary(static member => member.Name, static member => member.Value, StringComparer.Ordinal)
            : null;
}

[Description("One entry: the entity it places. Any other key sets the member the entity's class marks [Authorable].")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
internal sealed class SceneEntryJson
{
    [JsonPropertyName(SceneDocumentKeys.Entry.Id)]
    [Description("The entry's id, which an entity reference names: positive and unique in the document. Absent on an entry nothing references.")]
    [Range(1, int.MaxValue)]
    public int? Id { get; set; }

    [JsonPropertyName(SceneDocumentKeys.Entry.Type)]
    [Description("The type key of the entity class the entry places, \"tile-map\" for the engine's tile map.")]
    [Required]
    [SchemaLength(1)]
    public string? Type { get; set; }

    [JsonPropertyName(SceneDocumentKeys.Entry.X)]
    [Description("The entry's x position. Absent is 0.")]
    [DefaultValue(0f)]
    public float? X { get; set; }

    [JsonPropertyName(SceneDocumentKeys.Entry.Y)]
    [Description("The entry's y position. Absent is 0.")]
    [DefaultValue(0f)]
    public float? Y { get; set; }

    [JsonPropertyName(SceneDocumentKeys.Entry.Rotation)]
    [Description("The turn in degrees, clockwise on screen. Absent is 0.")]
    [DefaultValue(0f)]
    public float? Rotation { get; set; }

    [JsonPropertyName(SceneDocumentKeys.Entry.Scale)]
    [Description("The raw authored factor as [x, y], both finite and greater than zero. Absent is identity.")]
    [SchemaLength(2, 2)]
    [Range(0d, double.MaxValue, MinimumIsExclusive = true)]
    public float[]? Scale { get; set; }

    // An authored 0 is an ordinary band and is written back, so it stays distinct from an absent field.
    [JsonPropertyName(SceneDocumentKeys.Entry.ZIndex)]
    [Description("The entry's draw band. Absent keeps the band the entity's class sets.")]
    public int? ZIndex { get; set; }

    [JsonPropertyName(SceneDocumentKeys.Entry.ScrollFactor)]
    [Description("How far the entry moves with the camera, as [x, y], both finite. Absent keeps the factor the entity's class sets.")]
    [SchemaLength(2, 2)]
    public float[]? ScrollFactor { get; set; }

    // Read key by key into the entity class's authorable members when it spawns.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Members { get; set; }
}
