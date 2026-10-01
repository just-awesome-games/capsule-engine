using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Tiles;

namespace Capsule.Scenes.Documents;

// The file shape, mapped one to one onto the JSON. Declaration order is the written order, which the
// canonical writer depends on, so reordering a member here changes every scene document's bytes.
// Every member is nullable where the reader must tell an omitted field from a present one, so it can
// name the defect instead of reading a default.
[Description("A scene document: the settings a scene starts with and every entity and tile map it places.")]
internal sealed class SceneDocumentJson
{
    // A colour as the format spells it: "#rrggbb", or "#rrggbbaa" with an ff alpha, in either case.
    internal const string ColorPattern = "^#[0-9A-Fa-f]{6}([Ff]{2})?$";

    // Read and ignored. The writer never sets it, so no written or shipped document carries it.
    [JsonPropertyName(SchemaKeyConverter.Key)]
    [JsonConverter(typeof(SchemaKeyConverter))]
    public string? Schema { get; set; }

    [Description("The document format's version, which must be one this build supports.")]
    [Required]
    [AllowedValues(SceneDocumentFile.FormatVersion)]
    public int? FormatVersion { get; set; }

    [Description("The key of an abstract Scene subclass. The composed scene derives from it. Absent composes a plain Scene.")]
    public string? BaseScene { get; set; }

    [Description("The key of a concrete Camera subclass with a parameterless constructor, which the scene installs. Absent leaves the scene's default camera in place.")]
    public string? Camera { get; set; }

    [Description("The scene's extent as [w, h], both finite and greater than zero. Absent keeps the extent of the document's tile maps.")]
    [SchemaLength(2, 2)]
    [Range(0d, double.MaxValue, MinimumIsExclusive = true)]
    public float[]? Size { get; set; }

    [Description("The camera centre at which every layer sits as authored, as [x, y]. Absent leaves each camera its own.")]
    [SchemaLength(2, 2)]
    public float[]? ScrollCenter { get; set; }

    [Description("The colour the scene clears to, as \"#rrggbb\" or as \"#rrggbbaa\" with an ff alpha.")]
    [RegularExpression(ColorPattern)]
    public string? ClearColor { get; set; }

    [Description("The scene's ambient light colour, as \"#rrggbb\" or as \"#rrggbbaa\" with an ff alpha.")]
    [RegularExpression(ColorPattern)]
    public string? Ambient { get; set; }

    [Description("How the scene's textures are sampled. Absent keeps the game's setting.")]
    [AllowedValues(SceneDocumentFile.LinearSampling, SceneDocumentFile.PointSampling)]
    public string? Sampling { get; set; }

    // Raw JSON, read key by key into the composing class's authorable members when the scene is composed.
    [Description("The scene's properties. Each key sets the member the composing scene class marks [Authorable].")]
    public JsonElement? Properties { get; set; }

    // A null entry reaches the reader, which names it. An initializer here would invent data the format
    // never accepted.
    [Description("Every entry the scene places, in document order. Write an empty list for a scene with nothing in it.")]
    [Required]
    public SceneEntryJson?[]? Entities { get; set; }

    [Description("The next id to hand out, greater than every entry's id. Deleted ids are not reused.")]
    [Required]
    [Range(1, int.MaxValue)]
    public int NextEntityId { get; set; }

    [Description("What a derived document came from. Its presence marks a derived file, and an authoring source omits it.")]
    public SceneDocumentSourceJson? Source { get; set; }
}

[Description("One entry: a game entity, or the engine's tile map when its type is \"tile-map\".")]
internal sealed class SceneEntryJson
{
    [Description("The entry's id: positive, unique in the document and lower than nextEntityId.")]
    [Required]
    [Range(1, int.MaxValue)]
    public int? Id { get; set; }

    [Description("The spawn type: the key of the entity class the entry composes, or \"tile-map\" for terrain.")]
    [Required]
    [SchemaLength(1)]
    public string? Type { get; set; }

    [Description("The entry's x position. A tile-map entry is anchored at 0.")]
    [Required]
    public float? X { get; set; }

    [Description("The entry's y position. A tile-map entry is anchored at 0.")]
    [Required]
    public float? Y { get; set; }

    [Description("The turn in degrees, clockwise on screen. Absent is 0. A tile-map entry refuses it.")]
    [DefaultValue(0f)]
    public float? Rotation
    {
        get;
        set
        {
            field = value;
            HasRotation = true;
        }
    }

    [JsonIgnore]
    public bool HasRotation { get; private set; }

    [Description("The raw authored factor as [x, y], both finite and greater than zero. Absent is identity. A tile-map entry refuses it.")]
    [SchemaLength(2, 2)]
    [Range(0d, double.MaxValue, MinimumIsExclusive = true)]
    public float[]? Scale
    {
        get;
        set
        {
            field = value;
            HasScale = true;
        }
    }

    // Whether the document carried the field, since the deserializer calls the setter only for a field
    // that is present. The tile-map entry rejects a scale on presence, not on value.
    [JsonIgnore]
    public bool HasScale { get; private set; }

    // An authored 0 is an ordinary band and is written back, so it stays distinct from an absent field.
    [Description("The entry's draw band. On a tile-map entry it applies to the composed map.")]
    public int? ZIndex { get; set; }

