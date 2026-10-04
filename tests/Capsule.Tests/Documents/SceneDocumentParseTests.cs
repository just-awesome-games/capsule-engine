using System.Numerics;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using static Capsule.Tests.Documents.SceneDocumentFixtures;

namespace Capsule.Tests.Documents;

public sealed class SceneDocumentParseTests
{
    // One document, one defect: whatever the reader or the document model refuses, the message names it and
    // the failure arrives as a malformed document.
    [Theory]
    [InlineData("""{}""", "the scene document has no entities")]
    [InlineData("""{"entities": null}""", "the scene document has no entities")]
    [InlineData("""{"$schema": null, "entities": []}""", "\"$schema\" is null")]
    [InlineData("""{"$schema": 7, "entities": []}""", "\"$schema\" is a number")]
    public void Parse_RefusesAMalformedDocumentWithTheDefectNamed(string json, string defect)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocument.Parse(json));

        Assert.Contains(defect, error.Message, StringComparison.Ordinal);
    }

    // An untyped entry would reach the entity registry as "", failing at boot naming nothing an author could act on.
    [Theory]
    [InlineData(Coin + Coin, "has id 2, which an earlier entry has")]
    [InlineData(""",{"id": 1, "type": "coin", "x": 8, "y": 0}""", "has id 1, which an earlier entry has")]
    [InlineData(""",{"id": 0, "type": "coin", "x": 8, "y": 0}""", "has id 0. Make it positive")]
    [InlineData(", null", "entities[1] is null")]
    [InlineData(""",{"x": 8, "y": 0}""", "entities[1] has no type")]
    [InlineData(""",{"type": "coin", "x": 8, "y": 0, "rotation": 1e39}""", "not finite")]
    public void Parse_RefusesAMalformedEntryWithTheDefectNamed(string entities, string expected)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocument.Parse(DocumentText(entities)));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[2]", "scale of 1 components")]
    [InlineData("[1, 2, 3]", "scale of 3 components")]
    [InlineData("[0, 1]", "which is not a scale")]
    [InlineData("[1, -2]", "which is not a scale")]
    public void Parse_RejectsAScaleThatIsNotTwoPositiveFactors(string scale, string expected)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocument.Parse(DocumentText($$""",{"id": 2, "type": "coin", "x": 8, "y": 0, "scale": {{scale}}}""")));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(0f, float.PositiveInfinity)]
    public void Constructor_RejectsANonFiniteEntityPosition(float x, float y)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new SceneDocument([new SceneDocumentEntry("coin", new EntitySpawn(new Vector2(x, y)))]));

        Assert.Contains("not finite", error.Message, StringComparison.Ordinal);
    }

    // An importer's member named for a reserved key would be written twice.
    [Fact]
    public void Constructor_RejectsAMemberNamedForAReservedKey()
    {
        System.Text.Json.JsonElement members = System.Text.Json.JsonDocument.Parse("""{ "x": 4 }""").RootElement;

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new SceneDocument([new SceneDocumentEntry("coin", members)]));

        Assert.Contains("member 'x', which the format reserves", error.Message, StringComparison.Ordinal);
    }
}
