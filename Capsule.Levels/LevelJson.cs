using System.Text.Json.Serialization;

namespace Capsule.Levels;

// The file shape, one-to-one with the JSON. JsonPropertyOrder fixes field order, which the
// canonical writer depends on: a reordered member here changes every level file's bytes.
// Unmapped members are rejected so a typo in a hand-authored level fails at load, not in play.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class LevelJson
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

    [JsonPropertyName("tileTypes")]
    [JsonPropertyOrder(3)]
    public string[] TileTypes { get; set; } = [];

    [JsonPropertyName("tiles")]
    [JsonPropertyOrder(4)]
    public int[] Tiles { get; set; } = [];

    [JsonPropertyName("entities")]
    [JsonPropertyOrder(5)]
    public LevelEntityJson[] Entities { get; set; } = [];

    [JsonPropertyName("nextEntityId")]
    [JsonPropertyOrder(6)]
    public int NextEntityId { get; set; }

    [JsonPropertyName("source")]
    [JsonPropertyOrder(7)]
    public LevelSourceJson? Source { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class LevelEntityJson
{
    // Nullable so an omitted id fails as the format's missing-id error rather than reading as 0.
    [JsonPropertyName("id")]
    [JsonPropertyOrder(0)]
    public int? Id { get; set; }

    [JsonPropertyName("type")]
    [JsonPropertyOrder(1)]
    public string? Type { get; set; }

    [JsonPropertyName("x")]
    [JsonPropertyOrder(2)]
    public float X { get; set; }

    [JsonPropertyName("y")]
    [JsonPropertyOrder(3)]
    public float Y { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class LevelSourceJson
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
