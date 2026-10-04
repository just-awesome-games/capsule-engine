using System.Numerics;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Documents;

public sealed class SceneDocumentRoundTripTests
{
    // The document's own members sit ahead of the entries in authored order, a nested object among them, so a
    // document an importer writes reads back as it was built, and the shipped form carries them too.
    [Fact]
    public void TopLevelMembers_RoundTripAheadOfTheEntries()
    {
        const string Json = """{"baseScene":"playable-room","camera":{"type":"game-camera","scrollCenter":[160,90]},"size":[320,180],"music":"audio/room.ogg","entities":[{"type":"coin","x":8}]}""";

        SceneDocument document = SceneDocument.Parse(Json);

        Assert.Equal("playable-room", document.BaseScene);
        Assert.Equal("game-camera", document.Members?.GetProperty("camera").GetProperty("type").GetString());
        Assert.Equal(Json, document.ToJson());
    }

    // Rotation is degrees in the document and sits between the position and the scale. An absent position or
    // rotation is 0 and an absent scale is identity, and the writer writes none of them at that value.
    // Several degrees can read as one float of radians. The writer may then write different degrees that read back
    // as the same radians.
    [Fact]
    public void ATurnedEntry_RoundTrips_AndAnEntryAtItsDefaultsWritesOnlyItsType()
    {
        const string Json = """{"entities":[{"type":"spike","x":8,"rotation":-22.5,"scale":[2,1],"zIndex":3},{"type":"coin","y":16},{"type":"tile-map"}]}""";

        SceneDocument document = SceneDocument.Parse(Json);

        Assert.Equal(new SceneDocumentEntry("spike", new EntitySpawn(new Vector2(8f, 0f)) { Rotation = float.DegreesToRadians(-22.5f), Scale = new Vector2(2f, 1f), ZIndex = 3 }), document.Entries[0]);
        Assert.Equal(new SceneDocumentEntry("coin", new EntitySpawn(new Vector2(0f, 16f))), document.Entries[1]);
        Assert.Equal(Json, document.ToJson());

        SceneDocument turned = SceneDocument.Parse("""{"entities":[{"type":"saw","rotation":114.59156}]}""");
        Assert.Equal(2f, turned.Entries[0].Spawn.Rotation);
        Assert.Equal(turned.Entries[0], SceneDocument.Parse(turned.ToJson()).Entries[0]);
    }

    // Entry fields are recognised only inside the entities array, never in the scene's own members.
    [Fact]
    public void SceneMembers_ShapedLikeAnEntry_RoundTrip()
    {
        const string Json = """{"metadata":{"type":1},"entities":[]}""";

        Assert.Equal(Json, SceneDocument.Parse(Json).ToJson());
    }
}
