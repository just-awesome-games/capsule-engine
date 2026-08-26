using System.Text.Json.Serialization;

namespace Capsule.Maps;

// The file shape, one-to-one with the JSON. JsonPropertyOrder fixes field order, which the
// canonical writer depends on: a reordered member here changes every map file's bytes.
// Unmapped members are rejected so a typo in a hand-authored map fails at load, not in play.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class MapJson
{
    // Nullable so an omitted grid fails as the format's missing-grid error rather than reading
    // as a zero-sized one.
    [JsonPropertyName("grid")]
    [JsonPropertyOrder(0)]
    public TileGridJson? Grid { get; set; }

    [JsonPropertyName("objects")]
    [JsonPropertyOrder(1)]
    public MapObjectJson[] Objects { get; set; } = [];

    [JsonPropertyName("nextObjectId")]
    [JsonPropertyOrder(2)]
    public int NextObjectId { get; set; }

    [JsonPropertyName("source")]
    [JsonPropertyOrder(3)]
    public MapSourceJson? Source { get; set; }
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

    [JsonPropertyName("tileTypes")]
    [JsonPropertyOrder(3)]
    public TileTypeJson[] TileTypes { get; set; } = [];

    [JsonPropertyName("tiles")]
    [JsonPropertyOrder(4)]
    public int[] Tiles { get; set; } = [];
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
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class MapObjectJson
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
internal sealed class MapSourceJson
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
