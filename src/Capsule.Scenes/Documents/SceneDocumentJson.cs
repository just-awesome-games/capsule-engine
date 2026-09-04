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

    private float[]? _scale;

    // Absent on an entry at the authored size; WhenWritingNull keeps it out. Nullable so a wrong
    // arity is the reader's error to name.
    [JsonPropertyName("scale")]
    [JsonPropertyOrder(4)]
    public float[]? Scale
    {
        get => _scale;
        set
        {
            _scale = value;
            HasScale = true;
        }
    }

    // Whether the field was written at all: the deserializer calls the setter only for a field the
    // document carries. The tile-map entry is refused a scale on presence, not value.
    [JsonIgnore]
    public bool HasScale { get; private set; }

    // Raw JSON, not a member of this shape: properties are a contract per entry type. The reader
    // deserializes the tile-map's against TileGridJson and rejects properties on any other type.
    [JsonPropertyName("properties")]
    [JsonPropertyOrder(5)]
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

    // One asset's path under assets/textures, extension included, forward slashes only. Absent on
    // a grid that draws nothing; columns is nullable so a texture with no columns fails as the
    // grid's error rather than reading as 0.
    [JsonPropertyName("texture")]
    [JsonPropertyOrder(3)]
    public string? Texture { get; set; }

    [JsonPropertyName("columns")]
    [JsonPropertyOrder(4)]
    public int? Columns { get; set; }

    // Nullable for the entry list's reason: an absent palette or map is the format's fault to name.
    [JsonPropertyName("tileTypes")]
    [JsonPropertyOrder(5)]
    public TileTypeJson?[]? TileTypes { get; set; }

    [JsonPropertyName("tiles")]
    [JsonPropertyOrder(6)]
    public int[]? Tiles { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class TileTypeJson
{
    [JsonPropertyName("type")]
    [JsonPropertyOrder(0)]
    public string? Type { get; set; }

    // Absent for the reserved empty entry and for any tile type that draws nothing;
    // WhenWritingNull keeps it out of those entries' written form.
    [JsonPropertyName("cell")]
    [JsonPropertyOrder(1)]
    public int? Cell { get; set; }

    // Absent for every tile type that collides as nothing, which is the default.
    [JsonPropertyName("layer")]
    [JsonPropertyOrder(2)]
    public string? Layer { get; set; }

    // Absent for a tile type that collides on every side, which is the default.
    [JsonPropertyName("collidableFaces")]
    [JsonPropertyOrder(3)]
    public string?[]? CollidableFaces { get; set; }

    // Mapped only so the reader can name what replaced it, as a raw element so presence rather
    // than shape is the question. An absent field leaves ValueKind Undefined.
    [JsonPropertyName("collision")]
    [JsonPropertyOrder(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Collision { get; set; }
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
