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
    // Every top-level key sits at its canonical place, so a document authoring all of them is a fixed
    // point of the importer, and the shipped form carries them too. A colour reads in either case or with
    // an ff alpha, and writes as lowercase "#rrggbb".
    [Fact]
    public void EverySettingsKey_RoundTripsAtItsCanonicalFieldOrder()
    {
        string json = """
            {
              "formatVersion": 8,
              "baseScene": "playable-room",
              "camera": "game-camera",
              "size": [320, 180],
              "scrollCenter": [160, 90],
              "clearColor": "#101820",
              "ambient": "#484c68",
              "sampling": "point",
              "properties": {
                "music": "audio/room.ogg",
                "floor": [1, 2]
              },
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
            document.Settings with { Properties = null });
        Assert.Equal("audio/room.ogg", document.Settings.Properties?.GetProperty("music").GetString());
        Assert.Equal(json, SceneDocumentFile.ToJson(document));
        Assert.Equal(json, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(SceneDocumentFile.ToJson(document, compact: true))));
        Assert.Equal(json, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(json.Replace("#484c68", "#484C68", StringComparison.Ordinal))));
        Assert.Equal(json, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(json.Replace("#484c68", "#484c68ff", StringComparison.Ordinal))));
    }

    // The texture is written as its whole path under Assets/, and only the last dot splits off the
    // extension. Which extensions ship is the build's allow-list to decide.
    [Theory]
    [InlineData("terrain", ".png", "terrain.png")]
    [InlineData("terrain", ".bmp", "terrain.bmp")]
    [InlineData("x.atlas", ".png", "x.atlas.png")]
    [InlineData("terrain/cave", ".png", "terrain/cave.png")]
    public void AGridsTexture_RoundTripsAsOnePath(string name, string extension, string written)
    {
        string json = SceneDocumentFile.ToJson(Drawing(new TextureHandle(name, extension)));

        Assert.Contains($"\"texture\": \"{written}\",", json, StringComparison.Ordinal);
        Assert.Equal(new TextureHandle(name, extension), TileMapOf(SceneDocumentFile.Parse(json)).Grid.Texture);
    }

    [Fact]
    public void AGridThatDrawsNothingWritesNeitherTextureNorCell()
    {
        SceneDocument document = new(
            [new TileMapPlacement(1, new TileGrid(16, 1, 1, [TileGrid.EmptyTile, new TileType { Name = "hazard" }], [1]))],
            2);

        string json = SceneDocumentFile.ToJson(document);
        SceneDocument round = SceneDocumentFile.Parse(json);

        Assert.DoesNotContain("\"texture\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"columns\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"cell\"", json, StringComparison.Ordinal);
        Assert.Equivalent(TileMapOf(document).Grid.TileTypes.ToArray(), TileMapOf(round).Grid.TileTypes.ToArray(), strict: true);
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
            "  \"formatVersion\": 8,",
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
            "            \"name\": \"empty\"",
            "          },",
            "          {",
            "            \"name\": \"ground\",",
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
    }

    // Rotation is degrees in the document and sits between the position and the scale. An absent rotation
    // or scale is identity, and the canonical form writes neither at its identity.
    [Fact]
    public void ATurnedEntry_IsAFixedPoint_AndAnUnturnedOneWritesNoRotation()
    {
        string json = """
            {
              "formatVersion": 8,
              "entities": [
                {
                  "id": 1,
                  "type": "spike",
                  "x": 8,
                  "y": 0,
                  "rotation": -22.5,
                  "scale": [2, 1],
                  "zIndex": 3
                },
                {
                  "id": 2,
                  "type": "coin",
                  "x": 16,
                  "y": 0
                }
              ],
              "nextEntityId": 3
            }

            """.ReplaceLineEndings("\n");

        SceneDocument document = SceneDocumentFile.Parse(json);

        Assert.Equal(new EntityPlacement(1, "spike", 8f, 0f, 2f, 1f, 3, RotationDegrees: -22.5f), document.Entries[0].Entity);
        Assert.Equal(new EntityPlacement(2, "coin", 16f, 0f), document.Entries[1].Entity);
        Assert.Equal(json, SceneDocumentFile.ToJson(document));
    }

    // Entry fields are recognised only inside the entities array, never in authored scene properties.
    [Fact]
    public void SceneProperties_ShapedLikeAnEntry_RoundTrip()
    {
        string json = SceneDocumentFile.ToJson(SceneDocumentFile.Parse(
            """{"formatVersion":8,"properties":{"metadata":{"type":1}},"entities":[],"nextEntityId":1}"""));

        Assert.Contains("\"metadata\": {\n      \"type\": 1\n    }", json, StringComparison.Ordinal);
        Assert.Equal(json, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(json)));
    }
}
