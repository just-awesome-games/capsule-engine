using Capsule.Scenes.Documents;

namespace Capsule.Tests.Documents;

public sealed class SceneDocumentRoundTripTests
{
    // The document's own members sit ahead of the entries in authored order, a nested object among them, so a
    // document an importer writes reads back as it was built, and the shipped form carries them too.
    [Fact]
    public void TopLevelMembers_RoundTripAheadOfTheEntries()
    {
        const string Json = """{"baseScene":"playable-room","camera":{"type":"game-camera","scrollCenter":[160,90]},"size":[320,180],"music":"audio/room.ogg","entities":[{"type":"coin","x":8}]}""";

        SceneDocument document = SceneDocumentFile.Parse(Json);

        Assert.Equal("playable-room", document.BaseScene);
        Assert.Equal("game-camera", document.Properties?.GetProperty("camera").GetProperty("type").GetString());
        Assert.Equal(Json, SceneDocumentFile.ToJson(document));
    }

    // Rotation is degrees in the document and sits between the position and the scale. An absent position or
    // rotation is 0 and an absent scale is identity, and the writer writes none of them at that value.
    [Fact]
    public void ATurnedEntry_RoundTrips_AndAnEntryAtItsDefaultsWritesOnlyItsType()
    {
        const string Json = """{"entities":[{"type":"spike","x":8,"rotation":-22.5,"scale":[2,1],"zIndex":3},{"type":"coin","y":16},{"type":"tile-map"}]}""";

        SceneDocument document = SceneDocumentFile.Parse(Json);

        Assert.Equal(new SceneDocumentEntry("spike", 8f, 0f, 2f, 1f, 3, RotationDegrees: -22.5f), document.Entries[0]);
        Assert.Equal(new SceneDocumentEntry("coin", 0f, 16f), document.Entries[1]);
        Assert.Equal(Json, SceneDocumentFile.ToJson(document));
    }

    // Entry fields are recognised only inside the entities array, never in the scene's own members.
    [Fact]
    public void SceneProperties_ShapedLikeAnEntry_RoundTrip()
    {
        const string Json = """{"metadata":{"type":1},"entities":[]}""";

        Assert.Equal(Json, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(Json)));
    }
}
