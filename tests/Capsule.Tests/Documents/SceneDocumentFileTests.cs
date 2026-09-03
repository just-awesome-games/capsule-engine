using Capsule.Assets;
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

    private static readonly TextureHandle Atlas = new("terrain", ".png");

    [Theory]
    [InlineData("""{"entities": [], "nextEntityId": 1}""", "no formatVersion")]
    [InlineData("""{"formatVersion": 2, "entities": [], "nextEntityId": 1}""", "formatVersion 2 is unsupported")]
    public void Parse_RejectsADocumentWithBadFormatVersion(string json, string expectedMessage)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(json));

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
        Assert.Contains("supports formatVersion 4", error.Message, StringComparison.Ordinal);
    }

    // An explicit null arrives as a null the property's own initializer never answers for, so an
    // omitted list passing says nothing about this one.
    [Theory]
    [InlineData("""{"formatVersion": 4, "nextEntityId": 1}""")]
    [InlineData("""{"formatVersion": 4, "entities": null, "nextEntityId": 1}""")]
    public void Parse_RejectsADocumentWithNoEntities(string json)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(json));

        Assert.Contains("the scene document has no entities", error.Message, StringComparison.Ordinal);
    }

    // A tile map is one entry among the rest, so a scene of entities alone is an ordinary document
    // and an empty list is an empty scene.
    [Fact]
    public void ADocumentWithNoTileMapEntry_ParsesAndRoundTripsByteForByte()
    {
        string json = """
            {
              "formatVersion": 4,
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

        Assert.Equal(new EntityPlacement(1, "coin", 8f, 0f), Assert.Single(document.Entries.ToArray()));
        Assert.Equal(json, SceneDocumentFile.ToJson(document));
    }

    [Fact]
    public void ADocumentWithNoEntries_IsAnEmptyScene()
    {
        SceneDocument document = SceneDocumentFile.Parse("""{"formatVersion": 4, "entities": [], "nextEntityId": 1}""");

        Assert.Empty(document.Entries.ToArray());
    }

    [Fact]
    public void Parse_AllowsATileMapAfterAnEntity()
    {
        string json = """
            {
              "formatVersion": 4,
              "entities": [
                { "id": 1, "type": "coin", "x": 0, "y": 0 },
                { "id": 2, "type": "tile-map", "x": 0, "y": 0,
                  "properties": { "tileSize": 16, "width": 1, "height": 1,
                                  "tileTypes": [ { "type": "empty" } ], "tiles": [0] } }
              ],
              "nextEntityId": 3
            }
            """;

        SceneDocument document = SceneDocumentFile.Parse(json);

        Assert.NotNull(document.Entries[0].Entity);
        Assert.NotNull(document.Entries[1].TileMap);
    }

    [Fact]
    public void Parse_AllowsMoreThanOneTileMapEntry()
    {
        SceneDocument document = SceneDocumentFile.Parse(DocumentText(
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
                nextEntityId: 3));

        Assert.NotNull(document.Entries[0].TileMap);
        Assert.NotNull(document.Entries[1].TileMap);
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

    [Theory]
    [InlineData("""{"formatVersion": 4, "entities": [{"id": 1, "type": "tile-map", "x": 0, "y": 0}], "nextEntityId": 2}""")]
    [InlineData("""{"formatVersion": 4, "entities": [{"id": 1, "type": "tile-map", "x": 0, "y": 0, "properties": null}], "nextEntityId": 2}""")]
    public void Parse_RejectsATileMapEntryWithMissingOrNullProperties(string json)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(json));

        Assert.Contains("declares no properties", error.Message, StringComparison.Ordinal);
    }

    // A tile map draws in world coordinates whatever its entity's position says, so a position
    // here would be a coordinate the engine writes back and then ignores.
    [Fact]
    public void Parse_RejectsAPositionedTileMapEntry()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse("""
                {"formatVersion": 4, "entities": [{"id": 1, "type": "tile-map", "x": 8, "y": 0,
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
    [InlineData(0)]
    [InlineData(9)]
    public void ATilesCellIsReadAndWrittenAsItStands(int cell)
    {
        SceneDocument document = SceneDocumentFile.Parse(DocumentText(tileTypes: Palette(cell)));

        Assert.Equal(cell, TileMapOf(document).Grid.TileTypes[1].Cell);
        Assert.Contains($"\"cell\": {cell}", SceneDocumentFile.ToJson(document), StringComparison.Ordinal);
    }

    // The texture is written as the whole file name, so the document names exactly what ships.
    [Fact]
    public void AGridsTextureAndColumnsSurviveTheirOwnRoundTrip()
    {
        SceneDocument document = SceneDocumentFile.Parse(DocumentText());
        string json = SceneDocumentFile.ToJson(document);

        Assert.Equal(new TextureHandle("terrain", ".png"), TileMapOf(document).Grid.Texture);
        Assert.Equal(4, TileMapOf(document).Grid.Columns);
        Assert.Contains("\"texture\": \"terrain.png\",", json, StringComparison.Ordinal);
        Assert.Contains("\"columns\": 4,", json, StringComparison.Ordinal);
        Assert.Equal(json, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(json)));
    }

    // Which extensions ship is the build's allow-list to decide, so any of them writes.
    [Fact]
    public void ToJson_WritesWhateverExtensionTheHandleCarries()
    {
        Assert.Contains(
            "\"texture\": \"terrain.bmp\"",
            SceneDocumentFile.ToJson(Drawing(new TextureHandle("terrain", ".bmp"))),
            StringComparison.Ordinal);
    }

    // The reader splits a written name on its last dot, so a handle has a written form only when
    // that split hands it back unchanged. Each of these would come back as some other handle.
    [Theory]
    [InlineData("terrain.png", "")]
    [InlineData("a", ".b.png")]
    [InlineData("", ".png")]
    [InlineData("a", "png")]
    public void ToJson_RefusesATextureHandleTheWrittenNameWouldNotSplitBackInto(string name, string extension)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.ToJson(Drawing(new TextureHandle(name, extension))));

        Assert.Contains("does not split back out of one file name", error.Message, StringComparison.Ordinal);
    }

    // A struct's default has null parts, which is a handle with no written form, not a crash.
    [Fact]
    public void ToJson_RefusesTheDefaultTextureHandle()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.ToJson(Drawing(default)));

        Assert.Contains("does not split back out of one file name", error.Message, StringComparison.Ordinal);
    }

    // Dots inside the name are not the separator: only the last one is.
    [Fact]
    public void ATextureWhoseNameCarriesDots_RoundTripsWhole()
    {
        string json = SceneDocumentFile.ToJson(Drawing(new TextureHandle("x.atlas", ".png")));

        Assert.Contains("\"texture\": \"x.atlas.png\"", json, StringComparison.Ordinal);
        Assert.Equal(
            new TextureHandle("x.atlas", ".png"),
            TileMapOf(SceneDocumentFile.Parse(json)).Grid.Texture);
        Assert.Equal(json, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(json)));
    }

    private static SceneDocument Drawing(TextureHandle texture) =>
        new([new TileMapPlacement(1, new TileGrid(16, 2, 1, [TileGrid.EmptyTile, Ground(0)], [0, 1], texture, 4))], 2);

    [Fact]
    public void AGridThatDrawsNothingWritesNeitherTextureNorCell()
    {
        SceneDocument document = new(
            [new TileMapPlacement(1, new TileGrid(16, 1, 1, [TileGrid.EmptyTile, new TileDefinition("hazard", null)], [1]))],
            2);

        string json = SceneDocumentFile.ToJson(document);
        SceneDocument round = SceneDocumentFile.Parse(json);

        Assert.DoesNotContain("\"texture\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"columns\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"cell\"", json, StringComparison.Ordinal);
        Assert.Equal(TileMapOf(document).Grid.TileTypes.ToArray(), TileMapOf(round).Grid.TileTypes.ToArray());
        Assert.Equal(json, SceneDocumentFile.ToJson(round));
    }

    // Asked of the field's presence: a 0 read as an absent columns would parse and then be written
    // back without the field, so the document would not survive its own round trip.
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Parse_RejectsColumnsOnAGridWithNoTexture(int columns)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse($$$"""
                {"formatVersion": 4, "entities": [{"id": 1, "type": "tile-map", "x": 0, "y": 0,
                  "properties": {"tileSize": 16, "width": 1, "height": 1, "columns": {{{columns}}},
                                 "tileTypes": [{"type": "empty"}], "tiles": [0]}}], "nextEntityId": 2}
                """));

        Assert.Contains("declares columns but no texture", error.Message, StringComparison.Ordinal);
    }

    // The name is one file in a flat directory: a stem, an extension, and no way out of it. The
    // two spellings differ only for the separator JSON itself escapes.
    [Theory]
    [InlineData("tiles", "tiles")]
    [InlineData("tiles.", "tiles.")]
    [InlineData(".png", ".png")]
    [InlineData("a/tiles.png", "a/tiles.png")]
    [InlineData("a\\\\tiles.png", "a\\tiles.png")]
    [InlineData(" ", " ")]
    public void Parse_RejectsATextureThatIsNotOneFileName(string authored, string texture)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(texture: $"\"{authored}\"")));

        Assert.Contains($"grid has texture \"{texture}\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("extension included", error.Message, StringComparison.Ordinal);
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
                {"formatVersion": 4, "entities": [{"type": "tile-map", "x": 0, "y": 0,
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
            [
                new TileMapPlacement(1, new TileGrid(16, 2, 1, [TileGrid.EmptyTile, Ground(0)], [0, 1], Atlas, 4)),
                new EntityPlacement(2, "coin", 8f, 0f),
            ],
            3);

        string expected = string.Join(
            '\n',
            "{",
            "  \"formatVersion\": 4,",
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
            "        \"texture\": \"terrain.png\",",
            "        \"columns\": 4,",
            "        \"tileTypes\": [",
            "          {",
            "            \"type\": \"empty\"",
            "          },",
            "          {",
            "            \"type\": \"ground\",",
            "            \"cell\": 0",
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
            () => new SceneDocument([Terrain(), new EntityPlacement(2, "coin", x, y)], 3));

        Assert.Contains("not a position", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsAnEntityPlacementClaimingTheReservedTileMapType()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => new SceneDocument([new EntityPlacement(1, SceneDocument.TileMapType, 0f, 0f)], 2));

        Assert.Contains("reserved", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "room.map", Sha256)]
    [InlineData("editor", "", Sha256)]
    [InlineData("editor", "room.map", "")]
    public void Constructor_RejectsAnIncompleteSourceBlock(string tool, string path, string hash)
    {
        Assert.Throws<SceneDocumentFormatException>(
            () => new SceneDocument([Terrain()], 2, new SceneDocumentSource(tool, path, hash)));
    }

    [Theory]
    [InlineData("scenes\\room.map")]
    [InlineData("/scenes/room.map")]
    [InlineData("C:/scenes/room.map")]
    public void Constructor_RejectsASourcePathThatIsNotRelativeAndPortable(string path)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => new SceneDocument([Terrain()], 2, new SceneDocumentSource("editor", path, Sha256)));

        Assert.Contains("must be relative", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("C304030D3D53C9C440CD5D251080080A16B34BE3832AD1218B2B63CAE622CF6D")]
    public void Constructor_RejectsAHashThatIsNotALowercaseSha256(string hash)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => new SceneDocument([Terrain()], 2, new SceneDocumentSource("editor", "room.map", hash)));

        Assert.Contains("64 lowercase hex", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJson_RoundTripsADocumentWithASourceBlock()
    {
        SceneDocument document = new(
            [
                new TileMapPlacement(1, new TileGrid(8, 2, 1, [TileGrid.EmptyTile, Ground(0)], [1, 0], Atlas, 4)),
                new EntityPlacement(3, "player", 40.5f, 24f),
            ],
            4,
            new SceneDocumentSource("editor", "../scenes/room.map", Sha256));

        SceneDocument round = SceneDocumentFile.Parse(SceneDocumentFile.ToJson(document));

        Assert.Equal(SceneDocumentFile.ToJson(document), SceneDocumentFile.ToJson(round));
        Assert.Equal(document.Source, round.Source);
        Assert.Equal(document.Entries[0].Id, round.Entries[0].Id);
        Assert.Equal(document.Entries[1], round.Entries[1]);
        Assert.Equal(TileMapOf(document).Grid.TileTypes.ToArray(), TileMapOf(round).Grid.TileTypes.ToArray());
    }

    // Identity is what an absent scale means, so the canonical form carries the field only where
    // it says something — and a document that writes it must read it back the same way.
    [Fact]
    public void AScaledEntry_RoundTripsAndAnUnscaledOneWritesNoScale()
    {
        SceneDocument document = new(
            [new EntityPlacement(1, "banner", 8f, 0f, 2f, 3f), new EntityPlacement(2, "coin", 16f, 0f)],
            3);

        string json = SceneDocumentFile.ToJson(document);
        SceneDocument round = SceneDocumentFile.Parse(json);

        Assert.Contains("\"scale\": [\n        2,\n        3\n      ]", json, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(json, "\"scale\""));
        Assert.Equal(document.Entries[0].Entity, round.Entries[0].Entity);
        Assert.Equal(1f, round.Entries[1].Entity!.Value.ScaleX);
        Assert.Equal(1f, round.Entries[1].Entity!.Value.ScaleY);
        Assert.Equal(json, SceneDocumentFile.ToJson(round));
    }

    [Theory]
    [InlineData("[2]", "scale of 1 components")]
    [InlineData("[1, 2, 3]", "scale of 3 components")]
    [InlineData("[0, 1]", "which is not a scale")]
    [InlineData("[1, -2]", "which is not a scale")]
    public void Parse_RejectsAScaleThatIsNotTwoPositiveFactors(string scale, string expected)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(
                entities: $$"""
                    ,
                        {
                          "id": 2,
                          "type": "coin",
                          "x": 8,
                          "y": 0,
                          "scale": {{scale}}
                        }
                    """,
                nextEntityId: 3)));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    // Terrain is drawn in world coordinates and sized by its grid's tileSize, so a scale here
    // would be a factor the engine writes back and then ignores.
    [Fact]
    public void Parse_RejectsAScaleOnTheTileMapEntry()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(extra: "", scale: "\"scale\": [2, 2],")));

        Assert.Contains("anchored and unscaled", error.Message, StringComparison.Ordinal);
    }

    // Asked of the field's presence rather than its value: a null scale reads as the absent one, so
    // on value alone the entry would parse and then be written back without the field it declared.
    [Fact]
    public void Parse_RejectsANullScaleOnTheTileMapEntry()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(extra: "", scale: "\"scale\": null,")));

        Assert.Contains("anchored and unscaled", error.Message, StringComparison.Ordinal);
    }

    private static int CountOf(string json, string needle)
    {
        int count = 0;
        for (int at = json.IndexOf(needle, StringComparison.Ordinal); at >= 0;
            at = json.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static TileMapPlacement Terrain() =>
        new(1, new TileGrid(16, 2, 1, [TileGrid.EmptyTile, Ground(0)], [0, 1], Atlas, 4));

    private static TileMapPlacement TileMapOf(SceneDocument document, int index = 0) =>
        document.Entries[index].TileMap!.Value;

    private static TileDefinition Ground(int cell) => new("ground", cell);

    private static string Palette(int cell) =>
        $$"""[{"type": "empty"}, {"type": "ground", "cell": {{cell}}}]""";

    private static string DocumentText(
        string? tileTypes = null,
        string tiles = "[0, 1]",
        string entities = "",
        int nextEntityId = 2,
        string extra = "",
        string texture = "\"terrain.png\"",
        string scale = "") =>
        $$"""
        {
          "formatVersion": 4,
          "entities": [
            {
              "id": 1,
              "type": "tile-map",
              "x": 0,
              "y": 0,
              {{scale}}
              "properties": {
                "tileSize": 16,
                "width": 2,
                "height": 1,
                "texture": {{texture}},
                "columns": 4,
                "tileTypes": {{tileTypes ?? Palette(0)}},
                "tiles": {{tiles}}
              }
            }{{entities}}
          ],
          "nextEntityId": {{nextEntityId}}{{extra}}
        }
        """;
}
