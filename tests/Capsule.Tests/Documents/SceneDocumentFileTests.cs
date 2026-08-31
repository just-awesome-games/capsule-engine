using Capsule.Rendering;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Documents;

public sealed class SceneDocumentFileTests
{
    private const string Sha256 = "c304030d3d53c9c440cd5d251080080a16b34be3832ad1218b2b63cae622cf6d";

    private const string Coin = """
        ,
            {
              "id": 2,
              "type": "coin",
              "x": 8,
              "y": 0
            }
        """;

    private static readonly ColorRgba Slate = new(0x4A, 0x55, 0x68);

    [Fact]
    public void Parse_RejectsADocumentWithNoFormatVersion()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse("""{"entities": [], "nextEntityId": 1}"""));

        Assert.Contains("no formatVersion", error.Message, StringComparison.Ordinal);
        Assert.Contains("supports formatVersion 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsAnUnsupportedFormatVersion()
    {
        string json = DocumentText().Replace("\"formatVersion\": 1", "\"formatVersion\": 2", StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(() => SceneDocumentFile.Parse(json));

        Assert.Contains("formatVersion 2 is unsupported", error.Message, StringComparison.Ordinal);
        Assert.Contains("supports formatVersion 1", error.Message, StringComparison.Ordinal);
    }

    // An explicit null arrives as a null the property's own initializer never answers for, so an
    // omitted list passing says nothing about this one.
    [Theory]
    [InlineData("""{"formatVersion": 1, "nextEntityId": 1}""")]
    [InlineData("""{"formatVersion": 1, "entities": null, "nextEntityId": 1}""")]
    public void Parse_RejectsADocumentWithNoEntities(string json)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(json));

        Assert.Contains("the scene document has no entities", error.Message, StringComparison.Ordinal);
    }

    // Terrain is one entry among the rest, so a scene of entities alone is an ordinary document
    // and an empty list is an empty scene.
    [Fact]
    public void ADocumentWithNoTileMapEntry_ParsesAndRoundTripsByteForByte()
    {
        string json = """
            {
              "formatVersion": 1,
              "entities": [
                {
                  "id": 1,
                  "type": "coin",
                  "x": 8,
                  "y": 0
                }
              ],
              "nextEntityId": 2
            }

            """.ReplaceLineEndings("\n");

        SceneDocument document = SceneDocumentFile.Parse(json);

        Assert.Null(document.Grid);
        Assert.Equal(new EntityPlacement(1, "coin", 8f, 0f), Assert.Single(document.Entities.ToArray()));
        Assert.Equal(json, SceneDocumentFile.ToJson(document));
    }

    [Fact]
    public void ADocumentWithNoEntries_IsAnEmptyScene()
    {
        SceneDocument document = SceneDocumentFile.Parse("""{"formatVersion": 1, "entities": [], "nextEntityId": 1}""");

        Assert.Null(document.Grid);
        Assert.Empty(document.Entities.ToArray());
    }

    [Fact]
    public void Parse_RejectsATileMapEntryThatIsNotTheFirstEntry()
    {
        string json = """
            {
              "formatVersion": 1,
              "entities": [
                { "id": 1, "type": "coin", "x": 0, "y": 0 },
                { "id": 2, "type": "tile-map", "x": 0, "y": 0,
                  "properties": { "tileSize": 16, "width": 1, "height": 1,
                                  "tileTypes": [ { "type": "empty" } ], "tiles": [0] } }
              ],
              "nextEntityId": 3
            }
            """;

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(() => SceneDocumentFile.Parse(json));

        Assert.Contains("must be the document's first entry", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsASecondTileMapEntry()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(
                entities: """
                    ,
                        {
                          "id": 2,
                          "type": "tile-map",
                          "x": 0,
                          "y": 0,
                          "properties": { "tileSize": 16, "width": 1, "height": 1,
                                          "tileTypes": [ { "type": "empty" } ], "tiles": [0] }
                        }
                    """,
                nextEntityId: 3)));

        Assert.Contains("second 'tile-map' entry", error.Message, StringComparison.Ordinal);
    }

    // Properties are a contract per entry type, not a bag the reader sets by name, so a type with
    // no contract carrying them is a mistake rather than data the engine would silently drop.
    [Fact]
    public void Parse_RejectsPropertiesOnATypeThatDeclaresNoContract()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(
                entities: """
                    ,
                        {
                          "id": 2,
                          "type": "coin",
                          "x": 8,
                          "y": 0,
                          "properties": { "value": 5 }
                        }
                    """,
                nextEntityId: 3)));

        Assert.Contains("the type 'coin' has no properties contract", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsATileMapEntryWithNoProperties()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse("""
                {"formatVersion": 1, "entities": [{"id": 1, "type": "tile-map", "x": 0, "y": 0}], "nextEntityId": 2}
                """));

        Assert.Contains("declares no properties", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsATileMapEntryWhosePropertiesAreNull()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse("""
                {"formatVersion": 1, "entities": [{"id": 1, "type": "tile-map", "x": 0, "y": 0, "properties": null}], "nextEntityId": 2}
                """));

        Assert.Contains("declares no properties", error.Message, StringComparison.Ordinal);
    }

    // A tile map draws in world coordinates whatever its entity's position says, so a position
    // here would be a coordinate the engine writes back and then ignores.
    [Fact]
    public void Parse_RejectsAPositionedTileMapEntry()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse("""
                {"formatVersion": 1, "entities": [{"id": 1, "type": "tile-map", "x": 8, "y": 0,
                  "properties": {"tileSize": 16, "width": 1, "height": 1,
                                 "tileTypes": [{"type": "empty"}], "tiles": [0]}}], "nextEntityId": 2}
                """));

        Assert.Contains("anchored at the world origin", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_SurfacesAGridTheGridItselfRejects_AsAFormatFault()
    {
        string json = DocumentText().Replace("\"tileSize\": 16", "\"tileSize\": 0", StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(() => SceneDocumentFile.Parse(json));

        Assert.Contains("tileSize must be positive", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsAGridWithNoTileTypes()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(tileTypes: "null")));

        Assert.Contains("the 'tile-map' entry's grid has no tileTypes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsAGridWithNoTiles()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(tiles: "null")));

        Assert.Contains("the 'tile-map' entry's grid has no tiles", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsANullPaletteEntry()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(tileTypes: """[{"type": "empty"}, null]""")));

        Assert.Contains("tileTypes[1] is null", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsATileWithNoTileType()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(tiles: "[0, 7]")));

        Assert.Contains("tiles[1] is 7", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#00000000", 0x00, 0x00, 0x00, 0x00)]
    [InlineData("#1122ffee", 0x11, 0x22, 0xFF, 0xEE)]
    public void ColoursAreReadAndWrittenChannelByChannel(string color, int r, int g, int b, int a)
    {
        SceneDocument document = SceneDocumentFile.Parse(DocumentText(tileTypes: Palette(color)));

        Assert.Equal(new ColorRgba((byte)r, (byte)g, (byte)b, (byte)a), document.Grid!.TileTypes[1].Color);
        Assert.Contains($"\"color\": \"{color}\"", SceneDocumentFile.ToJson(document), StringComparison.Ordinal);
    }

    [Fact]
    public void PaletteEntryWithoutAColourRoundTripsCanonically()
    {
        SceneDocument document = new(
            new TileMapPlacement(1, new TileGrid(16, 1, 1, [TileGrid.EmptyTile, new TileDefinition("ground", null)], [1])),
            [],
            2);

        string json = SceneDocumentFile.ToJson(document);
        SceneDocument round = SceneDocumentFile.Parse(json);

        Assert.DoesNotContain("\"color\"", json, StringComparison.Ordinal);
        Assert.Equal(document.Grid!.TileTypes.ToArray(), round.Grid!.TileTypes.ToArray());
        Assert.Equal(json, SceneDocumentFile.ToJson(round));
    }

    [Theory]
    [InlineData("#4A5568FF")]
    [InlineData("#4a5568")]
    public void Parse_RejectsAColourThatIsNotTheCanonicalForm(string color)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(tileTypes: Palette(color))));

        Assert.Contains("lowercase #rrggbbaa", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsDuplicateEntityIds()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(entities: Coin + Coin, nextEntityId: 3)));

        Assert.Contains("appears more than once", error.Message, StringComparison.Ordinal);
    }

    // The terrain's id shares the one id space, so a placement cannot quietly reuse it.
    [Fact]
    public void Parse_RejectsAnEntityIdTheTileMapEntryAlreadyHolds()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(
                entities: """
                    ,
                        {
                          "id": 1,
                          "type": "coin",
                          "x": 8,
                          "y": 0
                        }
                    """)));

        Assert.Contains("entity id 1 appears more than once", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsAnEntityIdThatNextEntityIdWouldHandOutAgain()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(entities: Coin)));

        Assert.Contains("entity id 2 is not below nextEntityId 2", error.Message, StringComparison.Ordinal);
    }

    // The terrain entry draws its id from the same space as the placements. Without one it sits at
    // 0, which no other entry is ever checked against, so a later placement could alias it.
    [Fact]
    public void Parse_RejectsATileMapEntryWithNoId()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse("""
                {"formatVersion": 1, "entities": [{"type": "tile-map", "x": 0, "y": 0,
                  "properties": {"tileSize": 16, "width": 1, "height": 1,
                                 "tileTypes": [{"type": "empty"}], "tiles": [0]}}], "nextEntityId": 2}
                """));

        Assert.Contains("the 'tile-map' entry has no id", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsATileMapEntryIdThatNextEntityIdWouldHandOutAgain()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(nextEntityId: 1)));

        Assert.Contains("entity id 1 is not below nextEntityId 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsANullEntry()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(entities: ", null")));

        Assert.Contains("entities[1] is null", error.Message, StringComparison.Ordinal);
    }

    // An entry with no position would otherwise be placed at the origin, which is a position the
    // file never stated and the terrain entry is required to be at.
    [Theory]
    [InlineData("""{"id": 2, "type": "coin", "y": 0}""", "x")]
    [InlineData("""{"id": 2, "type": "coin", "x": 8}""", "y")]
    public void Parse_RejectsAnEntryWithNoPosition(string entry, string missing)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(entities: $",{entry}", nextEntityId: 3)));

        Assert.Contains($"entities[1] has no {missing}", error.Message, StringComparison.Ordinal);
    }

    // An untyped entry reaches the entity registry as "", which fails at boot naming nothing an
    // author could act on; the document is where the defect is, so it is refused there.
    [Fact]
    public void Parse_RejectsAnEntryWithNoType()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(
                entities: """
                    ,
                        {
                          "id": 2,
                          "x": 8,
                          "y": 0
                        }
                    """,
                nextEntityId: 3)));

        Assert.Contains("entity id 2 has no type", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_NamesTheFixWhenAnEntityHasNoId()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(
                entities: """
                    ,
                        {
                          "type": "coin",
                          "x": 128,
                          "y": 64
                        }
                    """)));

        Assert.Equal(
            "entity 'coin' at (128, 64) has no id — every entry takes one from nextEntityId when it is created.",
            error.Message);
    }

    [Fact]
    public void Parse_RejectsAFieldTheFormatDoesNotDefine()
    {
        Assert.Throws<SceneDocumentFormatException>(() => SceneDocumentFile.Parse(DocumentText(extra: ""","spawn": [0, 0]""")));
    }

    [Fact]
    public void Parse_RejectsAPaletteEntryFieldTheFormatDoesNotDefine()
    {
        Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(tileTypes: """[{"type": "empty"}, {"type": "ground", "sprite": "wall.png"}]""")));
    }

    [Fact]
    public void ToJson_WritesTheCanonicalForm()
    {
        SceneDocument document = new(
            new TileMapPlacement(1, new TileGrid(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("ground", Slate)], [0, 1])),
            [new EntityPlacement(2, "coin", 8f, 0f)],
            3);

        string expected = string.Join(
            '\n',
            "{",
            "  \"formatVersion\": 1,",
            "  \"entities\": [",
            "    {",
            "      \"id\": 1,",
            "      \"type\": \"tile-map\",",
            "      \"x\": 0,",
            "      \"y\": 0,",
            "      \"properties\": {",
            "        \"tileSize\": 16,",
            "        \"width\": 2,",
            "        \"height\": 1,",
            "        \"tileTypes\": [",
            "          {",
            "            \"type\": \"empty\"",
            "          },",
            "          {",
            "            \"type\": \"ground\",",
            "            \"color\": \"#4a5568ff\"",
            "          }",
            "        ],",
            "        \"tiles\": [",
            "          0,",
            "          1",
            "        ]",
            "      }",
            "    },",
            "    {",
            "      \"id\": 2,",
            "      \"type\": \"coin\",",
            "      \"x\": 8,",
            "      \"y\": 0",
            "    }",
            "  ],",
            "  \"nextEntityId\": 3",
            "}",
            string.Empty);

        Assert.Equal(expected, SceneDocumentFile.ToJson(document));
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(0f, float.PositiveInfinity)]
    public void Constructor_RejectsANonFiniteEntityPosition(float x, float y)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => new SceneDocument(Terrain(), [new EntityPlacement(2, "coin", x, y)], 3));

        Assert.Contains("not a position", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsATileMapPlacementAmongTheEntities()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => new SceneDocument(null, [new EntityPlacement(1, SceneDocument.TileMapType, 0f, 0f)], 2));

        Assert.Contains("must be the document's first entry", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "room.tmj", Sha256)]
    [InlineData("tiled", "", Sha256)]
    [InlineData("tiled", "room.tmj", "")]
    public void Constructor_RejectsAnIncompleteSourceBlock(string tool, string path, string hash)
    {
        Assert.Throws<SceneDocumentFormatException>(
            () => new SceneDocument(Terrain(), [], 2, new SceneDocumentSource(tool, path, hash)));
    }

    [Theory]
    [InlineData("scenes\\room.tmj")]
    [InlineData("/scenes/room.tmj")]
    [InlineData("C:/scenes/room.tmj")]
    public void Constructor_RejectsASourcePathThatIsNotRelativeAndPortable(string path)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => new SceneDocument(Terrain(), [], 2, new SceneDocumentSource("tiled", path, Sha256)));

        Assert.Contains("must be relative", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("C304030D3D53C9C440CD5D251080080A16B34BE3832AD1218B2B63CAE622CF6D")]
    public void Constructor_RejectsAHashThatIsNotALowercaseSha256(string hash)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => new SceneDocument(Terrain(), [], 2, new SceneDocumentSource("tiled", "room.tmj", hash)));

        Assert.Contains("64 lowercase hex", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJson_RoundTripsADocumentWithASourceBlock()
    {
        SceneDocument document = new(
            new TileMapPlacement(1, new TileGrid(8, 2, 1, [TileGrid.EmptyTile, new TileDefinition("ground", Slate)], [1, 0])),
            [new EntityPlacement(3, "player", 40.5f, 24f)],
            4,
            new SceneDocumentSource("tiled", "../scenes/room.tmj", Sha256));

        SceneDocument round = SceneDocumentFile.Parse(SceneDocumentFile.ToJson(document));

        Assert.Equal(SceneDocumentFile.ToJson(document), SceneDocumentFile.ToJson(round));
        Assert.Equal(document.Source, round.Source);
        Assert.Equal(document.TileMap?.Id, round.TileMap?.Id);
        Assert.Equal(document.Grid!.TileTypes.ToArray(), round.Grid!.TileTypes.ToArray());
    }

    private static TileMapPlacement Terrain() =>
        new(1, new TileGrid(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("ground", Slate)], [0, 1]));

    private static string Palette(string color) =>
        $$"""[{"type": "empty"}, {"type": "ground", "color": "{{color}}"}]""";

    private static string DocumentText(
        string? tileTypes = null,
        string tiles = "[0, 1]",
        string entities = "",
        int nextEntityId = 2,
        string extra = "") =>
        $$"""
        {
          "formatVersion": 1,
          "entities": [
            {
              "id": 1,
              "type": "tile-map",
              "x": 0,
              "y": 0,
              "properties": {
                "tileSize": 16,
                "width": 2,
                "height": 1,
                "tileTypes": {{tileTypes ?? Palette("#4a5568ff")}},
                "tiles": {{tiles}}
              }
            }{{entities}}
          ],
          "nextEntityId": {{nextEntityId}}{{extra}}
        }
        """;
}
