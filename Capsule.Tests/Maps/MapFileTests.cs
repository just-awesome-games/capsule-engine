using Capsule.Maps;
using Capsule.Rendering;

namespace Capsule.Tests.Maps;

/// <summary>
/// The format's contract: what a map file may say, and the exact bytes one is written back as.
/// The canonical form is load-bearing — the golden fixture byte-compares against it.
/// </summary>
public sealed class MapFileTests
{
    // Real-shaped: the format takes 64 lowercase hex characters and nothing else, so a
    // placeholder here would fail every spec below on its hash rather than on what it pins.
    private const string Sha256 = "c304030d3d53c9c440cd5d251080080a16b34be3832ad1218b2b63cae622cf6d";

    private static readonly ColorRgba Slate = new(0x4A, 0x55, 0x68);

    [Fact]
    public void Parse_RejectsAMapWithNoFormatVersion()
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => MapFile.Parse("""{"grid": {}, "objects": [], "nextObjectId": 1}"""));

        Assert.Contains("no formatVersion", error.Message, StringComparison.Ordinal);
        Assert.Contains("supports formatVersion 1", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Parse_RejectsAnUnsupportedFormatVersion(int formatVersion)
    {
        string json = MapText().Replace("\"formatVersion\": 1", $"\"formatVersion\": {formatVersion}", StringComparison.Ordinal);

        MapFormatException error = Assert.Throws<MapFormatException>(() => MapFile.Parse(json));

        Assert.Contains($"formatVersion {formatVersion} is unsupported", error.Message, StringComparison.Ordinal);
        Assert.Contains("supports formatVersion 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsAMapWithNoGrid()
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => MapFile.Parse("""{"formatVersion": 1, "objects": [], "nextObjectId": 1}"""));

        Assert.Contains("no grid", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsATilesArrayThatDoesNotFillTheGrid()
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => MapFile.Parse(MapText(tiles: "[0]")));

        Assert.Contains("requires 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsATileWithNoTileType()
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => MapFile.Parse(MapText(tiles: "[0, 7]")));

        Assert.Contains("tiles[1] is 7", error.Message, StringComparison.Ordinal);
    }

    // Every channel differs, so a reader that shuffled them would still parse and still be
    // wrong; the ends of the range pin the two saturating cases.
    [Theory]
    [InlineData("#00000000", 0x00, 0x00, 0x00, 0x00)]
    [InlineData("#ffffffff", 0xFF, 0xFF, 0xFF, 0xFF)]
    [InlineData("#1122ffee", 0x11, 0x22, 0xFF, 0xEE)]
    public void ColoursAreReadAndWrittenChannelByChannel(string color, int r, int g, int b, int a)
    {
        Map map = MapFile.Parse(MapText(tileTypes: Palette(color)));

        Assert.Equal(new ColorRgba((byte)r, (byte)g, (byte)b, (byte)a), map.Grid.TileTypes[1].Color);
        Assert.Contains($"\"color\": \"{color}\"", MapFile.ToJson(map), StringComparison.Ordinal);
    }

    [Fact]
    public void PaletteEntryWithoutAColourRoundTripsCanonically()
    {
        Map map = new(
            new TileGrid(16, 1, 1, [TileGrid.EmptyTile, new TileDefinition("ground", null)], [1]),
            [],
            1);

        string json = MapFile.ToJson(map);
        Map round = MapFile.Parse(json);

        Assert.DoesNotContain("\"color\"", json, StringComparison.Ordinal);
        Assert.Equal(map.Grid.TileTypes.ToArray(), round.Grid.TileTypes.ToArray());
        Assert.Equal(json, MapFile.ToJson(round));
    }

    // The written form is lowercase #rrggbbaa and only that: anything else read back would be
    // written out differently, and the map would not survive its own round trip.
    [Theory]
    [InlineData("#4A5568FF")]
    [InlineData("#4a5568")]
    [InlineData("4a5568ff")]
    [InlineData("#4a5568fff")]
    [InlineData("")]
    public void Parse_RejectsAColourThatIsNotTheCanonicalForm(string color)
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => MapFile.Parse(MapText(tileTypes: Palette(color))));

        Assert.Contains("lowercase #rrggbbaa", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsDuplicateObjectIds()
    {
        string objects = """[{"id": 1, "type": "coin", "x": 0, "y": 0}, {"id": 1, "type": "coin", "x": 8, "y": 0}]""";

        MapFormatException error = Assert.Throws<MapFormatException>(
            () => MapFile.Parse(MapText(objects: objects, nextObjectId: 2)));

        Assert.Contains("appears more than once", error.Message, StringComparison.Ordinal);
    }

    // nextObjectId is the promise that ids are never reused; an id at or above it means the
    // counter was rewound, and the next id minted would collide with one already placed.
    [Fact]
    public void Parse_RejectsAnObjectIdThatNextObjectIdWouldHandOutAgain()
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => MapFile.Parse(MapText(objects: """[{"id": 4, "type": "coin", "x": 0, "y": 0}]""", nextObjectId: 4)));

        Assert.Contains("is not below nextObjectId 4", error.Message, StringComparison.Ordinal);
    }

    // The message is the whole point of this error: an object written without an id is a map
    // author's mistake, and nothing in the file says where ids come from.
    [Fact]
    public void Parse_NamesTheFixWhenAnObjectHasNoId()
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => MapFile.Parse(MapText(objects: """[{"type": "coin", "x": 128, "y": 64}]""")));

        Assert.Equal(
            "object 'coin' at (128, 64) has no id — every object takes one from nextObjectId when it is created.",
            error.Message);
    }

    // Strict by choice: a typo'd field in a map written from code or a test would otherwise be
    // silently dropped and only surface as wrong behaviour in play.
    [Fact]
    public void Parse_RejectsAFieldTheFormatDoesNotDefine()
    {
        Assert.Throws<MapFormatException>(() => MapFile.Parse(MapText(extra: ""","spawn": [0, 0]""")));
    }

    [Fact]
    public void Parse_RejectsAPaletteEntryFieldTheFormatDoesNotDefine()
    {
        Assert.Throws<MapFormatException>(
            () => MapFile.Parse(MapText(tileTypes: """[{"type": "empty"}, {"type": "ground", "sprite": "wall.png"}]""")));
    }

    // Pins the canonical form whole: field order, the nested grid, the palette entry shape and
    // its omitted colour on the reserved entry, indent, LF, the trailing newline, and that an
    // absent source block is omitted rather than written as null.
    [Fact]
    public void ToJson_WritesTheCanonicalForm()
    {
        Map map = new(
            new TileGrid(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("ground", Slate)], [0, 1]),
            [new MapObject(1, "coin", 8f, 0f)],
            2);

        string expected = string.Join(
            '\n',
            "{",
            "  \"formatVersion\": 1,",
            "  \"grid\": {",
            "    \"tileSize\": 16,",
            "    \"width\": 2,",
            "    \"height\": 1,",
            "    \"tileTypes\": [",
            "      {",
            "        \"type\": \"empty\"",
            "      },",
            "      {",
            "        \"type\": \"ground\",",
            "        \"color\": \"#4a5568ff\"",
            "      }",
            "    ],",
            "    \"tiles\": [",
            "      0,",
            "      1",
            "    ]",
            "  },",
            "  \"objects\": [",
            "    {",
            "      \"id\": 1,",
            "      \"type\": \"coin\",",
            "      \"x\": 8,",
            "      \"y\": 0",
            "    }",
            "  ],",
            "  \"nextObjectId\": 2",
            "}",
            string.Empty);

        Assert.Equal(expected, MapFile.ToJson(map));
    }

    // Neither has a JSON number, so either would construct a map that cannot be written out.
    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(0f, float.PositiveInfinity)]
    public void Constructor_RejectsANonFiniteObjectPosition(float x, float y)
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => new Map(Grid(), [new MapObject(1, "coin", x, y)], 2));

        Assert.Contains("not a position", error.Message, StringComparison.Ordinal);
    }

    // A half-filled block writes a source object that Parse rejects: the map would fail its own
    // round trip.
    [Theory]
    [InlineData("", "room.tmj", Sha256)]
    [InlineData("tiled", "", Sha256)]
    [InlineData("tiled", "room.tmj", "")]
    public void Constructor_RejectsAnIncompleteSourceBlock(string tool, string path, string hash)
    {
        Assert.Throws<MapFormatException>(() => new Map(Grid(), [], 1, new MapSource(tool, path, hash)));
    }

    [Fact]
    public void Constructor_RejectsADefaultSourceBlock()
    {
        Assert.Throws<MapFormatException>(() => new Map(Grid(), [], 1, default(MapSource)));
    }

    // The source block only means anything if it resolves on someone else's machine: an
    // absolute or backslashed path records provenance nobody else can follow.
    [Theory]
    [InlineData("..\\maps\\room.tmj")]
    [InlineData("maps\\room.tmj")]
    [InlineData("/maps/room.tmj")]
    [InlineData("C:/maps/room.tmj")]
    public void Constructor_RejectsASourcePathThatIsNotRelativeAndPortable(string path)
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => new Map(Grid(), [], 1, new MapSource("tiled", path, Sha256)));

        Assert.Contains("must be relative", error.Message, StringComparison.Ordinal);
    }

    // A hash no importer could have produced can only ever mismatch, so the gate would report
    // a stale map where the real defect is a hand-written source block.
    [Theory]
    [InlineData("abc123")]
    [InlineData("C304030D3D53C9C440CD5D251080080A16B34BE3832AD1218B2B63CAE622CF6D")]
    [InlineData("g304030d3d53c9c440cd5d251080080a16b34be3832ad1218b2b63cae622cf6d")]
    public void Constructor_RejectsAHashThatIsNotALowercaseSha256(string hash)
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => new Map(Grid(), [], 1, new MapSource("tiled", "room.tmj", hash)));

        Assert.Contains("64 lowercase hex", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJson_RoundTripsAMapWithASourceBlock()
    {
        Map map = new(
            new TileGrid(8, 2, 1, [TileGrid.EmptyTile, new TileDefinition("ground", Slate)], [1, 0]),
            [new MapObject(3, "player", 40.5f, 24f)],
            4,
            new MapSource("tiled", "../maps/room.tmj", Sha256));

        Map round = MapFile.Parse(MapFile.ToJson(map));

        Assert.Equal(MapFile.ToJson(map), MapFile.ToJson(round));
        Assert.Equal(map.Source, round.Source);
        Assert.Equal(map.Grid.TileTypes.ToArray(), round.Grid.TileTypes.ToArray());
    }

    private static TileGrid Grid() =>
        new(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("ground", Slate)], [0, 1]);

    private static string Palette(string color) =>
        $$"""[{"type": "empty"}, {"type": "ground", "color": "{{color}}"}]""";

    private static string MapText(
        string? tileTypes = null,
        string tiles = "[0, 1]",
        string objects = "[]",
        int nextObjectId = 1,
        string extra = "") =>
        $$"""
        {
          "formatVersion": 1,
          "grid": {
            "tileSize": 16,
            "width": 2,
            "height": 1,
            "tileTypes": {{tileTypes ?? Palette("#4a5568ff")}},
            "tiles": {{tiles}}
          },
          "objects": {{objects}},
          "nextObjectId": {{nextObjectId}}{{extra}}
        }
        """;
}