    [Description("How far the entry moves with the camera, as [x, y], both finite. On a tile-map entry it applies to the composed map. A tile-map entry whose properties set collider refuses it.")]
    [SchemaLength(2, 2)]
    public float[]? ScrollFactor { get; set; }

    // Raw JSON, because each entry type defines its own properties contract. The reader deserializes the
    // tile map's against TileGridJson. A game entry's are read key by key into its class's authorable
    // members when it spawns.
    [Description("The entry's properties. A tile-map entry holds its grid and collider here. A game entity's keys set the members its class marks [Authorable].")]
    public JsonElement? Properties { get; set; }
}

[Description("A tile map's grid: its tile size, its extent in tiles, the texture it draws from, its palette, its tiles and whether it collides.")]
internal sealed class TileGridJson
{
    [Description("The side of one square tile in pixels.")]
    [Required]
    [Range(1, int.MaxValue)]
    public int TileSize { get; set; }

    [Description("The grid's width in tiles.")]
    [Required]
    [Range(1, int.MaxValue)]
    public int Width { get; set; }

    [Description("The grid's height in tiles.")]
    [Required]
    [Range(1, int.MaxValue)]
    public int Height { get; set; }

    [Description("The key of the texture every drawn tile is cut from, extension included, with forward slashes and no empty, \".\" or \"..\" segment. Absent on a grid that draws nothing.")]
    [SchemaLength(1)]
    public string? Texture { get; set; }

    [Description("How many cells wide the texture is. Required with texture, and absent without one.")]
    [Range(1, int.MaxValue)]
    public int? Columns { get; set; }

    [Description("The palette every tile indexes. Index 0 is \"empty\", carrying nothing but its name.")]
    [Required]
    public TileTypeJson?[]? TileTypes { get; set; }

    [Description("The width x height palette indices, row by row from the top-left tile.")]
    [Required]
    [Range(0, int.MaxValue)]
    public int[]? Tiles { get; set; }

    [Description("How each tile's drawing and collision shape is mirrored or turned, in the shape of tiles. 1 mirrors it left to right, 2 top to bottom and 4 swaps its axes before either, and the sum combines them. Absent is all 0.")]
    [Range(0, TileTransforms.Count - 1)]
    public int[]? Transforms { get; set; }

    [Description("True to give the map a collider, whose cells collide on their tile types' layers. The palette must then name a layer, and the entry may author no scrollFactor. Absent is false.")]
    [DefaultValue(false)]
    public bool? Collider { get; set; }
}

[Description("One palette entry: a named tile type, the class it composes, and how its tiles draw and collide.")]
internal sealed class TileTypeJson
{
    [Description("The tile type's name, unique within the palette.")]
    [Required]
    [SchemaLength(1)]
    public string? Name { get; set; }

    [Description("The key of the TileType subclass the entry composes. Absent composes a plain TileType.")]
    [SchemaLength(1)]
    public string? Type { get; set; }

    [Description("Which cell of the texture a tile of this type draws, counted across a row of columns then down from cell 0. Absent with no frames is a semantic tile: queryable, may collide, draws nothing.")]
    [Range(0, int.MaxValue)]
    public int? Cell { get; set; }

    [Description("The cells a tile of this type draws in turn, looping, in place of cell. Absent with no cell draws nothing.")]
    [SchemaLength(1)]
    public TileFrameJson?[]? Frames { get; set; }

    [Description("The collision layer every tile of this type is on, one name the game owns. Absent is decoration.")]
    [SchemaLength(1)]
    public string? Layer { get; set; }

    [Description("The convex polygon the tile collides as: three or four [x, y] points in pixels from the tile's top-left corner with Y down, each within [0, tileSize]. Absent is the whole tile.")]
    [SchemaLength(3, Shape2D.MaxPoints)]
    public float[]?[]? Shape { get; set; }

    [Description("True for a tile that blocks only a body coming down onto it from above.")]
    [DefaultValue(false)]
    public bool? OneWay { get; set; }

    [Description("True for a oneWay tile that also blocks from the sides and passes a body only from below.")]
    [DefaultValue(false)]
    public bool? SolidSides { get; set; }

    // Raw JSON, read key by key into the composing class's authorable members when the scene is composed.
    [Description("The entry's properties. Each key sets the member its TileType subclass marks [Authorable].")]
    public JsonElement? Properties { get; set; }
}

[Description("One frame of an animated tile: a cell and how long it is held.")]
internal sealed class TileFrameJson
{
    [Description("Which cell of the texture the frame draws, counted as a palette entry's cell is.")]
    [Required]
    [Range(0, int.MaxValue)]
    public int? Cell { get; set; }

    [Description("The fixed steps the frame is held for, at least one. Not milliseconds.")]
    [Required]
    [Range(1, int.MaxValue)]
    public int? Ticks { get; set; }
}

[Description("What a derived document came from: the tool, the source's path and the hash of its source closure.")]
internal sealed class SceneDocumentSourceJson
{
    [Description("The tool that derived the document.")]
    [Required]
    public string? Tool { get; set; }

    [Description("The source's relative path, with forward slashes.")]
    [Required]
    public string? Path { get; set; }

    [Description("The SHA-256 of the source closure, as 64 lowercase hex characters.")]
    [Required]
    [RegularExpression("^[0-9a-f]{64}$")]
    public string? Hash { get; set; }
}
