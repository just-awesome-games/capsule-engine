using Capsule.Assets;
using Capsule.Scenes.Documents;
using Capsule.Tests.Scenes;
using Capsule.Tiles;
using static Capsule.Tests.Documents.SceneDocumentFixtures;

namespace Capsule.Tests.Documents;

public sealed class SceneDocumentParseTests
{
    // One document, one defect: whatever the reader, the document model or a grid refuses, the
    // message names it and the failure arrives as a malformed document.
    [Theory]
    [InlineData("""{"entities": [], "nextEntityId": 1}""", "no formatVersion")]
    [InlineData("""{"formatVersion": 2, "entities": [], "nextEntityId": 1}""", "formatVersion 2 is unsupported")]
    [InlineData("""{"formatVersion": 6, "nextEntityId": 1}""", "the scene document has no entities")]
    [InlineData("""{"formatVersion": 6, "entities": null, "nextEntityId": 1}""", "the scene document has no entities")]
    [InlineData(TileMapWithoutProperties, "declares no properties")]
    [InlineData("""{"formatVersion": 6, "entities": [{"id": 1, "type": "tile-map", "x": 0, "y": 0, "properties": null}], "nextEntityId": 2}""", "declares no properties")]
    [InlineData(Grid1x1, "anchored at the world origin", "\"x\": 0", "\"x\": 8")]
    [InlineData(Grid1x1, "tileSize must be positive", "\"tileSize\": 16", "\"tileSize\": 0")]
    [InlineData(Grid1x1, "the 'tile-map' entry has no id", "\"id\": 1,", "")]
    [InlineData(Grid1x1, "columns is 4 on a grid that names no texture", "\"tileSize\": 16", "\"columns\": 4, \"tileSize\": 16")]
    public void Parse_RefusesAMalformedDocumentWithTheDefectNamed(string json, string defect, string? find = null, string? replace = null)
    {
        string text = find is null ? json : json.Replace(find, replace, StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(text));

        Assert.Contains(defect, error.Message, StringComparison.Ordinal);
    }

