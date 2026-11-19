using System.Numerics;
using System.Text;
using Capsule.Scenes;
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
    // A scale is two positive factors.
    [Theory]
    [InlineData(Coin + Coin, "has id 2, which an earlier entry has")]
    [InlineData(""",{"id": 1, "type": "coin", "x": 8, "y": 0}""", "has id 1, which an earlier entry has")]
    [InlineData(""",{"id": 0, "type": "coin", "x": 8, "y": 0}""", "has id 0. Make it positive")]
    [InlineData(", null", "entities[1] is null")]
    [InlineData(""",{"x": 8, "y": 0}""", "entities[1] has no type")]
    [InlineData(""",{"type": "coin", "x": 8, "y": 0, "name": " "}""", "has a blank name. Write a name, or omit it.")]
    [InlineData(""",{"type": "coin", "x": 8, "y": 0, "name": "north (2)"}""", "is named 'north (2)', which ends in a number in parentheses")]
    [InlineData(""",{"type": "coin", "x": 8, "y": 0, "rotation": 1e39}""", "not finite")]
    [InlineData(""",{"type": "coin", "x": 8, "y": 0, "scrollFactor": [0.5, 1, 2]}""", "entities[1] has a scrollFactor of 3 components")]
    [InlineData(""",{"type": "coin", "x": 8, "y": 0, "tint": "#80ffffff0"}""", "entities[1] has a tint of \"#80ffffff0\", which is not a colour. Write it as \"#rrggbb\" or as \"#rrggbbaa\"")]
    [InlineData(""",{"id": 2, "type": "coin", "x": 8, "y": 0, "scale": [2]}""", "scale of 1 components")]
    [InlineData(""",{"id": 2, "type": "coin", "x": 8, "y": 0, "scale": [1, 2, 3]}""", "scale of 3 components")]
    [InlineData(""",{"id": 2, "type": "coin", "x": 8, "y": 0, "scale": [0, 1]}""", "which is not a scale")]
    [InlineData(""",{"id": 2, "type": "coin", "x": 8, "y": 0, "scale": [1, -2]}""", "which is not a scale")]
    public void Parse_RefusesAMalformedEntryWithTheDefectNamed(string entities, string expected)
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocument.Parse(DocumentText(entities)));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    // A member name reads as the name it spells, escaped or not and however long, and an escaped declared field
    // stays that field.
    [Fact]
    public void Parse_ReadsAnEscapedMemberNameAsTheNameItSpells()
    {
        string longName = new('k', 200);
        SceneDocument document = SceneDocument.Parse($$"""{"entities": [{"type": "coin", "\u0078": 8, "sp\u0065ed": 1, "{{longName}}": 2}]}""");

        Assert.Equal(8f, document.Entries[0].Spawn.Position.X);
        Assert.Equal(["speed", longName], document.Entries[0].Authored!.Keys);
    }

    // A member name that is not UTF-8, on an entry or on the document, is refused naming where it sits, never read as
    // a replacement character.
    [Theory]
    [InlineData("""{"entities": [{"type": "coin"}, {"type": "coin", "x": 0, "y": 0, "sp?ed": 1}]}""", "entities[1]")]
    [InlineData("""{"entities": [], "sp?ed": 1}""", "the document")]
    public void Parse_RefusesAMemberNameThatIsNotUtf8(string json, string holder)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        utf8[Array.IndexOf(utf8, (byte)'?')] = 0xFF;

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(() => SceneDocumentJson.Read(utf8));
        Assert.Equal($"malformed scene document JSON: {holder} has a member name that is not UTF-8. Save the file as UTF-8.", error.Message);
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    // The document model's refusal reaches an author as the defect and its fix, with no argument name appended.
    [Fact]
    public void Parse_RefusesADuplicateIdWithTheDefectAlone()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocument.Parse(DocumentText(""",{"id": 1, "type": "coin", "x": 8, "y": 0}""")));

        Assert.Equal("entities[1] ('coin' at (8, 0)) has id 1, which an earlier entry has. Give every entry a unique id.", error.Message);
    }

    // Each finite value is accepted, however large their sum.
    [Fact]
    public void Constructor_AcceptsLargeFinitePlacements()
    {
        SceneDocument document = new([new SceneDocumentEntry("coin", new EntitySpawn(new Vector2(2e38f, 2e38f)) { ScrollFactor = new Vector2(3e38f, 3e38f) })]);

        Assert.Equal(new Vector2(2e38f, 2e38f), document.Entries[0].Spawn.Position);
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
