using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capsule.Scenes.Documents;

// The file shape, one-to-one with the JSON. JsonPropertyOrder fixes field order, which the
// canonical writer depends on: a reordered member here changes every scene document's bytes.
// Unmapped members are rejected so a typo in a hand-authored document fails at load, not in play.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SceneDocumentJson
{
    // Nullable so an omitted version is distinct from an unsupported numeric version.
    [JsonPropertyName("formatVersion")]
    [JsonPropertyOrder(0)]
    public int? FormatVersion { get; set; }

    // Nullable so an absent list is distinct from an empty scene, and so is a null where an entry
    // belongs: an initializer here would answer for JSON the format has not accepted.
    [JsonPropertyName("entities")]
    [JsonPropertyOrder(1)]
    public SceneEntryJson?[]? Entities { get; set; }

    [JsonPropertyName("nextEntityId")]
    [JsonPropertyOrder(2)]
    public int NextEntityId { get; set; }

    [JsonPropertyName("source")]
    [JsonPropertyOrder(3)]
    public SceneDocumentSourceJson? Source { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SceneEntryJson
{
    // Nullable so an omitted id fails as the format's missing-id error rather than reading as 0.
    [JsonPropertyName("id")]
    [JsonPropertyOrder(0)]
    public int? Id { get; set; }

    [JsonPropertyName("type")]
    [JsonPropertyOrder(1)]
    public string? Type { get; set; }

    // Nullable so an omitted coordinate fails as the format's missing-position error rather than
    // reading as the origin, which is a position the terrain entry is required to be at.
    [JsonPropertyName("x")]
    [JsonPropertyOrder(2)]
    public float? X { get; set; }

    [JsonPropertyName("y")]
    [JsonPropertyOrder(3)]
    public float? Y { get; set; }

    // Held as raw JSON, not as a member of this shape: properties are a contract per entry type,
    // and exactly one type declares one today. The reader deserializes the tile-map's against
    // TileGridJson and rejects properties on any other type, so nothing here is set by name.
    // WhenWritingNull keeps the member out of an entry that carries none.
    [JsonPropertyName("properties")]
    [JsonPropertyOrder(4)]
    public JsonElement? Properties { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class TileGridJson
{
    [JsonPropertyName("tileSize")]
    [JsonPropertyOrder(0)]
    public int TileSize { get; set; }

    [JsonPropertyName("width")]
    [JsonPropertyOrder(1)]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    [JsonPropertyOrder(2)]
    public int Height { get; set; }

    // Nullable for the entry list's reason: an absent palette or map is the format's fault to
    // name, and a null where a palette entry belongs is not an entry.
    [JsonPropertyName("tileTypes")]
    [JsonPropertyOrder(3)]
    public TileTypeJson?[]? TileTypes { get; set; }

    [JsonPropertyName("tiles")]
    [JsonPropertyOrder(4)]
    public int[]? Tiles { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class TileTypeJson
{
    [JsonPropertyName("type")]
    [JsonPropertyOrder(0)]
    public string? Type { get; set; }

    // Absent for the reserved empty entry alone; WhenWritingNull keeps it out of that entry's
    // written form.
    [JsonPropertyName("color")]
    [JsonPropertyOrder(1)]
    public string? Color { get; set; }

    // Absent for every tile type that collides as nothing, which is the default.
    [JsonPropertyName("collision")]
    [JsonPropertyOrder(2)]
    public string? Collision { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SceneDocumentSourceJson
{
    [JsonPropertyName("tool")]
    [JsonPropertyOrder(0)]
    public string? Tool { get; set; }

    [JsonPropertyName("path")]
    [JsonPropertyOrder(1)]
    public string? Path { get; set; }

    [JsonPropertyName("hash")]
    [JsonPropertyOrder(2)]
    public string? Hash { get; set; }
}
