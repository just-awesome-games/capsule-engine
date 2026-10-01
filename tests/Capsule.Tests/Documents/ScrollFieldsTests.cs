using System.Numerics;
using Capsule.Scenes.Documents;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Documents;

public sealed class ScrollFieldsTests
{
    // The centre sits after the version, and the band and the factor after the scale on either entry type.
    [Fact]
    public void ScrollCenterBandAndScrollFactor_RoundTripCanonicallyOnEveryEntryType()
    {
        string json = """
            {
              "formatVersion": 8,
              "scrollCenter": [160, 90],
              "entities": [
                {
                  "id": 1,
                  "type": "tile-map",
                  "x": 0,
                  "y": 0,
                  "zIndex": -20,
                  "scrollFactor": [0.5, 1],
                  "properties": {
                    "tileSize": 16,
                    "width": 1,
                    "height": 1,
                    "tileTypes": [
                      {
                        "name": "empty"
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
                  "scrollFactor": [0, 0]
                },
                {
                  "id": 3,
                  "type": "coin",
                  "x": 0,
                  "y": 0,
                  "scale": [2, 2],
                  "zIndex": 0,
                  "scrollFactor": [1, 1]
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
        Assert.Equal((-20, new Vector2(0.5f, 1f)), (document.Entries[0].ZIndex, document.Entries[0].ScrollFactor));
        Assert.Equal(new EntityPlacement(2, "sky", 8f, 0f, ScrollFactor: Vector2.Zero), document.Entries[1].Entity);

        // An authored identity is a value, an absent field is none, and the two survive the round trip
        // as the different documents they are.
        Assert.Equal((0, Vector2.One), (document.Entries[2].ZIndex, document.Entries[2].ScrollFactor));
        Assert.Equal((null, null), (document.Entries[3].ZIndex, document.Entries[3].ScrollFactor));
        Assert.Equal(json, SceneDocumentFile.ToJson(document));
    }

    [Theory]
    [InlineData("\"scrollCenter\": [160], \"entities\": []", "the scene document has a scrollCenter of 1 components")]
    [InlineData("\"entities\": [{\"id\": 1, \"type\": \"coin\", \"x\": 0, \"y\": 0, \"scrollFactor\": [0.5, 1, 2]}]", "entities[0] has a scrollFactor of 3 components")]
    public void Parse_RejectsAPairThatIsNotTwoComponents(string fields, string expected)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse($$"""{"formatVersion": 8, {{fields}}, "nextEntityId": 2}"""));

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

    // A grid answers queries at its authored cells, so a map with a collider refuses a scroll factor. A
    // collider on a palette naming no layer could never collide. A layered palette without one scrolls.
    [Fact]
    public void ATileMapWithACollider_RefusesAScrollFactorAndALayerlessPalette()
    {
        ArgumentException scrolled = Assert.Throws<ArgumentException>(
            () => new SceneDocument(
                [new TileMapPlacement(1, SceneFixtures.TerrainGrid("#"), ScrollFactor: new Vector2(0.5f, 1f), HasCollider: true)],
                2));
        ArgumentException layerless = Assert.Throws<ArgumentException>(
            () => new SceneDocument([new TileMapPlacement(1, SceneFixtures.RoomGrid(), HasCollider: true)], 2));

        Assert.Contains("drop collider from its properties", scrolled.Message, StringComparison.Ordinal);
        Assert.Contains("names no layer", layerless.Message, StringComparison.Ordinal);

        SceneDocument decorative = new([new TileMapPlacement(1, SceneFixtures.TerrainGrid("#"), ScrollFactor: new Vector2(0.5f, 1f))], 2);
        Assert.Equal(new Vector2(0.5f, 1f), decorative.Entries[0].ScrollFactor);
    }
}
