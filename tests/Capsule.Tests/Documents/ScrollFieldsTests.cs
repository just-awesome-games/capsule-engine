using System.Numerics;
using Capsule.Scenes.Documents;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Documents;

public sealed class ScrollFieldsTests
{
    // Both fields are optional and additive under the current format: a document authoring neither
    // reads as it always did, and one authoring both is written back byte for byte, the centre after
    // the version and the factor after the band.
    [Fact]
    public void ScrollCenterAndScrollFactor_RoundTripCanonicallyOnEveryEntryType()
    {
        string json = """
            {
              "formatVersion": 6,
              "scrollCenter": [
                160,
                90
              ],
              "entities": [
                {
                  "id": 1,
                  "type": "tile-map",
                  "x": 0,
                  "y": 0,
                  "zIndex": -20,
                  "scrollFactor": [
                    0.5,
                    1
                  ],
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
                  "type": "sky",
                  "x": 8,
                  "y": 0,
                  "scrollFactor": [
                    0,
                    0
                  ]
                },
                {
                  "id": 3,
                  "type": "coin",
                  "x": 0,
                  "y": 0,
                  "scale": [
                    2,
                    2
                  ],
                  "zIndex": 3,
                  "scrollFactor": [
                    1,
                    1
                  ]
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

        Assert.Equal(new Vector2(160, 90), document.Settings.ScrollCenter);
        Assert.Equal(new Vector2(0.5f, 1f), document.Entries[0].TileMap!.Value.ScrollFactor);
        Assert.Equal(new EntityPlacement(2, "sky", 8f, 0f, ScrollFactor: Vector2.Zero), document.Entries[1].Entity);

        // An authored one is a factor, an absent field is no factor, and the two survive the round
        // trip as the different documents they are.
        Assert.Equal(Vector2.One, document.Entries[2].ScrollFactor);
        Assert.Null(document.Entries[3].ScrollFactor);
        Assert.Equal(json, SceneDocumentFile.ToJson(document));
    }

    [Fact]
    public void ADocumentAuthoringNeither_WritesNeither()
    {
        SceneDocument document = new([new EntityPlacement(1, "coin", 0f, 0f)], 2);

        string json = SceneDocumentFile.ToJson(document);

        Assert.DoesNotContain("scrollCenter", json, StringComparison.Ordinal);
        Assert.DoesNotContain("scrollFactor", json, StringComparison.Ordinal);
        Assert.Null(SceneDocumentFile.Parse(json).Settings.ScrollCenter);
    }

    [Theory]
    [InlineData("[0.5]", "scrollFactor of 1 components")]
    [InlineData("[0.5, 1, 2]", "scrollFactor of 3 components")]
    public void Parse_RejectsAScrollFactorThatIsNotTwoComponents(string factor, string expected)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse($$"""
                {
                  "formatVersion": 6,
                  "entities": [
                    { "id": 1, "type": "coin", "x": 0, "y": 0, "scrollFactor": {{factor}} }
                  ],
                  "nextEntityId": 2
                }
                """));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[160]", "scrollCenter of 1 components")]
    [InlineData("[160, 90, 0]", "scrollCenter of 3 components")]
    public void Parse_RejectsAScrollCenterThatIsNotTwoComponents(string center, string expected)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse($$"""
                {
                  "formatVersion": 6,
                  "scrollCenter": {{center}},
                  "entities": [],
                  "nextEntityId": 1
                }
                """));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    // JSON has no number for them, so a non-finite component reaches the document only from code and
    // fails it the way a bad scale does.
    [Fact]
    public void ANonFiniteFactorOrScrollCenter_FailsTheDocument()
    {
        ArgumentException factor = Assert.Throws<ArgumentException>(
            () => new SceneDocument([new EntityPlacement(1, "coin", 0f, 0f, ScrollFactor: new Vector2(float.NaN, 1f))], 2));
        ArgumentException center = Assert.Throws<ArgumentException>(
            () => new SceneDocument([], 1, settings: new SceneSettings { ScrollCenter = new Vector2(0f, float.PositiveInfinity) }));

        Assert.Contains("not a scroll factor", factor.Message, StringComparison.Ordinal);
        Assert.Contains("scrollCenter", center.Message, StringComparison.Ordinal);
    }

    // A grid answers queries at its authored cells, so one that scrolls collides as nothing.
    [Fact]
    public void ACollidingTileMap_RefusesAScrollFactor()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new SceneDocument(
                [new TileMapPlacement(1, SceneFixtures.TerrainGrid("#"), ScrollFactor: new Vector2(0.5f, 1f))],
                2));

        Assert.Contains("palette that collides", error.Message, StringComparison.Ordinal);

        SceneDocument decorative = new([new TileMapPlacement(1, SceneFixtures.RoomGrid(), ScrollFactor: new Vector2(0.5f, 1f))], 2);
        Assert.Equal(new Vector2(0.5f, 1f), decorative.Entries[0].ScrollFactor);
        Assert.False(decorative.Entries[0].TileMap!.Value.Grid.Collides);
    }
}
