using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capsule.Scenes.Documents;

// The file shape, mapped one to one onto the JSON. JsonPropertyOrder fixes field order, which the
// canonical writer depends on, so reordering a member here changes every scene document's bytes.
// Unmapped members are rejected. A typo in a hand-authored document fails at load instead of in play.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SceneDocumentJson
{
    // Nullable, to tell an omitted version from an unsupported numeric one.
    [JsonPropertyName("formatVersion")]
    [JsonPropertyOrder(0)]
    public int? FormatVersion { get; set; }

    // Absent when the document composes a plain Scene, and WhenWritingNull keeps it out.
    [JsonPropertyName("baseScene")]
    [JsonPropertyOrder(1)]
    public string? BaseScene { get; set; }

    // Absent when the document installs no camera, and WhenWritingNull keeps it out.
    [JsonPropertyName("camera")]
    [JsonPropertyOrder(2)]
    public string? Camera { get; set; }

    // Absent when the document keeps the tile-map extent, and WhenWritingNull keeps it out. Nullable so the
    // reader reports a wrong component count.
    [JsonPropertyName("size")]
    [JsonPropertyOrder(3)]
    public float[]? Size { get; set; }

    // Absent when the document authors no scroll centre, and WhenWritingNull keeps it out. Nullable so the
    // reader reports a wrong component count.
    [JsonPropertyName("scrollCenter")]
    [JsonPropertyOrder(4)]
    public float[]? ScrollCenter { get; set; }

    // A colour is "#rrggbb" or "#rrggbbaa" and sampling is "linear" or "point". The reader parses all
    // three, and WhenWritingNull keeps an absent one out.
    [JsonPropertyName("clearColor")]
    [JsonPropertyOrder(5)]
    public string? ClearColor { get; set; }

    [JsonPropertyName("ambient")]
    [JsonPropertyOrder(6)]
    public string? Ambient { get; set; }

    [JsonPropertyName("sampling")]
    [JsonPropertyOrder(7)]
    public string? Sampling { get; set; }

    // Nullable, to tell an absent list from an empty scene and to let a null entry reach the reader. An
    // initializer here would invent data the format never accepted.
    [JsonPropertyName("entities")]
    [JsonPropertyOrder(8)]
    public SceneEntryJson?[]? Entities { get; set; }

    [JsonPropertyName("nextEntityId")]
    [JsonPropertyOrder(9)]
    public int NextEntityId { get; set; }

    [JsonPropertyName("source")]
    [JsonPropertyOrder(10)]
    public SceneDocumentSourceJson? Source { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SceneEntryJson
{
    // Nullable, to make an omitted id raise the format's missing-id error instead of reading as 0.
    [JsonPropertyName("id")]
    [JsonPropertyOrder(0)]
    public int? Id { get; set; }

    [JsonPropertyName("type")]
    [JsonPropertyOrder(1)]
    public string? Type { get; set; }

    // Nullable, to make an omitted coordinate raise the format's missing-position error instead of
    // reading as the origin, which is where the terrain entry must sit.
    [JsonPropertyName("x")]
    [JsonPropertyOrder(2)]
    public float? X { get; set; }

    [JsonPropertyName("y")]
    [JsonPropertyOrder(3)]
    public float? Y { get; set; }

    private float[]? _scale;

    // Absent on an entry at the authored size, and WhenWritingNull keeps it out. Nullable so the reader
    // reports a wrong component count.
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

    // Whether the document carried the field, since the deserializer calls the setter only for a field
    // that is present. The tile-map entry rejects a scale on presence, not on value.
    [JsonIgnore]
    public bool HasScale { get; private set; }

    // Absent when the entry authors no band, and WhenWritingNull keeps it out. An authored 0 is an ordinary
    // band and is written back, so it stays distinct from an absent field.
    [JsonPropertyName("zIndex")]
    [JsonPropertyOrder(5)]
    public int? ZIndex { get; set; }

    // Absent when the entry authors no factor, and WhenWritingNull keeps it out. Nullable so the reader
    // reports a wrong component count.
    [JsonPropertyName("scrollFactor")]
    [JsonPropertyOrder(6)]
    public float[]? ScrollFactor { get; set; }

    // Held as raw JSON, not a typed member, because each entry type defines its own properties
    // contract. The reader deserializes the tile-map's against TileGridJson and rejects properties on any
    // other type.
    [JsonPropertyName("properties")]
    [JsonPropertyOrder(7)]
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

    // The texture's path under Assets/, extension included and forward slashes only. Absent on a grid
    // that draws nothing. Columns is nullable, to make a texture with no columns raise the grid's error
    // instead of reading as 0.
    [JsonPropertyName("texture")]
    [JsonPropertyOrder(3)]
    public string? Texture { get; set; }

    [JsonPropertyName("columns")]
    [JsonPropertyOrder(4)]
    public int? Columns { get; set; }

    // Nullable for the same reason the entry list is, so the reader names an absent palette or map.
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

    // Absent for the reserved empty entry and for any tile type that draws nothing. WhenWritingNull keeps it
    // out of those entries' written form.
    [JsonPropertyName("cell")]
    [JsonPropertyOrder(1)]
    public int? Cell { get; set; }

    // Absent for every tile type that does not collide, which is the default.
    [JsonPropertyName("layer")]
    [JsonPropertyOrder(2)]
    public string? Layer { get; set; }

    // Absent for a tile type that collides as its whole tile, which is the default.
    [JsonPropertyName("shape")]
    [JsonPropertyOrder(3)]
    public float[]?[]? Shape { get; set; }

    // Absent for a tile type that blocks from every side, which is the default. The writer never emits false.
    [JsonPropertyName("oneWay")]
    [JsonPropertyOrder(4)]
    public bool? OneWay { get; set; }

    // Absent for a tile type that passes a mover from the sides, which is the default. The writer never
    // emits false.
    [JsonPropertyName("solidSides")]
    [JsonPropertyOrder(5)]
    public bool? SolidSides { get; set; }
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
