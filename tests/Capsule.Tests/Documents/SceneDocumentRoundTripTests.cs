using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes.Documents;
using Capsule.Tests.Scenes;
using Capsule.Tiles;
using static Capsule.Tests.Documents.SceneDocumentFixtures;

namespace Capsule.Tests.Documents;

public sealed class SceneDocumentRoundTripTests
{
    // A tile map is one entry among the rest, so a scene of entities alone is an ordinary document.
    [Fact]
    public void ADocumentWithNoTileMapEntry_ParsesAndRoundTripsByteForByte()
    {
        string json = """
            {
              "formatVersion": 6,
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

    // Every top-level key sits at its canonical place, so a document authoring all of them is a fixed
    // point of the importer. A colour reads in either case or with an ff alpha, and writes as lowercase
    // "#rrggbb".
    [Fact]
    public void EverySettingsKey_RoundTripsAtItsCanonicalFieldOrder()
    {
        string json = """
            {
              "formatVersion": 6,
              "baseScene": "playable-room",
              "camera": "game-camera",
              "size": [
                320,
                180
              ],
              "scrollCenter": [
                160,
                90
              ],
              "clearColor": "#101820",
              "ambient": "#484c68",
              "sampling": "point",
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

        Assert.Equal(
            new SceneSettings
            {
                BaseScene = "playable-room",
                Camera = "game-camera",
                Size = new Vector2(320, 180),
                ScrollCenter = new Vector2(160, 90),
                ClearColor = new ColorRgba(16, 24, 32),
                Ambient = new ColorRgba(72, 76, 104),
                Sampling = TextureSampling.Point,
            },
            document.Settings);
        Assert.Equal(json, SceneDocumentFile.ToJson(document));
        Assert.Equal(json, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(json.Replace("#484c68", "#484C68", StringComparison.Ordinal))));
        Assert.Equal(json, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(json.Replace("#484c68", "#484c68ff", StringComparison.Ordinal))));
    }

    // A band is the one field both entry types carry, and an unbanded entry carries none.
    [Fact]
    public void AnAuthoredBand_RoundTripsOnBothEntryTypes()
    {
        string json = """
            {
              "formatVersion": 6,
              "entities": [
                {
                  "id": 1,
                  "type": "tile-map",
                  "x": 0,
                  "y": 0,
                  "zIndex": -20,
                  "properties": {
                    "tileSize": 16,
                    "width": 1,
                    "height": 1,
                    "tileTypes": [
                      {
                        "type": "empty"
                      }
                    ],
                    "tiles": [
                      0
                    ]
                  }
                },
                {
                  "id": 2,
                  "type": "coin",
                  "x": 8,
                  "y": 0,
                  "zIndex": 7
                },
                {
                  "id": 3,
                  "type": "coin",
                  "x": 0,
                  "y": 0,
                  "zIndex": 0
                },
                {
                  "id": 4,
                  "type": "coin",
                  "x": 0,
                  "y": 0
                }
              ],
              "nextEntityId": 5
            }

            """.ReplaceLineEndings("\n");

        SceneDocument document = SceneDocumentFile.Parse(json);

        Assert.Equal(-20, document.Entries[0].TileMap!.Value.ZIndex);
        Assert.Equal(new EntityPlacement(2, "coin", 8f, 0f, ZIndex: 7), document.Entries[1].Entity);

        // An authored 0 is a band, an absent field is no band, and the two survive the round trip
        // as the different documents they are.
        Assert.Equal(0, document.Entries[2].ZIndex);
        Assert.Null(document.Entries[3].ZIndex);
        Assert.Equal(json, SceneDocumentFile.ToJson(document));
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

    // A texture under a directory of the textures root is named by that path, and the handle it
    // parses to carries it whole.
    [Fact]
    public void ANestedTexturePath_RoundTripsWhole()
    {
        string json = SceneDocumentFile.ToJson(Drawing(new TextureHandle("terrain/cave", ".png")));

        Assert.Contains("\"texture\": \"terrain/cave.png\"", json, StringComparison.Ordinal);
        Assert.Equal(
            new TextureHandle("terrain/cave", ".png"),
            SceneDocumentFile.Parse(json).Entries[0].TileMap!.Value.Grid.Texture);
    }

    [Fact]
    public void ToJson_WritesTheCanonicalForm()
    {
        SceneDocument document = new(
            [
                new TileMapPlacement(1, new TileGrid(16, 2, 1, [TileGrid.EmptyTile, SceneFixtures.Tile("ground", 0)], [0, 1], Atlas, 4)),
                new EntityPlacement(2, "coin", 8f, 0f),
            ],
            3);

        string expected = string.Join(
            '\n',
            "{",
            "  \"formatVersion\": 6,",
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
            "          0, 1",
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

    // The tiles array is written one grid row per line, so a map reads as its own shape.
    [Fact]
    public void ToJson_WritesOneLinePerGridRow()
    {
        SceneDocument document = new(
            [
                new TileMapPlacement(
                    1,
                    new TileGrid(16, 3, 2, [TileGrid.EmptyTile, SceneFixtures.Tile("ground", 0)], [0, 0, 1, 1, 1, 1], Atlas, 4)),
            ],
            2);

        string json = SceneDocumentFile.ToJson(document);

        Assert.Contains("\"tiles\": [\n          0, 0, 1,\n          1, 1, 1\n        ]", json, StringComparison.Ordinal);
        Assert.Equal([0, 0, 1, 1, 1, 1], SceneDocumentFile.Parse(json).Entries[0].TileMap!.Value.Grid.Tiles.ToArray());
    }

    [Fact]
    public void ToJson_RoundTripsADocumentWithASourceBlock()
    {
        SceneDocument document = new(
            [
                new TileMapPlacement(1, new TileGrid(8, 2, 1, [TileGrid.EmptyTile, SceneFixtures.Tile("ground", 0)], [1, 0], Atlas, 4)),
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

    // An absent scale means identity, so the canonical form carries the field only where it says
    // something, and a document that writes it must read it back the same way.
    [Fact]
    public void AScaledEntry_RoundTripsAndAnUnscaledOneWritesNoScale()
    {
        SceneDocument document = new(
            [new EntityPlacement(1, "banner", 8f, 0f, 2f, 3f), new EntityPlacement(2, "coin", 16f, 0f)],
            3);

        string json = SceneDocumentFile.ToJson(document);
        SceneDocument round = SceneDocumentFile.Parse(json);

        Assert.Contains("\"scale\": [\n        2,\n        3\n      ]", json, StringComparison.Ordinal);
        Assert.Equal(1, json.Split("\"scale\"").Length - 1);
        Assert.Equal(document.Entries[0].Entity, round.Entries[0].Entity);
        Assert.Equal(1f, round.Entries[1].Entity!.Value.ScaleX);
        Assert.Equal(1f, round.Entries[1].Entity!.Value.ScaleY);
        Assert.Equal(json, SceneDocumentFile.ToJson(round));
    }
}