    // A scene setting names its key, the authored value and the form the key accepts.
    [Theory]
    [InlineData("\"clearColor\": \"#10182g\"", "clearColor is \"#10182g\"", "\"#rrggbb\"")]
    [InlineData("\"ambient\": \"#fff\"", "ambient is \"#fff\"", "\"#rrggbb\"")]
    [InlineData("\"ambient\": \"#484c6880\"", "ambient has alpha 128", "opaque")]
    [InlineData("\"size\": [0, 180]", "size is (0, 180)", "greater than zero")]
    [InlineData("\"size\": [320, -1]", "size is (320, -1)", "greater than zero")]
    [InlineData("\"sampling\": \"nearest\"", "sampling is \"nearest\"", "\"linear\" or \"point\"")]
    public void Parse_RefusesAMalformedSettingWithTheKeyAndTheAcceptedForm(string field, string defect, string fix)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse($$"""{"formatVersion": 6, {{field}}, "entities": [], "nextEntityId": 1}"""));

        Assert.Contains(defect, error.Message, StringComparison.Ordinal);
        Assert.Contains(fix, error.Message, StringComparison.Ordinal);
    }

    // A field the format does not define is a typo, which no document is read past.
    [Fact]
    public void Parse_RejectsAFieldTheFormatDoesNotDefine()
    {
        Assert.Throws<SceneDocumentFormatException>(() => SceneDocumentFile.Parse(DocumentText(extra: ""","spawn": [0, 0]""")));
    }

    // Entry order is the document's, and nothing about a tile map makes it first or unique.
    [Theory]
    [InlineData("coin")]
    [InlineData("tile-map")]
    public void Parse_AllowsATileMapAfterAnyEntry(string firstType)
    {
        string first = firstType == "tile-map"
            ? """{ "id": 1, "type": "tile-map", "x": 0, "y": 0, "properties": { "tileSize": 16, "width": 1, "height": 1, "tileTypes": [ { "type": "empty" } ], "tiles": [0] } }"""
            : """{ "id": 1, "type": "coin", "x": 0, "y": 0 }""";

        SceneDocument document = SceneDocumentFile.Parse($$"""
            {
              "formatVersion": 6,
              "entities": [
                {{first}},
                { "id": 2, "type": "tile-map", "x": 0, "y": 0,
                  "properties": { "tileSize": 16, "width": 1, "height": 1,
                                  "tileTypes": [ { "type": "empty" } ], "tiles": [0] } }
              ],
              "nextEntityId": 3
            }
            """);

        Assert.NotNull(document.Entries[1].TileMap);
        if (firstType == "tile-map")
        {
            Assert.NotNull(document.Entries[0].TileMap);
        }
        else
        {
            Assert.NotNull(document.Entries[0].Entity);
        }
    }

    [Theory]
    [InlineData("null", "[0, 1]", "the 'tile-map' entry's grid has no tileTypes")]
    [InlineData(null, "null", "the 'tile-map' entry's grid has no tiles")]
    [InlineData("""[{"type": "empty"}, null]""", "[0, 1]", "tileTypes[1] is null")]
    [InlineData(null, "[0, 7]", "tiles[1] is 7")]
    [InlineData(null, "[0, 1], \"transforms\": [0, 8]", "transforms[1] is 8")]
    // A palette field the format does not define, as a typo spells one.
    [InlineData("""[{"type": "empty"}, {"type": "ground", "sprite": "wall.png"}]""", "[0, 1]", "the 'tile-map' entry's properties are not")]
    public void Parse_RefusesAMalformedGridWithTheDefectNamed(string? tileTypes, string tiles, string expected)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(tileTypes: tileTypes, tiles: tiles)));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    // The two spellings differ only for the separator JSON itself escapes.
    [Theory]
    [InlineData("tiles", "tiles")]
    [InlineData("tiles.", "tiles.")]
    [InlineData(".png", ".png")]
    [InlineData("a//tiles.png", "a//tiles.png")]
    [InlineData("../tiles.png", "../tiles.png")]
    [InlineData("./tiles.png", "./tiles.png")]
    [InlineData("a/tiles", "a/tiles")]
    [InlineData("a/", "a/")]
    [InlineData("a\\\\tiles.png", "a\\tiles.png")]
    [InlineData(" ", " ")]
    public void Parse_RejectsATextureThatIsNotOneAssetPath(string authored, string texture)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(texture: $"\"{authored}\"")));

        Assert.Contains($"grid has texture \"{texture}\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("extension included", error.Message, StringComparison.Ordinal);
    }

    // Ids share one space with the tile-map entry's, and nextEntityId is the next one to hand out.
    // An entry with no position would otherwise be placed at the origin, which is a position the
    // file never stated. An untyped entry would reach the entity registry as "", failing at boot
    // naming nothing an author could act on. Properties are a contract per entry type, never a bag
    // the reader sets by name.
    [Theory]
    [InlineData(Coin + Coin, 3, "appears more than once")]
    [InlineData(""",{"id": 1, "type": "coin", "x": 8, "y": 0}""", 2, "entity id 1 appears more than once")]
    [InlineData(Coin, 2, "entity id 2 is not below nextEntityId 2")]
    [InlineData("", 1, "entity id 1 is not below nextEntityId 1")]
    [InlineData(", null", 2, "entities[1] is null")]
    [InlineData(""",{"id": 2, "type": "coin", "y": 0}""", 3, "entities[1] has no x")]
    [InlineData(""",{"id": 2, "type": "coin", "x": 8}""", 3, "entities[1] has no y")]
    [InlineData(""",{"id": 2, "x": 8, "y": 0}""", 3, "entity id 2 has no type")]
    [InlineData(""",{"id": 2, "type": "coin", "x": 8, "y": 0, "properties": {"value": 5}}""", 3, "the type 'coin' has no properties contract")]
    public void Parse_RefusesAMalformedEntryWithTheDefectNamed(string entities, int nextEntityId, string expected)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(entities: entities, nextEntityId: nextEntityId)));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
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

        Assert.Contains("coin", error.Message, StringComparison.Ordinal);
        Assert.Contains("128", error.Message, StringComparison.Ordinal);
        Assert.Contains("64", error.Message, StringComparison.Ordinal);
        Assert.Contains("no id", error.Message, StringComparison.Ordinal);
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

    // Terrain is drawn in world coordinates and sized by its grid's tileSize, so a scale here would
    // be a factor the engine writes back and then ignores. Asked of the field's presence rather than
    // its value: on value alone a null scale would parse and be written back without the field.
    [Theory]
    [InlineData("\"scale\": [2, 2],")]
    [InlineData("\"scale\": null,")]
    public void Parse_RejectsAScaleOnTheTileMapEntry(string scale)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(DocumentText(scale: scale)));

        Assert.Contains("anchored and unscaled", error.Message, StringComparison.Ordinal);
    }
}
